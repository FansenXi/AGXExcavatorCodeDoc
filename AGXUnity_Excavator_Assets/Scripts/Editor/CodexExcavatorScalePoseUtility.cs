#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity.Collide;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexExcavatorScalePoseUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorScalePose.request";
    private const string OutputDirectory = "Temp/CodexExcavatorScalePose";
    private const float RequestedScale = 0.8f;
    private const float AlreadyScaledTrackLengthThreshold = 2.4f;
    private const float TargetTrackMinY = 0.05f;
    private const float TargetBucketMinY = 1.05f;
    private const float FactoryCeilingSafetyY = 3.45f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorScalePoseUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Bake Scale E85 To 80 Percent And Raise Bucket" )]
    public static void BakeScaleAndPoseFromMenu()
    {
      BakeScaleAndPose( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}", null );
        return;
      }

      BakeScaleAndPose( "request-file" );
    }

    private static void BakeScaleAndPose( string source )
    {
      s_isRunning = true;
      var result = new ScalePoseResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          WriteResult( false, "Excavator root was not found.", result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        result.scene_backup_path = BackupSceneFile();

        var beforeRenderers = CollectMeasuredRenderers( root );
        var beforeOverall = CalculateBounds( beforeRenderers );
        var beforeTracks = CalculateTrackBounds( beforeRenderers, beforeOverall );
        var beforeBucket = CalculateBucketBounds( beforeRenderers );
        FillBoundsResult( beforeOverall, result.before_overall );
        FillBoundsResult( beforeTracks, result.before_tracks );
        FillBoundsResult( beforeBucket, result.before_bucket );

        var appliedScale = RequestedScale;
        if ( beforeTracks.HasValue && beforeTracks.Value.size.x < AlreadyScaledTrackLengthThreshold ) {
          appliedScale = 1.0f;
          result.scaling_skipped = true;
        }
        else {
          ApplyBakedScale( root, RequestedScale, result );
        }

        AlignTrackBottom( root, result );
        RaiseBucketPose( root, result );
        SynchronizeConstraintFrames( root, result );

        var afterRenderers = CollectMeasuredRenderers( root );
        var afterOverall = CalculateBounds( afterRenderers );
        var afterTracks = CalculateTrackBounds( afterRenderers, afterOverall );
        var afterBucket = CalculateBucketBounds( afterRenderers );
        FillBoundsResult( afterOverall, result.after_overall );
        FillBoundsResult( afterTracks, result.after_tracks );
        FillBoundsResult( afterBucket, result.after_bucket );

        root.transform.localScale = Vector3.one;
        EditorUtility.SetDirty( root.transform );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.applied_scale = FormatFloat( appliedScale );
        result.root_position_m = FormatVector( root.transform.position );
        result.root_euler_deg = FormatVector( root.transform.eulerAngles );
        result.root_lossy_scale = FormatVector( root.transform.lossyScale );
        result.message = $"Baked E85 scale/pose from {source}. Scale applied: {appliedScale:0.###}; bucket min Y: {result.after_bucket.min_m}.";
        WriteResult( true, result.message, result );
      }
      catch ( System.Exception exception ) {
        result.message = exception.ToString();
        WriteResult( false, result.message, result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void ApplyBakedScale( GameObject root, float scale, ScalePoseResult result )
    {
      foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) ) {
        if ( transform == root.transform )
          continue;

        Undo.RecordObject( transform, "Bake excavator scale" );
        transform.localPosition *= scale;
        if ( ShouldScaleTransformLocalScale( transform ) )
          transform.localScale *= scale;
        EditorUtility.SetDirty( transform );
        result.scaled_transform_positions++;
      }

      foreach ( var shape in root.GetComponentsInChildren<Shape>( true ) ) {
        if ( ScaleShapeDimensions( shape, scale ) ) {
          ScalePrimitiveRenderDataVisual( shape, scale );
          EditorUtility.SetDirty( shape );
          result.scaled_shapes++;
        }
      }

      foreach ( var track in root.GetComponentsInChildren<Track>( true ) ) {
        Undo.RecordObject( track, "Bake excavator scale" );
        track.Width *= scale;
        track.Thickness *= scale;
        track.InitialTensionDistance *= scale;
        EditorUtility.SetDirty( track );
        result.scaled_tracks++;
      }

      foreach ( var wheel in root.GetComponentsInChildren<TrackWheel>( true ) ) {
        Undo.RecordObject( wheel, "Bake excavator scale" );
        wheel.Radius *= scale;
        EditorUtility.SetDirty( wheel );
        result.scaled_track_wheels++;
      }

      var massScale = scale * scale * scale;
      var inertiaScale = massScale * scale * scale;
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        ScaleMassProperties( rigidBody, scale, massScale, inertiaScale );
        EditorUtility.SetDirty( rigidBody );
        result.scaled_rigid_bodies++;
      }

      foreach ( var component in root.GetComponentsInChildren<Component>( true ) ) {
        if ( component == null || component is Transform )
          continue;

        result.scaled_serialized_local_positions += ScaleSerializedLocalPositions( component, scale );
      }

      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) )
        ScaleConstraintControllers( constraint, scale, result );
    }

    private static bool ShouldScaleTransformLocalScale( Transform transform )
    {
      if ( transform.GetComponent<AGXUnity.Collide.Mesh>() != null )
        return true;

      if ( transform.GetComponent<Renderer>() == null )
        return false;

      return transform.GetComponentInParent<Shape>() == null;
    }

    private static bool ScaleShapeDimensions( Shape shape, float scale )
    {
      if ( shape == null )
        return false;

      Undo.RecordObject( shape, "Bake excavator shape scale" );

      if ( shape is Box box ) {
        box.HalfExtents *= scale;
        return true;
      }

      if ( shape is Sphere sphere ) {
        sphere.Radius *= scale;
        return true;
      }

      if ( shape is Cylinder cylinder ) {
        cylinder.Radius *= scale;
        cylinder.Height *= scale;
        return true;
      }

      if ( shape is Capsule capsule ) {
        capsule.Radius *= scale;
        capsule.Height *= scale;
        return true;
      }

      if ( shape is Cone cone ) {
        cone.TopRadius *= scale;
        cone.BottomRadius *= scale;
        cone.Height *= scale;
        return true;
      }

      if ( shape is HollowCylinder hollowCylinder ) {
        hollowCylinder.Thickness *= scale;
        hollowCylinder.Radius *= scale;
        hollowCylinder.Height *= scale;
        return true;
      }

      if ( shape is HollowCone hollowCone ) {
        hollowCone.TopRadius *= scale;
        hollowCone.BottomRadius *= scale;
        hollowCone.Thickness *= scale;
        hollowCone.Height *= scale;
        return true;
      }

      return false;
    }

    private static void ScalePrimitiveRenderDataVisual( Shape shape, float scale )
    {
      if ( shape == null || shape is AGXUnity.Collide.Mesh )
        return;

      foreach ( var visual in shape.GetComponentsInChildren<AGXUnity.Rendering.ShapeVisualRenderData>( true ) ) {
        if ( visual == null || visual.Shape != shape )
          continue;

        Undo.RecordObject( visual.transform, "Bake primitive render-data visual scale" );
        visual.transform.localScale *= scale;
        EditorUtility.SetDirty( visual.transform );
      }
    }

    private static void ScaleMassProperties( RigidBody rigidBody, float lengthScale, float massScale, float inertiaScale )
    {
      if ( rigidBody == null || rigidBody.MassProperties == null )
        return;

      var properties = rigidBody.MassProperties;
      properties.Mass.DefaultValue *= massScale;
      properties.Mass.UserValue *= massScale;
      properties.InertiaDiagonal.DefaultValue *= inertiaScale;
      properties.InertiaDiagonal.UserValue *= inertiaScale;
      properties.InertiaOffDiagonal.DefaultValue *= inertiaScale;
      properties.InertiaOffDiagonal.UserValue *= inertiaScale;
      properties.CenterOfMassOffset.DefaultValue *= lengthScale;
      properties.CenterOfMassOffset.UserValue *= lengthScale;
    }

    private static int ScaleSerializedLocalPositions( Component component, float scale )
    {
      var serializedObject = new SerializedObject( component );
      var iterator = serializedObject.GetIterator();
      var changed = 0;

      while ( iterator.Next( true ) ) {
        if ( iterator.propertyPath.IndexOf( "m_localPosition", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        if ( iterator.propertyType == SerializedPropertyType.Vector3 ) {
          iterator.vector3Value *= scale;
          changed++;
        }
      }

      if ( changed > 0 ) {
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( component );
      }

      return changed;
    }

    private static void ScaleConstraintControllers( Constraint constraint, float scale, ScalePoseResult result )
    {
      if ( constraint == null )
        return;

      var isLinearPrimaryConstraint = constraint.Type == ConstraintType.Prismatic || constraint.Type == ConstraintType.DistanceJoint;
      foreach ( var controller in constraint.GetElementaryConstraintControllers() ) {
        if ( controller == null )
          continue;

        var isTranslationalController = isLinearPrimaryConstraint ||
                                        controller.GetControllerType() == Constraint.ControllerType.Translational;
        if ( !isTranslationalController )
          continue;

        Undo.RecordObject( constraint, "Bake excavator constraint controller scale" );

        if ( controller is LockController lockController ) {
          lockController.Position = ScaleFinite( lockController.Position, scale );
          result.scaled_constraint_controllers++;
        }
        else if ( controller is RangeController rangeController ) {
          rangeController.Range = new RangeReal( ScaleFinite( rangeController.Range.Min, scale ),
                                                 ScaleFinite( rangeController.Range.Max, scale ) );
          result.scaled_constraint_controllers++;
        }
        else if ( controller is TargetSpeedController speedController ) {
          speedController.Speed = ScaleFinite( speedController.Speed, scale );
          result.scaled_constraint_controllers++;
        }
      }

      EditorUtility.SetDirty( constraint );
    }

    private static void AlignTrackBottom( GameObject root, ScalePoseResult result )
    {
      var renderers = CollectMeasuredRenderers( root );
      var overall = CalculateBounds( renderers );
      var trackBounds = CalculateTrackBounds( renderers, overall ) ?? overall;
      if ( !trackBounds.HasValue )
        return;

      var deltaY = TargetTrackMinY - trackBounds.Value.min.y;
      if ( Mathf.Abs( deltaY ) < 0.001f )
        return;

      Undo.RecordObject( root.transform, "Align excavator track bottom" );
      root.transform.position += new Vector3( 0.0f, deltaY, 0.0f );
      EditorUtility.SetDirty( root.transform );
      result.track_alignment_delta_y_m = FormatFloat( deltaY );
    }

    private static void RaiseBucketPose( GameObject root, ScalePoseResult result )
    {
      var poseGroup = CollectPoseGroupTransforms( root );
      result.pose_group_count = poseGroup.Count;
      if ( poseGroup.Count == 0 )
        return;

      var bucketBounds = CalculateBucketBounds( CollectMeasuredRenderers( root ) );
      if ( !bucketBounds.HasValue || bucketBounds.Value.min.y >= TargetBucketMinY )
        return;

      var pivot = FindBoomBasePivot( root ) ?? new Vector3( 3.2f, 1.1f, 5.1f );
      var angle = EstimateBucketLiftAngle( pivot, bucketBounds.Value );
      ApplyPoseRotation( poseGroup, pivot, angle );
      result.pose_rotation_deg = FormatFloat( angle );

      var afterOverall = CalculateBounds( CollectMeasuredRenderers( root ) );
      if ( afterOverall.HasValue && afterOverall.Value.max.y > FactoryCeilingSafetyY ) {
        var excess = afterOverall.Value.max.y - FactoryCeilingSafetyY;
        var correction = Mathf.Min( angle * 0.65f, Mathf.Max( 0.0f, excess * 16.0f ) );
        if ( correction > 0.05f ) {
          ApplyPoseRotation( poseGroup, pivot, -correction );
          result.pose_ceiling_correction_deg = FormatFloat( correction );
        }
      }
    }

    private static float EstimateBucketLiftAngle( Vector3 pivot, Bounds bucketBounds )
    {
      var horizontalLever = Mathf.Max( 0.35f, bucketBounds.center.x - pivot.x );
      var requiredLift = Mathf.Max( 0.0f, TargetBucketMinY - bucketBounds.min.y );
      var angle = Mathf.Rad2Deg * Mathf.Asin( Mathf.Clamp( requiredLift / horizontalLever, 0.0f, 0.38f ) );
      return Mathf.Clamp( angle + 1.5f, 4.0f, 18.0f );
    }

    private static Vector3? FindBoomBasePivot( GameObject root )
    {
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
        if ( constraint == null || constraint.AttachmentPair == null || constraint.Type != ConstraintType.Hinge )
          continue;

        var referenceBody = constraint.AttachmentPair.ReferenceBody;
        var connectedBody = constraint.AttachmentPair.ConnectedBody;
        var referenceName = referenceBody != null ? referenceBody.name : string.Empty;
        var connectedName = connectedBody != null ? connectedBody.name : string.Empty;
        var connectsBoomBase = ( referenceName.Equals( "boom_rotaty", StringComparison.OrdinalIgnoreCase ) &&
                                 connectedName.Equals( "Arm", StringComparison.OrdinalIgnoreCase ) ) ||
                               ( referenceName.Equals( "Arm", StringComparison.OrdinalIgnoreCase ) &&
                                 connectedName.Equals( "boom_rotaty", StringComparison.OrdinalIgnoreCase ) );
        if ( connectsBoomBase )
          return constraint.AttachmentPair.ReferenceFrame.Position;
      }

      return null;
    }

    private static void ApplyPoseRotation( List<Transform> transforms, Vector3 pivot, float angleDegrees )
    {
      if ( Mathf.Abs( angleDegrees ) < 0.001f )
        return;

      var rotation = Quaternion.AngleAxis( angleDegrees, Vector3.forward );
      foreach ( var transform in transforms ) {
        Undo.RecordObject( transform, "Raise excavator bucket pose" );
        var newPosition = pivot + rotation * ( transform.position - pivot );
        var newRotation = rotation * transform.rotation;
        transform.SetPositionAndRotation( newPosition, newRotation );
        EditorUtility.SetDirty( transform );
      }
    }

    private static List<Transform> CollectPoseGroupTransforms( GameObject root )
    {
      var selected = new List<Transform>();
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        if ( rigidBody == null || rigidBody.transform == root.transform )
          continue;

        if ( IsWorkEquipmentBodyName( rigidBody.name ) )
          selected.Add( rigidBody.transform );
      }

      selected.RemoveAll( transform => HasSelectedAncestor( transform, selected ) );
      return selected;
    }

    private static bool IsWorkEquipmentBodyName( string name )
    {
      if ( string.IsNullOrEmpty( name ) )
        return false;

      if ( name.IndexOf( "Blade", StringComparison.OrdinalIgnoreCase ) >= 0 ||
           name.IndexOf( "Roller", StringComparison.OrdinalIgnoreCase ) >= 0 ||
           name.IndexOf( "Sprocket", StringComparison.OrdinalIgnoreCase ) >= 0 ||
           name.IndexOf( "Idler", StringComparison.OrdinalIgnoreCase ) >= 0 ||
           name.IndexOf( "Carriage", StringComparison.OrdinalIgnoreCase ) >= 0 ||
           name.IndexOf( "Chassie", StringComparison.OrdinalIgnoreCase ) >= 0 )
        return false;

      return name.Equals( "Arm", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Stick", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Bucket", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder1", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder1_arm", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder2", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder2_arm", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder3", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Cylinder3_arm", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Tilt_cylinder", StringComparison.OrdinalIgnoreCase ) ||
             name.Equals( "Tilt_cylinder_arm", StringComparison.OrdinalIgnoreCase );
    }

    private static bool HasSelectedAncestor( Transform transform, List<Transform> selected )
    {
      var parent = transform.parent;
      while ( parent != null ) {
        if ( selected.Contains( parent ) )
          return true;

        parent = parent.parent;
      }

      return false;
    }

    private static void SynchronizeConstraintFrames( GameObject root, ScalePoseResult result )
    {
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
        if ( constraint == null || constraint.AttachmentPair == null )
          continue;

        if ( constraint.Type == ConstraintType.Prismatic || constraint.Type == ConstraintType.DistanceJoint )
          continue;

        Undo.RecordObject( constraint, "Synchronize excavator constraint frames" );
        constraint.AttachmentPair.ConnectedFrame.Position = constraint.AttachmentPair.ReferenceFrame.Position;
        constraint.AttachmentPair.ConnectedFrame.Rotation = constraint.AttachmentPair.ReferenceFrame.Rotation;
        EditorUtility.SetDirty( constraint );
        result.synchronized_constraints++;
      }
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; scale utility did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static string BackupSceneFile()
    {
      var sceneAbsolutePath = GetProjectRelativeAbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = GetProjectRelativeAbsolutePath( "Temp/CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_e85_scale_pose_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        if ( component.GetType().FullName != "AGXUnity.Excavator" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          return prefabRoot;
      }

      return FindSceneObject( "Excavator_BobcatE85" ) ??
             FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
             FindSceneObject( "Excavator" );
    }

    private static GameObject FindSceneObject( string name )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var gameObject in objects ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == name )
          return gameObject;
      }

      return null;
    }

    private static Transform FindDescendant( Transform root, string name )
    {
      if ( root == null )
        return null;

      foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) ) {
        if ( transform.name.Equals( name, StringComparison.OrdinalIgnoreCase ) )
          return transform;
      }

      return null;
    }

    private static List<Renderer> CollectMeasuredRenderers( GameObject root )
    {
      var result = new List<Renderer>();
      foreach ( var renderer in root.GetComponentsInChildren<Renderer>( true ) ) {
        if ( renderer == null || renderer is ParticleSystemRenderer )
          continue;

        if ( !renderer.enabled || !renderer.gameObject.activeInHierarchy )
          continue;

        result.Add( renderer );
      }

      return result;
    }

    private static Bounds? CalculateTrackBounds( List<Renderer> renderers, Bounds? overallBounds )
    {
      var namedTracks = new List<Renderer>();
      foreach ( var renderer in renderers ) {
        if ( ContainsTrackKeyword( GetHierarchyPath( renderer.gameObject ) ) )
          namedTracks.Add( renderer );
      }

      var bounds = CalculateBounds( namedTracks );
      if ( bounds.HasValue || !overallBounds.HasValue )
        return bounds;

      var lower = new List<Renderer>();
      var cutoff = overallBounds.Value.min.y + Mathf.Min( 0.85f, Mathf.Max( 0.35f, overallBounds.Value.size.y * 0.32f ) );
      foreach ( var renderer in renderers ) {
        if ( renderer.bounds.center.y <= cutoff && renderer.bounds.min.y <= cutoff )
          lower.Add( renderer );
      }

      return CalculateBounds( lower );
    }

    private static Bounds? CalculateBucketBounds( List<Renderer> renderers )
    {
      var bucketRenderers = new List<Renderer>();
      foreach ( var renderer in renderers ) {
        var path = GetHierarchyPath( renderer.gameObject );
        if ( path.IndexOf( "/Bucket/", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.EndsWith( "/Bucket", StringComparison.OrdinalIgnoreCase ) )
          bucketRenderers.Add( renderer );
      }

      return CalculateBounds( bucketRenderers );
    }

    private static bool ContainsTrackKeyword( string value )
    {
      if ( string.IsNullOrEmpty( value ) )
        return false;

      return value.IndexOf( "track", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "crawler", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "tread", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "undercarriage", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "roller", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "sprocket", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "idler", StringComparison.OrdinalIgnoreCase ) >= 0;
    }

    private static Bounds? CalculateBounds( List<Renderer> renderers )
    {
      var hasBounds = false;
      var result = new Bounds();
      foreach ( var renderer in renderers ) {
        if ( !hasBounds ) {
          result = renderer.bounds;
          hasBounds = true;
        }
        else {
          result.Encapsulate( renderer.bounds );
        }
      }

      return hasBounds ? result : (Bounds?)null;
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

    private static float ScaleFinite( float value, float scale )
    {
      return float.IsNaN( value ) || float.IsInfinity( value ) ? value : value * scale;
    }

    private static void FillBoundsResult( Bounds? bounds, BoundsResult target )
    {
      if ( !bounds.HasValue )
        return;

      target.min_m = FormatVector( bounds.Value.min );
      target.max_m = FormatVector( bounds.Value.max );
      target.center_m = FormatVector( bounds.Value.center );
      target.size_m = FormatVector( bounds.Value.size );
      target.length_x_mm = FormatFloat( bounds.Value.size.x * 1000.0f );
      target.width_z_mm = FormatFloat( bounds.Value.size.z * 1000.0f );
      target.height_y_mm = FormatFloat( bounds.Value.size.y * 1000.0f );
    }

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( bool success, string message, ScalePoseResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new ScalePoseResult();

      result.success = success;
      result.message = message;
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    [Serializable]
    private sealed class ScalePoseResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public string scene_backup_path;
      public string applied_scale;
      public bool scaling_skipped;
      public string root_position_m;
      public string root_euler_deg;
      public string root_lossy_scale;
      public string track_alignment_delta_y_m;
      public string pose_rotation_deg;
      public string pose_ceiling_correction_deg;
      public int scaled_transform_positions;
      public int scaled_shapes;
      public int scaled_tracks;
      public int scaled_track_wheels;
      public int scaled_rigid_bodies;
      public int scaled_serialized_local_positions;
      public int scaled_constraint_controllers;
      public int pose_group_count;
      public int synchronized_constraints;
      public BoundsResult before_overall = new BoundsResult();
      public BoundsResult before_tracks = new BoundsResult();
      public BoundsResult before_bucket = new BoundsResult();
      public BoundsResult after_overall = new BoundsResult();
      public BoundsResult after_tracks = new BoundsResult();
      public BoundsResult after_bucket = new BoundsResult();
    }

    [Serializable]
    private sealed class BoundsResult
    {
      public string min_m;
      public string max_m;
      public string center_m;
      public string size_m;
      public string length_x_mm;
      public string width_z_mm;
      public string height_y_mm;
    }
  }
}
#endif
