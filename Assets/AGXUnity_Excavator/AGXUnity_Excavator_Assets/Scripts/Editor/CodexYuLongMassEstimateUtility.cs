#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexYuLongMassEstimateUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string PrefabPath = "Assets/remake3/urdf/remake3.prefab";
    private const string RequestPath = "Temp/CodexYuLongMassEstimate.request";
    private const string OutputDirectory = "Temp/CodexYuLongMassEstimate";

    private const float SteelDensityKgPerCubicMeter = 7890.0f;

    private const float SwingMinAngle = -2.094395f;
    private const float SwingMaxAngle = 2.094395f;
    private const float BoomMinAngle = -0.785398f;
    private const float BoomMaxAngle = 1.221730f;
    private const float StickMinAngle = -1.745329f;
    private const float StickMaxAngle = 0.872665f;
    private const float BucketMinAngle = -1.570796f;
    private const float BucketMaxAngle = 1.047198f;
    private const float SwingTorqueLimit = 100000.0f;
    private const float BoomTorqueLimit = 80000.0f;
    private const float StickTorqueLimit = 60000.0f;
    private const float BucketTorqueLimit = 50000.0f;

    private static readonly BodySpec[] BodySpecs = {
      new BodySpec( "base_link", 1200.0f, 600.0f, 2500.0f, agx.RigidBody.MotionControl.STATIC ),
      new BodySpec( "upper_structure", 800.0f, 400.0f, 1800.0f, agx.RigidBody.MotionControl.DYNAMICS ),
      new BodySpec( "boom", 350.0f, 180.0f, 900.0f, agx.RigidBody.MotionControl.DYNAMICS ),
      new BodySpec( "stick", 220.0f, 120.0f, 650.0f, agx.RigidBody.MotionControl.DYNAMICS ),
	      new BodySpec( "bucket", 120.0f, 70.0f, 380.0f, agx.RigidBody.MotionControl.DYNAMICS )
    };

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongMassEstimateUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Estimate YuLong Mass From Mesh Volume" )]
    public static void EstimateFromMenu()
    {
      EstimateYuLongMasses( "menu" );
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

      EstimateYuLongMasses( "request-file" );
    }

    private static void EstimateYuLongMasses( string source )
    {
      s_isRunning = true;
      var result = new MassEstimateResult { source = source };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );

        var estimates = EstimatePrefabMasses( result );
        ApplyToPrefab( estimates, result );
        ApplyToSceneInstances( estimates, result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.message = $"Estimated YuLong masses from visual mesh volume using steel density {SteelDensityKgPerCubicMeter:0.#} kg/m^3.";
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

    private static Dictionary<string, BodyEstimate> EstimatePrefabMasses( MassEstimateResult result )
    {
      var estimates = new Dictionary<string, BodyEstimate>( StringComparer.OrdinalIgnoreCase );
      var prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
      try {
        if ( prefabRoot == null )
          throw new InvalidOperationException( $"Could not load prefab contents: {PrefabPath}" );

        foreach ( var spec in BodySpecs ) {
          var bodyTransform = FindChildRecursive( prefabRoot.transform, spec.Name );
          if ( bodyTransform == null ) {
            Append( ref result.warnings, $"Missing body transform {spec.Name} in prefab" );
            continue;
          }

          var meshVolume = EstimateOwnVisualMeshVolume( bodyTransform, out var meshCount );
          var rawSteelMass = meshVolume * SteelDensityKgPerCubicMeter;
          var appliedMass = Clamp( rawSteelMass, spec.MinimumMassKg, spec.MaximumMassKg );

          var estimate = new BodyEstimate {
            name = spec.Name,
            mesh_count = meshCount,
            visual_mesh_volume_m3 = meshVolume,
            raw_solid_steel_mass_kg = rawSteelMass,
            applied_mass_kg = appliedMass,
            previous_mass_kg = spec.PreviousMassKg,
            minimum_mass_kg = spec.MinimumMassKg,
            maximum_mass_kg = spec.MaximumMassKg
          };

          estimates[spec.Name] = estimate;
          Append( ref result.mass_estimates,
                  $"{spec.Name}: volume={meshVolume:0.####}m3 rawSteel={rawSteelMass:0.#}kg applied={appliedMass:0.#}kg meshes={meshCount}" );
        }
      }
      finally {
        if ( prefabRoot != null )
          PrefabUtility.UnloadPrefabContents( prefabRoot );
      }

      return estimates;
    }

    private static void ApplyToPrefab( Dictionary<string, BodyEstimate> estimates, MassEstimateResult result )
    {
      var prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
      try {
        if ( prefabRoot == null )
          throw new InvalidOperationException( $"Could not load prefab contents: {PrefabPath}" );

        var rig = prefabRoot.GetComponent<ExcavatorYuLong>();
        if ( rig == null )
          rig = prefabRoot.AddComponent<ExcavatorYuLong>();

        ApplyToRigRoot( prefabRoot.transform, rig, estimates, result );
        PrefabUtility.SaveAsPrefabAsset( prefabRoot, PrefabPath );
        result.prefab_updated = true;
      }
      finally {
        if ( prefabRoot != null )
          PrefabUtility.UnloadPrefabContents( prefabRoot );
      }
    }

    private static void ApplyToSceneInstances( Dictionary<string, BodyEstimate> estimates, MassEstimateResult result )
    {
      foreach ( var rig in Resources.FindObjectsOfTypeAll<ExcavatorYuLong>() ) {
        if ( rig == null || !rig.gameObject.scene.IsValid() )
          continue;

        ApplyToRigRoot( rig.transform, rig, estimates, result );
        result.scene_rigs_updated++;
      }
    }

    private static void ApplyToRigRoot( Transform root,
                                        ExcavatorYuLong rig,
                                        Dictionary<string, BodyEstimate> estimates,
                                        MassEstimateResult result )
    {
      if ( root == null || rig == null )
        return;

      rig.ResolveReferences();

      foreach ( var spec in BodySpecs ) {
        if ( !estimates.TryGetValue( spec.Name, out var estimate ) ) {
          Append( ref result.warnings, $"No mass estimate for {spec.Name}" );
          continue;
        }

        ApplyBodyMass( root, spec, estimate.applied_mass_kg, result );
      }

	      var dynamicMass = SumMass( estimates, "upper_structure", "boom", "stick", "bucket" );
	      var armMass = SumMass( estimates, "boom", "stick", "bucket" );
	      var stickBucketMass = SumMass( estimates, "stick", "bucket" );
	      var bucketMass = SumMass( estimates, "bucket" );

      var swingTorque = ScaleTorque( SwingTorqueLimit, dynamicMass / ( 800.0f + 350.0f + 220.0f + 120.0f ) );
      var boomTorque = ScaleTorque( BoomTorqueLimit, armMass / ( 350.0f + 220.0f + 120.0f ) );
      var stickTorque = ScaleTorque( StickTorqueLimit, stickBucketMass / ( 220.0f + 120.0f ) );
      var bucketTorque = ScaleTorque( BucketTorqueLimit, bucketMass / 120.0f );

      TuneConstraint( rig.SwingHinge, "swing_joint", swingTorque, SwingMinAngle, SwingMaxAngle, result );
      TuneConstraint( rig.BoomConstraint, "boom_joint", boomTorque, BoomMinAngle, BoomMaxAngle, result );
      TuneConstraint( rig.StickConstraint, "stick_joint", stickTorque, StickMinAngle, StickMaxAngle, result );
      TuneConstraint( rig.BucketConstraint, "bucket_joint", bucketTorque, BucketMinAngle, BucketMaxAngle, result );

      EditorUtility.SetDirty( rig );
    }

    private static void ApplyBodyMass( Transform root,
                                       BodySpec spec,
                                       float massKg,
                                       MassEstimateResult result )
    {
      var transform = FindChildRecursive( root, spec.Name );
      var rigidBody = transform != null ? transform.GetComponent<RigidBody>() : null;
      if ( rigidBody == null ) {
        Append( ref result.warnings, $"Missing RigidBody on {spec.Name}" );
        return;
      }

      rigidBody.MotionControl = spec.MotionControl;
      rigidBody.MassProperties.Mass.UseDefault = false;
      rigidBody.MassProperties.Mass.UserValue = massKg;

      EditorUtility.SetDirty( rigidBody );
      result.bodies_updated++;
      Append( ref result.body_settings, $"{GetHierarchyPath( transform.gameObject )}: mass={massKg:0.#}kg motion={spec.MotionControl}" );
    }

    private static void TuneConstraint( Constraint constraint,
                                        string name,
                                        float torqueLimit,
                                        float minAngle,
                                        float maxAngle,
                                        MassEstimateResult result )
    {
      if ( constraint == null ) {
        Append( ref result.warnings, $"Missing constraint {name}" );
        return;
      }

      var range = constraint.GetController<RangeController>();
      if ( range != null ) {
        range.Enable = true;
        range.Range = new RangeReal( minAngle, maxAngle );
      }

      var targetSpeed = constraint.GetController<TargetSpeedController>();
      if ( targetSpeed != null ) {
        targetSpeed.Enable = true;
        targetSpeed.LockAtZeroSpeed = true;
        targetSpeed.ForceRange = new RangeReal( -torqueLimit, torqueLimit );
      }
      else {
        Append( ref result.warnings, $"Missing TargetSpeedController on {name}" );
      }

      var lockController = constraint.GetController<LockController>();
      if ( lockController != null ) {
        lockController.Enable = false;
        lockController.ForceRange = new RangeReal( -torqueLimit, torqueLimit );
      }

      EditorUtility.SetDirty( constraint );
      result.constraints_updated++;
      Append( ref result.constraint_settings,
              $"{GetHierarchyPath( constraint.gameObject )}: range=[{minAngle:0.###},{maxAngle:0.###}]rad targetSpeedForce=+/-{torqueLimit:0.#}" );
    }

    private static float EstimateOwnVisualMeshVolume( Transform bodyTransform, out int meshCount )
    {
      var visualFilters = CollectOwnMeshFilters( bodyTransform, requireVisualName: true );
      var filters = visualFilters.Count > 0 ? visualFilters : CollectOwnMeshFilters( bodyTransform, requireVisualName: false );

      double volume = 0.0;
      meshCount = 0;

      foreach ( var filter in filters ) {
        if ( filter == null || filter.sharedMesh == null )
          continue;

        var meshVolume = Math.Abs( CalculateSignedMeshVolume( filter.sharedMesh, filter.transform.localToWorldMatrix ) );
        if ( meshVolume <= 1.0e-8 )
          meshVolume = CalculateBoundsFallbackVolume( filter );

        volume += meshVolume;
        meshCount++;
      }

      return (float)Math.Max( 0.0, volume );
    }

    private static List<MeshFilter> CollectOwnMeshFilters( Transform bodyTransform, bool requireVisualName )
    {
      var filters = new List<MeshFilter>();
      CollectOwnMeshFiltersRecursive( bodyTransform, bodyTransform, requireVisualName, filters );
      return filters;
    }

    private static void CollectOwnMeshFiltersRecursive( Transform bodyTransform,
                                                       Transform current,
                                                       bool requireVisualName,
                                                       List<MeshFilter> filters )
    {
      if ( current != bodyTransform && current.GetComponent<RigidBody>() != null )
        return;

      var filter = current.GetComponent<MeshFilter>();
      var renderer = current.GetComponent<MeshRenderer>();
      if ( filter != null &&
           filter.sharedMesh != null &&
           renderer != null &&
           ( !requireVisualName || current.name.IndexOf( "VisualMesh", StringComparison.OrdinalIgnoreCase ) >= 0 ) )
        filters.Add( filter );

      foreach ( Transform child in current )
        CollectOwnMeshFiltersRecursive( bodyTransform, child, requireVisualName, filters );
    }

    private static double CalculateSignedMeshVolume( UnityEngine.Mesh mesh, Matrix4x4 localToWorld )
    {
      var vertices = mesh.vertices;
      double volume = 0.0;

      for ( var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++ ) {
        var triangles = mesh.GetTriangles( subMesh );
        for ( var i = 0; i + 2 < triangles.Length; i += 3 ) {
          var a = localToWorld.MultiplyPoint3x4( vertices[triangles[i]] );
          var b = localToWorld.MultiplyPoint3x4( vertices[triangles[i + 1]] );
          var c = localToWorld.MultiplyPoint3x4( vertices[triangles[i + 2]] );
          volume += Vector3.Dot( a, Vector3.Cross( b, c ) ) / 6.0;
        }
      }

      return volume;
    }

    private static double CalculateBoundsFallbackVolume( MeshFilter filter )
    {
      var bounds = filter.sharedMesh.bounds;
      var size = Vector3.Scale( bounds.size, filter.transform.lossyScale );
      return Math.Abs( size.x * size.y * size.z ) * 0.15;
    }

    private static float SumMass( Dictionary<string, BodyEstimate> estimates, params string[] names )
    {
      var total = 0.0f;
      foreach ( var name in names ) {
        if ( estimates.TryGetValue( name, out var estimate ) )
          total += estimate.applied_mass_kg;
      }

      return total;
    }

    private static float ScaleTorque( float previousTorque, float massRatio )
    {
      if ( massRatio <= 0.0f || float.IsNaN( massRatio ) || float.IsInfinity( massRatio ) )
        return previousTorque;

      var scaled = previousTorque * Mathf.Sqrt( massRatio );
      return Mathf.Clamp( scaled, previousTorque, previousTorque * 2.25f );
    }

    private static float Clamp( float value, float minimum, float maximum )
    {
      if ( value < minimum )
        return minimum;
      if ( value > maximum )
        return maximum;
      return value;
    }

    private static Transform FindChildRecursive( Transform root, string objectName )
    {
      if ( root == null )
        return null;
      if ( root.name == objectName )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, objectName );
        if ( result != null )
          return result;
      }

      return null;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; YuLong mass estimate tool did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static string SaveCurrentSceneBackup( Scene scene )
    {
      if ( !scene.IsValid() )
        return string.Empty;

      EditorSceneManager.SaveScene( scene );

      var sceneAbsolutePath = GetProjectRelativeAbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = GetProjectRelativeAbsolutePath( "CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_yulong_mass_estimate_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName;
      return projectRoot == null ? projectRelativePath : Path.Combine( projectRoot, projectRelativePath );
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

    private static void Append( ref string text, string value )
    {
      if ( string.IsNullOrEmpty( text ) )
        text = value;
      else
        text += "; " + value;
    }

    private static void WriteResult( bool success, string message, MassEstimateResult result )
    {
      if ( result == null )
        result = new MassEstimateResult();

      result.success = success;
      result.message = message;

      var outputDirectory = GetProjectRelativeAbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      File.WriteAllText( Path.Combine( outputDirectory, "result.json" ), JsonUtility.ToJson( result, true ) );
    }

    private readonly struct BodySpec
    {
      public BodySpec( string name,
                       float previousMassKg,
                       float minimumMassKg,
                       float maximumMassKg,
                       agx.RigidBody.MotionControl motionControl )
      {
        Name = name;
        PreviousMassKg = previousMassKg;
        MinimumMassKg = minimumMassKg;
        MaximumMassKg = maximumMassKg;
        MotionControl = motionControl;
      }

      public string Name { get; }
      public float PreviousMassKg { get; }
      public float MinimumMassKg { get; }
      public float MaximumMassKg { get; }
      public agx.RigidBody.MotionControl MotionControl { get; }
    }

    [Serializable]
    private sealed class BodyEstimate
    {
      public string name;
      public int mesh_count;
      public float visual_mesh_volume_m3;
      public float raw_solid_steel_mass_kg;
      public float applied_mass_kg;
      public float previous_mass_kg;
      public float minimum_mass_kg;
      public float maximum_mass_kg;
    }

    [Serializable]
    private sealed class MassEstimateResult
    {
      public bool success;
      public string source;
      public string message;
      public string scene_backup_path;
      public bool prefab_updated;
      public int scene_rigs_updated;
      public int bodies_updated;
      public int constraints_updated;
      public string mass_estimates;
      public string body_settings;
      public string constraint_settings;
      public string warnings;
    }
  }
}
#endif
