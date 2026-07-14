#if UNITY_EDITOR
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexExcavatorPoseBakeUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexExcavatorPoseBake.request";
    private const string OutputDirectory = "Temp/CodexExcavatorPoseBake";
    private const string DefaultSnapshotPath = "Temp/CodexExcavatorPoseSnapshot/playmode_target_pose.json";
    private const string BackupDirectory = "CodexSceneBackups";
    private const string YuLongNormalizationProfilePath =
      "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/YuLong_norm.json";
    private const int MaxReportedMissingPaths = 40;
    private const int DefaultResetQposBurnInSteps = 3;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorPoseBakeUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Bake Excavator Pose Snapshot To Scene" )]
    public static void BakePoseSnapshotFromMenu()
    {
      if ( EditorApplication.isPlayingOrWillChangePlaymode ) {
        EditorApplication.isPlaying = false;
        WriteResult( new PoseBakeResult {
          success = false,
          message = "Editor is in Play Mode; requested exit. Run the bake command again after Edit Mode is restored.",
          source = "menu",
          requested_exit_play_mode = true,
          snapshot_path = DefaultSnapshotPath
        } );
        return;
      }

      BakePoseSnapshot( DefaultSnapshotPath, "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 0.5;

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      var requestText = string.Empty;
      try {
        requestText = File.ReadAllText( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new PoseBakeResult {
          success = false,
          message = "Could not read request file: " + exception.Message,
          source = "request-file"
        } );
        return;
      }

      var snapshotPath = ParseRequestValue( requestText, "snapshot_path", DefaultSnapshotPath );

      if ( EditorApplication.isPlayingOrWillChangePlaymode ) {
        if ( EditorApplication.isPlaying )
          EditorApplication.isPlaying = false;

        WriteResult( new PoseBakeResult {
          success = false,
          message = "Editor is in Play Mode; requested exit and will bake after returning to Edit Mode.",
          source = "request-file",
          requested_exit_play_mode = true,
          snapshot_path = snapshotPath
        } );
        return;
      }

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new PoseBakeResult {
          success = false,
          message = "Could not delete request file: " + exception.Message,
          source = "request-file",
          snapshot_path = snapshotPath
        } );
        return;
      }

      BakePoseSnapshot( snapshotPath, "request-file" );
    }

    private static void BakePoseSnapshot( string snapshotPath, string source )
    {
      s_isRunning = true;
      var result = new PoseBakeResult {
        source = source,
        snapshot_path = snapshotPath
      };

      try {
        var absoluteSnapshotPath = GetProjectRelativeAbsolutePath( snapshotPath );
        if ( !File.Exists( absoluteSnapshotPath ) ) {
          result.message = "Pose snapshot file was not found: " + absoluteSnapshotPath;
          WriteResult( result );
          return;
        }

        var snapshot = JsonUtility.FromJson<PoseSnapshotResult>( File.ReadAllText( absoluteSnapshotPath ) );
        if ( snapshot == null || !snapshot.success ) {
          result.message = "Pose snapshot is missing or was not captured successfully.";
          WriteResult( result );
          return;
        }

        result.snapshot_unity_is_playing = snapshot.unity_is_playing;
        result.snapshot_excavator_root = snapshot.excavator_root;
        result.snapshot_rigid_body_count = snapshot.rigid_body_count;
        result.snapshot_constraint_count = snapshot.constraint_count;
        result.snapshot_key_transform_count = snapshot.key_transform_count;

        var scene = EnsureTargetSceneIsAvailable( result );
        if ( !scene.IsValid() ) {
          WriteResult( result );
          return;
        }

        result.scene_path = scene.path;
        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          result.message = "Excavator root was not found.";
          WriteResult( result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        SaveBackupBeforeBake( scene, result );

        var appliedTransformPaths = new HashSet<string>( StringComparer.Ordinal );
        var transformSamples = CollectTransformSamples( snapshot );
        transformSamples.Sort( ( lhs, rhs ) => CountPathSegments( lhs.path ).CompareTo( CountPathSegments( rhs.path ) ) );

        foreach ( var transformSample in transformSamples ) {
          if ( transformSample == null || string.IsNullOrEmpty( transformSample.path ) )
            continue;

          var transform = ResolveSnapshotTransformPath( transformSample.path, root.transform, snapshot.excavator_root );
          if ( transform == null ) {
            AddCappedMissingPath( result.missing_transform_paths, transformSample.path );
            continue;
          }

          ApplyTransformSample( transform, transformSample, appliedTransformPaths, result );
        }

        foreach ( var constraintSample in snapshot.constraints ) {
          if ( constraintSample == null || string.IsNullOrEmpty( constraintSample.path ) )
            continue;

          var transform = ResolveSnapshotTransformPath( constraintSample.path, root.transform, snapshot.excavator_root );
          var constraint = transform != null ? transform.GetComponent<Constraint>() : null;
          if ( constraint == null ) {
            AddCappedMissingPath( result.missing_constraint_paths, constraintSample.path );
            continue;
          }

          ApplyConstraintSample( constraint, constraintSample, appliedTransformPaths, result );
        }

        ConfigureResetPoseQpos( snapshot, result );

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );
        AssetDatabase.SaveAssets();

        result.success = result.missing_constraint_paths.Count == 0 &&
                         result.controller_type_mismatches.Count == 0 &&
                         result.parse_failures.Count == 0;
        result.message = $"Baked {result.transform_samples_applied} transform samples, {result.constraints_applied} constraints, " +
                         $"{result.constraint_frames_applied} constraint frames, and {result.controllers_applied} controller values.";
        if ( result.reset_qpos_applied )
          result.message += $" Configured reset qpos {result.reset_qpos}.";
        else if ( !string.IsNullOrWhiteSpace( result.reset_qpos_warning ) )
          result.message += $" Reset qpos was not configured: {result.reset_qpos_warning}.";
        if ( result.controllers_skipped > 0 )
          result.message += $" Skipped {result.controllers_skipped} controller value(s) that were not present in the snapshot.";
        if ( !result.success )
          result.message += " Inspect missing paths, controller mismatches, or parse failures.";

        WriteResult( result );
      }
      catch ( System.Exception exception ) {
        result.success = false;
        result.message = exception.ToString();
        WriteResult( result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void ConfigureResetPoseQpos( PoseSnapshotResult snapshot, PoseBakeResult result )
    {
      if ( !TryBuildYuLongResetQpos( snapshot, out var qpos, out var warning ) ) {
        result.reset_qpos_warning = warning;
        return;
      }

      var servers = UnityEngine.Object.FindObjectsByType<AgxSimStepAckServer>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );
      if ( servers == null || servers.Length == 0 ) {
        result.reset_qpos_warning = "AgxSimStepAckServer was not found in the scene.";
        return;
      }

      var server = servers[ 0 ];
      Undo.RecordObject( server, "Configure Excavator Reset QPos" );
      server.ConfigureResetPoseQpos( qpos, DefaultResetQposBurnInSteps );
      EditorUtility.SetDirty( server );
      PrefabUtility.RecordPrefabInstancePropertyModifications( server );

      result.reset_qpos_applied = true;
      result.reset_qpos = FormatFloatArray( qpos );
      result.reset_qpos_burn_in_steps = DefaultResetQposBurnInSteps;
    }

    private static bool TryBuildYuLongResetQpos( PoseSnapshotResult snapshot,
                                                 out float[] qpos,
                                                 out string warning )
    {
      qpos = null;
      warning = string.Empty;

      if ( snapshot?.constraints == null || snapshot.constraints.Count == 0 ) {
        warning = "snapshot has no constraints.";
        return false;
      }

      var hasSwing = TryGetConstraintAngle( snapshot, "swing_joint", out var swingRaw );
      var hasBoom = TryGetConstraintAngle( snapshot, "boom_joint", out var boomRaw );
      var hasStick = TryGetConstraintAngle( snapshot, "stick_joint", out var stickRaw );
      var hasBucket = TryGetConstraintAngle( snapshot, "bucket_joint", out var bucketRaw );
      if ( !hasSwing || !hasBoom || !hasStick || !hasBucket ) {
        warning = "snapshot does not contain current angles for swing/boom/stick/bucket joints.";
        return false;
      }

      var profilePath = GetProjectRelativeAbsolutePath( YuLongNormalizationProfilePath );
      if ( !File.Exists( profilePath ) ) {
        warning = $"YuLong normalization profile was not found: {YuLongNormalizationProfilePath}";
        return false;
      }

      var profile = JsonUtility.FromJson<ActuatorNormalizationProfile>( File.ReadAllText( profilePath ) );
      if ( profile == null ) {
        warning = "YuLong normalization profile could not be parsed.";
        return false;
      }

      qpos = new[]
      {
        NormalizeAxis( swingRaw, profile.swing ),
        NormalizeAxis( boomRaw, profile.boom ),
        NormalizeAxis( stickRaw, profile.stick ),
        NormalizeAxis( bucketRaw, profile.bucket )
      };
      return true;
    }

    private static bool TryGetConstraintAngle( PoseSnapshotResult snapshot, string name, out float angle )
    {
      angle = 0.0f;
      foreach ( var constraint in snapshot.constraints ) {
        if ( constraint == null )
          continue;

        if ( !string.Equals( constraint.name, name, StringComparison.OrdinalIgnoreCase ) )
          continue;

        return float.TryParse( constraint.current_angle,
                               NumberStyles.Float,
                               CultureInfo.InvariantCulture,
                               out angle );
      }

      return false;
    }

    private static float NormalizeAxis( float rawAngle, ActuatorNormalizationAxisProfile profile )
    {
      if ( profile == null || Mathf.Abs( profile.max - profile.min ) < 1.0e-5f )
        return 0.0f;

      return Mathf.Clamp01( Mathf.InverseLerp( profile.min, profile.max, rawAngle ) );
    }

    private static string FormatFloatArray( float[] values )
    {
      if ( values == null || values.Length == 0 )
        return string.Empty;

      var parts = new string[ values.Length ];
      for ( var index = 0; index < values.Length; ++index )
        parts[ index ] = values[ index ].ToString( "0.######", CultureInfo.InvariantCulture );

      return "[" + string.Join( ", ", parts ) + "]";
    }

    private static Scene EnsureTargetSceneIsAvailable( PoseBakeResult result )
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        result.message = $"Active scene '{activeScene.path}' has unsaved changes; pose bake did not switch scenes.";
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static void SaveBackupBeforeBake( Scene scene, PoseBakeResult result )
    {
      EditorSceneManager.SaveScene( scene );

      var backupDirectory = GetProjectRelativeAbsolutePath( BackupDirectory );
      Directory.CreateDirectory( backupDirectory );

      var fileName = Path.GetFileNameWithoutExtension( ScenePath ) +
                     "_before_excavator_pose_bake_" +
                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                     ".unity";
      var backupPath = Path.Combine( backupDirectory, fileName );
      File.Copy( GetProjectRelativeAbsolutePath( ScenePath ), backupPath, true );
      result.backup_path = backupPath;
    }

    private static List<TransformSample> CollectTransformSamples( PoseSnapshotResult snapshot )
    {
      var samples = new List<TransformSample>();

      if ( snapshot.root_transform != null )
        samples.Add( snapshot.root_transform );

      foreach ( var rigidBody in snapshot.rigid_bodies ) {
        if ( rigidBody != null && rigidBody.transform != null )
          samples.Add( rigidBody.transform );
      }

      foreach ( var keyTransform in snapshot.key_transforms ) {
        if ( keyTransform != null )
          samples.Add( keyTransform );
      }

      foreach ( var constraint in snapshot.constraints ) {
        if ( constraint != null && constraint.transform != null )
          samples.Add( constraint.transform );
      }

      return samples;
    }

    private static bool ApplyTransformSample( Transform transform,
                                              TransformSample sample,
                                              HashSet<string> appliedTransformPaths,
                                              PoseBakeResult result )
    {
      if ( !appliedTransformPaths.Add( sample.path ) )
        return false;

      if ( !TryParseVector( sample.local_position, out var localPosition ) ||
           !TryParseVector( sample.local_euler, out var localEuler ) ||
           !TryParseVector( sample.local_scale, out var localScale ) ) {
        result.parse_failures.Add( "Transform parse failed: " + sample.path );
        return false;
      }

      Undo.RecordObject( transform, "Bake Excavator Pose Snapshot" );
      transform.localPosition = localPosition;
      transform.localEulerAngles = localEuler;
      transform.localScale = localScale;
      EditorUtility.SetDirty( transform );
      PrefabUtility.RecordPrefabInstancePropertyModifications( transform );
      result.transform_samples_applied++;
      return true;
    }

    private static void ApplyConstraintSample( Constraint constraint,
                                               ConstraintSample sample,
                                               HashSet<string> appliedTransformPaths,
                                               PoseBakeResult result )
    {
      if ( sample.transform != null )
        ApplyTransformSample( constraint.transform, sample.transform, appliedTransformPaths, result );

      Undo.RecordObject( constraint, "Bake Excavator Pose Snapshot" );
      var changed = false;

      var pair = constraint.AttachmentPair;
      if ( pair != null ) {
        changed |= ApplyFrameSample( pair.ReferenceFrame, sample.reference_frame, result );
        changed |= ApplyFrameSample( pair.ConnectedFrame, sample.connected_frame, result );
      }

      changed |= ApplyControllerSamples( constraint, sample, result );

      if ( changed ) {
        result.constraints_applied++;
        EditorUtility.SetDirty( constraint );
        PrefabUtility.RecordPrefabInstancePropertyModifications( constraint );
      }
    }

    private static bool ApplyFrameSample( ConstraintFrame frame, ConstraintFrameSample sample, PoseBakeResult result )
    {
      if ( frame == null || sample == null )
        return false;

      if ( !TryParseVector( sample.local_position, out var localPosition ) ||
           !TryParseVector( sample.local_euler, out var localEuler ) ) {
        result.parse_failures.Add( "Constraint frame parse failed." );
        return false;
      }

      frame.LocalPosition = localPosition;
      frame.LocalRotation = Quaternion.Euler( localEuler );
      result.constraint_frames_applied++;
      return true;
    }

    private static bool ApplyControllerSamples( Constraint constraint, ConstraintSample sample, PoseBakeResult result )
    {
      if ( sample.controllers == null || sample.controllers.Count == 0 )
        return false;

      var changed = false;
      var controllers = constraint.GetElementaryConstraintControllers();
      var count = Math.Min( controllers.Length, sample.controllers.Count );
      for ( var index = 0; index < count; ++index ) {
        var controller = controllers[ index ];
        var controllerSample = sample.controllers[ index ];
        if ( controller == null || controllerSample == null )
          continue;

        if ( controller.GetType().Name != controllerSample.type ) {
          result.controller_type_mismatches.Add( sample.path + "[" + index + "]: " +
                                                controller.GetType().Name + " != " + controllerSample.type );
          continue;
        }

        if ( ApplyControllerSample( controller, controllerSample, result ) )
          changed = true;
      }

      return changed;
    }

    private static bool ApplyControllerSample( ElementaryConstraintController controller,
                                               ConstraintControllerSample sample,
                                               PoseBakeResult result )
    {
      if ( controller is LockController lockController ) {
        if ( TryParseKeyedFloat( sample.value, "position", out var position ) ) {
          lockController.Position = position;
          result.controllers_applied++;
          return true;
        }
      }
      else if ( controller is RangeController rangeController ) {
        if ( TryParseKeyedRange( sample.value, "range", out var range ) ) {
          rangeController.Range = range;
          result.controllers_applied++;
          return true;
        }
      }
      else if ( controller is TargetSpeedController speedController ) {
        if ( TryParseKeyedFloat( sample.value, "speed", out var speed ) ) {
          speedController.Speed = speed;
          result.controllers_applied++;
          return true;
        }
      }
      else if ( controller is ElectricMotorController motorController ) {
        if ( string.IsNullOrWhiteSpace( sample.value ) ) {
          result.controllers_skipped++;
          return false;
        }

        if ( TryParseKeyedFloat( sample.value, "voltage", out var voltage ) &&
             TryParseKeyedFloat( sample.value, "armature_resistance", out var armatureResistance ) &&
             TryParseKeyedFloat( sample.value, "torque_constant", out var torqueConstant ) ) {
          motorController.Voltage = voltage;
          motorController.ArmatureResistance = armatureResistance;
          motorController.TorqueConstant = torqueConstant;
          result.controllers_applied++;
          return true;
        }
      }
      else if ( controller is FrictionController frictionController ) {
        if ( string.IsNullOrWhiteSpace( sample.value ) ) {
          result.controllers_skipped++;
          return false;
        }

        if ( TryParseKeyedFloat( sample.value, "friction_coefficient", out var frictionCoefficient ) &&
             TryParseKeyedBool( sample.value, "non_linear_direct_solve_enabled", out var nonLinearDirectSolveEnabled ) &&
             TryParseKeyedRange( sample.value, "minimum_static_friction_force_range", out var minimumStaticFrictionForceRange ) ) {
          frictionController.FrictionCoefficient = frictionCoefficient;
          frictionController.NonLinearDirectSolveEnabled = nonLinearDirectSolveEnabled;
          frictionController.MinimumStaticFrictionForceRange = minimumStaticFrictionForceRange;
          result.controllers_applied++;
          return true;
        }
      }
      else if ( controller is ScrewController screwController ) {
        if ( TryParseKeyedFloat( sample.value, "lead", out var lead ) ) {
          screwController.Lead = lead;
          result.controllers_applied++;
          return true;
        }
      }

      if ( string.IsNullOrWhiteSpace( sample.value ) ) {
        result.controllers_skipped++;
        return false;
      }

      result.parse_failures.Add( "Controller parse failed: " + sample.type + " " + sample.value );
      return false;
    }

    private static Transform ResolveSnapshotTransformPath( string snapshotPath, Transform root, string snapshotRootPath )
    {
      if ( root == null || string.IsNullOrEmpty( snapshotPath ) )
        return null;

      if ( snapshotPath == snapshotRootPath )
        return root;

      if ( !string.IsNullOrEmpty( snapshotRootPath ) &&
           snapshotPath.StartsWith( snapshotRootPath + "/", StringComparison.Ordinal ) ) {
        var relativePath = snapshotPath.Substring( snapshotRootPath.Length + 1 );
        return FindRelativeTransform( root, relativePath );
      }

      var currentRootPath = GetHierarchyPath( root.gameObject );
      if ( snapshotPath.StartsWith( currentRootPath + "/", StringComparison.Ordinal ) ) {
        var relativePath = snapshotPath.Substring( currentRootPath.Length + 1 );
        return FindRelativeTransform( root, relativePath );
      }

      return null;
    }

    private static Transform FindRelativeTransform( Transform root, string relativePath )
    {
      var current = root;
      foreach ( var part in relativePath.Split( '/' ) ) {
        if ( string.IsNullOrEmpty( part ) )
          continue;

        var child = current.Find( part );
        if ( child == null )
          return null;

        current = child;
      }

      return current;
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var preferredYuLong = ResolveYuLongRoot();
      if ( preferredYuLong != null )
        return preferredYuLong;

      var preferredBobcat = FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
                            FindSceneObject( "Excavator_BobcatE85" );
      if ( preferredBobcat != null )
        return preferredBobcat;

      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        var type = component.GetType();
        var typeName = type.FullName ?? type.Name;
        if ( typeName != "AGXUnity.Excavator" && type.Name != "ExcavatorE85" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          return prefabRoot;

        var transform = component.transform;
        while ( transform.parent != null &&
                transform.parent.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          transform = transform.parent;

        return transform.gameObject;
      }

      return FindSceneObject( "Excavator CAT 365 Tracked" ) ??
             FindSceneObject( "Excavator" );
    }

    private static GameObject ResolveYuLongRoot()
    {
      var namedYuLongRoot = FindSceneObject( "remake3" );
      if ( namedYuLongRoot != null )
        return namedYuLongRoot;

      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        if ( component.GetType().Name != "ExcavatorYuLong" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null &&
             prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 &&
             prefabRoot.name != "=== Scene ===" )
          return prefabRoot;

        var transform = component.transform;
        while ( transform.parent != null &&
                transform.parent.name != "=== Scene ===" &&
                transform.parent.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          transform = transform.parent;

        return transform.gameObject;
      }

      return FindSceneObject( "remake3" );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == objectName )
          return gameObject;
      }

      return null;
    }

    private static string GetHierarchyPath( GameObject gameObject )
    {
      if ( gameObject == null )
        return string.Empty;

      var names = new List<string>();
      var current = gameObject.transform;
      while ( current != null ) {
        names.Add( current.name );
        current = current.parent;
      }

      names.Reverse();
      return string.Join( "/", names );
    }

    private static string ParseRequestValue( string requestText, string key, string fallback )
    {
      foreach ( var line in requestText.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries ) ) {
        var trimmed = line.Trim();
        if ( trimmed.StartsWith( key + "=", StringComparison.OrdinalIgnoreCase ) )
          return trimmed.Substring( key.Length + 1 ).Trim();
      }

      return fallback;
    }

    private static bool TryParseVector( string text, out Vector3 value )
    {
      value = Vector3.zero;
      if ( string.IsNullOrWhiteSpace( text ) )
        return false;

      var trimmed = text.Trim();
      if ( trimmed.StartsWith( "(", StringComparison.Ordinal ) )
        trimmed = trimmed.Substring( 1 );
      if ( trimmed.EndsWith( ")", StringComparison.Ordinal ) )
        trimmed = trimmed.Substring( 0, trimmed.Length - 1 );

      var parts = trimmed.Split( ',' );
      if ( parts.Length != 3 )
        return false;

      if ( !TryParseFloat( parts[ 0 ], out var x ) ||
           !TryParseFloat( parts[ 1 ], out var y ) ||
           !TryParseFloat( parts[ 2 ], out var z ) )
        return false;

      value = new Vector3( x, y, z );
      return true;
    }

    private static bool TryParseKeyedFloat( string text, string key, out float value )
    {
      value = 0f;
      var stringValue = FindKeyedValue( text, key );
      if ( stringValue == null )
        return false;

      return TryParseFloat( stringValue, out value );
    }

    private static bool TryParseKeyedBool( string text, string key, out bool value )
    {
      value = false;
      var stringValue = FindKeyedValue( text, key );
      if ( stringValue == null )
        return false;

      if ( string.Equals( stringValue, "true", StringComparison.OrdinalIgnoreCase ) ) {
        value = true;
        return true;
      }

      if ( string.Equals( stringValue, "false", StringComparison.OrdinalIgnoreCase ) ) {
        value = false;
        return true;
      }

      return false;
    }

    private static bool TryParseKeyedRange( string text, string key, out RangeReal range )
    {
      range = new RangeReal( float.NegativeInfinity, float.PositiveInfinity );
      var stringValue = FindKeyedValue( text, key );
      if ( stringValue == null )
        return false;

      return TryParseRangeValue( stringValue, out range );
    }

    private static bool TryParseRangeValue( string text, out RangeReal range )
    {
      range = new RangeReal( float.NegativeInfinity, float.PositiveInfinity );
      if ( string.IsNullOrWhiteSpace( text ) )
        return false;

      var trimmed = text.Trim();
      if ( trimmed.StartsWith( "(", StringComparison.Ordinal ) )
        trimmed = trimmed.Substring( 1 );
      if ( trimmed.EndsWith( ")", StringComparison.Ordinal ) )
        trimmed = trimmed.Substring( 0, trimmed.Length - 1 );

      var parts = trimmed.Split( ',' );
      if ( parts.Length != 2 )
        return false;

      if ( !TryParseFloat( parts[ 0 ], out var min ) ||
           !TryParseFloat( parts[ 1 ], out var max ) )
        return false;

      range = new RangeReal( min, max );
      return true;
    }

    private static string FindKeyedValue( string text, string key )
    {
      if ( string.IsNullOrWhiteSpace( text ) )
        return null;

      var prefix = key + "=";
      foreach ( var segment in text.Split( ';' ) ) {
        var trimmed = segment.Trim();
        if ( trimmed.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
          return trimmed.Substring( prefix.Length ).Trim();
      }

      return null;
    }

    private static bool TryParseFloat( string text, out float value )
    {
      return float.TryParse( text.Trim(),
                             NumberStyles.Float,
                             CultureInfo.InvariantCulture,
                             out value );
    }

    private static void AddCappedMissingPath( List<string> missingPaths, string path )
    {
      if ( missingPaths.Count < MaxReportedMissingPaths )
        missingPaths.Add( path );
    }

    private static int CountPathSegments( string path )
    {
      if ( string.IsNullOrEmpty( path ) )
        return 0;

      var count = 1;
      foreach ( var character in path ) {
        if ( character == '/' )
          count++;
      }
      return count;
    }

    private static void WriteResult( PoseBakeResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      if ( Path.IsPathRooted( projectRelativePath ) )
        return projectRelativePath;

      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    [Serializable]
    private sealed class PoseBakeResult
    {
      public bool success;
      public string message;
      public string source;
      public string snapshot_path;
      public string scene_path;
      public string backup_path;
      public bool requested_exit_play_mode;
      public bool snapshot_unity_is_playing;
      public string snapshot_excavator_root;
      public string excavator_root;
      public int snapshot_rigid_body_count;
      public int snapshot_constraint_count;
      public int snapshot_key_transform_count;
      public int transform_samples_applied;
      public int constraints_applied;
      public int constraint_frames_applied;
      public int controllers_applied;
      public int controllers_skipped;
      public bool reset_qpos_applied;
      public string reset_qpos;
      public string reset_qpos_warning;
      public int reset_qpos_burn_in_steps;
      public List<string> missing_transform_paths = new List<string>();
      public List<string> missing_constraint_paths = new List<string>();
      public List<string> controller_type_mismatches = new List<string>();
      public List<string> parse_failures = new List<string>();
    }

    [Serializable]
    private sealed class PoseSnapshotResult
    {
      public bool success;
      public string message;
      public string source;
      public bool unity_is_playing;
      public string realtime_since_startup_s;
      public string scene_path;
      public string excavator_root;
      public TransformSample root_transform;
      public BoundsSample overall_bounds = new BoundsSample();
      public BoundsSample bucket_bounds = new BoundsSample();
      public int rigid_body_count;
      public int constraint_count;
      public int key_transform_count;
      public List<RigidBodySample> rigid_bodies = new List<RigidBodySample>();
      public List<ConstraintSample> constraints = new List<ConstraintSample>();
      public List<TransformSample> key_transforms = new List<TransformSample>();
    }

    [Serializable]
    private sealed class TransformSample
    {
      public string name;
      public string path;
      public string world_position;
      public string world_euler;
      public string local_position;
      public string local_euler;
      public string local_scale;
      public string lossy_scale;
    }

    [Serializable]
    private sealed class RigidBodySample
    {
      public string name;
      public string path;
      public string motion_control;
      public TransformSample transform;
    }

    [Serializable]
    private sealed class ConstraintSample
    {
      public string name;
      public string path;
      public string type;
      public bool synchronized;
      public string reference_object;
      public string connected_object;
      public string reference_body;
      public string connected_body;
      public string frame_position_distance_m;
      public string frame_axis_angle_deg;
      public string current_angle;
      public string current_speed;
      public TransformSample transform;
      public ConstraintFrameSample reference_frame;
      public ConstraintFrameSample connected_frame;
      public List<ConstraintControllerSample> controllers = new List<ConstraintControllerSample>();
    }

    [Serializable]
    private sealed class ConstraintFrameSample
    {
      public string local_position;
      public string local_euler;
      public string world_position;
      public string world_euler;
    }

    [Serializable]
    private sealed class ConstraintControllerSample
    {
      public string type;
      public string controller_type;
      public string value;
    }

    [Serializable]
    private sealed class BoundsSample
    {
      public string center;
      public string size;
      public string min;
      public string max;
    }
  }
}
#pragma warning restore 0649
#endif
