#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity.Model;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexYuLongShovelUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string PrefabPath = "Assets/remake3/urdf/remake3.prefab";
    private const string RequestPath = "Temp/CodexYuLongShovel.request";
    private const string OutputDirectory = "Temp/CodexYuLongShovel";
    private const string SettingsPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/YuLongShovelSettings.asset";
    private const float MaxPenetrationForceN = 30000.0f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongShovelUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Configure YuLong Shovel" )]
    public static void ConfigureFromMenu()
    {
      ConfigureYuLongShovel( "menu" );
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

      ConfigureYuLongShovel( "request-file" );
    }

    private static void ConfigureYuLongShovel( string source )
    {
      s_isRunning = true;
      var result = new ShovelResult { source = source };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );

        var settings = LoadOrCreateSettings();
        ConfigurePrefab( settings, result );
        ConfigureSceneInstances( settings, result );
        BindMassTrackers( result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        WriteResult( true, "YuLong bucket shovel configured.", result );
      }
      catch ( System.Exception exception ) {
        result.message = exception.ToString();
        WriteResult( false, result.message, result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void ConfigurePrefab( DeformableTerrainShovelSettings settings, ShovelResult result )
    {
      var prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
      try {
        if ( prefabRoot == null )
          throw new InvalidOperationException( $"Could not load prefab contents: {PrefabPath}" );

        var bucket = ResolveBucket( prefabRoot.transform );
	        if ( bucket == null )
	          throw new InvalidOperationException( "Could not find bucket in remake3 prefab." );

        ConfigureBucketShovel( bucket, settings, result, "prefab" );
        PrefabUtility.SaveAsPrefabAsset( prefabRoot, PrefabPath );
        result.prefab_updated = true;
      }
      finally {
        if ( prefabRoot != null )
          PrefabUtility.UnloadPrefabContents( prefabRoot );
      }
    }

    private static void ConfigureSceneInstances( DeformableTerrainShovelSettings settings, ShovelResult result )
    {
      foreach ( var rig in Resources.FindObjectsOfTypeAll<ExcavatorYuLong>() ) {
        if ( rig == null || !rig.gameObject.scene.IsValid() )
          continue;

        rig.ResolveReferences();
        var bucket = rig.BucketReference != null ? rig.BucketReference : ResolveBucket( rig.transform );
        if ( bucket == null ) {
          Append( ref result.warnings, $"Missing YuLong bucket under {GetHierarchyPath( rig.gameObject )}" );
          continue;
        }

        ConfigureBucketShovel( bucket, settings, result, "scene" );
        result.scene_buckets_updated++;
      }
    }

    private static void ConfigureBucketShovel( Transform bucket,
                                               DeformableTerrainShovelSettings settings,
                                               ShovelResult result,
                                               string scope )
    {
      var shovel = bucket.GetComponent<DeformableTerrainShovel>();
      if ( shovel == null )
        shovel = bucket.gameObject.AddComponent<DeformableTerrainShovel>();

      var bounds = CalculateOwnVisualLocalBounds( bucket, out var meshCount );
      if ( meshCount == 0 )
        throw new InvalidOperationException( $"Could not calculate visual bounds for {GetHierarchyPath( bucket.gameObject )}" );

      var size = bounds.size;
      var widthAxis = GetLargestAxis( size );
      var depthAxis = GetSmallestAxis( size );
      var heightAxis = GetRemainingAxis( widthAxis, depthAxis );

      var cuttingDepth = GetMin( bounds, depthAxis );
      var topDepth = Mathf.Lerp( GetMin( bounds, depthAxis ), GetMax( bounds, depthAxis ), 0.82f );
      var cuttingHeight = Mathf.Lerp( GetMin( bounds, heightAxis ), GetMax( bounds, heightAxis ), 0.12f );
      var topHeight = Mathf.Lerp( GetMin( bounds, heightAxis ), GetMax( bounds, heightAxis ), 0.82f );
      var widthMin = Mathf.Lerp( GetMin( bounds, widthAxis ), GetMax( bounds, widthAxis ), 0.08f );
      var widthMax = Mathf.Lerp( GetMin( bounds, widthAxis ), GetMax( bounds, widthAxis ), 0.92f );

      var cuttingStart = bounds.center;
      var cuttingEnd = bounds.center;
      SetAxis( ref cuttingStart, widthAxis, widthMin );
      SetAxis( ref cuttingEnd, widthAxis, widthMax );
      SetAxis( ref cuttingStart, depthAxis, cuttingDepth );
      SetAxis( ref cuttingEnd, depthAxis, cuttingDepth );
      SetAxis( ref cuttingStart, heightAxis, cuttingHeight );
      SetAxis( ref cuttingEnd, heightAxis, cuttingHeight );

      var topStart = bounds.center;
      var topEnd = bounds.center;
      SetAxis( ref topStart, widthAxis, widthMin );
      SetAxis( ref topEnd, widthAxis, widthMax );
      SetAxis( ref topStart, depthAxis, topDepth );
      SetAxis( ref topEnd, depthAxis, topDepth );
      SetAxis( ref topStart, heightAxis, topHeight );
      SetAxis( ref topEnd, heightAxis, topHeight );

      var toothStart = 0.5f * ( cuttingStart + cuttingEnd );
      var toothEnd = toothStart;
      SetAxis( ref toothEnd, depthAxis, GetMin( bounds, depthAxis ) - Mathf.Max( 0.12f, GetSize( size, depthAxis ) * 0.25f ) );

      // AGX derives the bucket-inside direction from Cross(cutting edge, cutting-to-top).
      // YuLong's bucket mesh needs both edge lines reversed for that ray to point into the bucket.
      shovel.TopEdge = Line.Create( bucket.gameObject, topEnd, topStart );
      shovel.CuttingEdge = Line.Create( bucket.gameObject, cuttingEnd, cuttingStart );
      shovel.HasTeeth = true;
      shovel.ToothDirection = Line.Create( bucket.gameObject, toothStart, toothEnd );
      shovel.Settings = settings;

      EditorUtility.SetDirty( shovel );
      result.shovels_configured++;
      Append( ref result.shovel_settings,
              $"{scope}:{GetHierarchyPath( bucket.gameObject )} boundsCenter={FormatVector( bounds.center )} boundsSize={FormatVector( bounds.size )} axes width={AxisName( widthAxis )} depth={AxisName( depthAxis )} height={AxisName( heightAxis )} cutting={FormatVector( cuttingEnd )}->{FormatVector( cuttingStart )} top={FormatVector( topEnd )}->{FormatVector( topStart )} tooth={FormatVector( toothStart )}->{FormatVector( toothEnd )}" );
    }

    private static void BindMassTrackers( ShovelResult result )
    {
      foreach ( var tracker in Resources.FindObjectsOfTypeAll<ExcavationMassTracker>() ) {
        if ( tracker == null || !tracker.gameObject.scene.IsValid() )
          continue;

        var serialized = new SerializedObject( tracker );
        var machineRootProperty = serialized.FindProperty( "m_machineRoot" );
        var machineRoot = machineRootProperty != null ? machineRootProperty.objectReferenceValue as Transform : null;
        var shovel = machineRoot != null ? machineRoot.GetComponentInChildren<DeformableTerrainShovel>( true ) : null;
        if ( shovel == null )
          shovel = UnityEngine.Object.FindFirstObjectByType<DeformableTerrainShovel>( FindObjectsInactive.Include );

        if ( shovel == null ) {
          Append( ref result.warnings, $"Could not bind shovel on {GetHierarchyPath( tracker.gameObject )}" );
          continue;
        }

        var shovelProperty = serialized.FindProperty( "shovel" );
        if ( shovelProperty != null )
          shovelProperty.objectReferenceValue = shovel;

        var bucketFrameProperty = serialized.FindProperty( "m_bucketMeasurementFrame" );
        if ( bucketFrameProperty != null )
          bucketFrameProperty.objectReferenceValue = shovel.transform;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( tracker );
        result.mass_trackers_bound++;
      }
    }

    private static DeformableTerrainShovelSettings LoadOrCreateSettings()
    {
      var settings = AssetDatabase.LoadAssetAtPath<DeformableTerrainShovelSettings>( SettingsPath );
      if ( settings == null ) {
        settings = ScriptAsset.Create<DeformableTerrainShovelSettings>();
        settings.name = "YuLongShovelSettings";
        var directory = Path.GetDirectoryName( SettingsPath );
        if ( !string.IsNullOrEmpty( directory ) )
          Directory.CreateDirectory( GetProjectRelativeAbsolutePath( directory ) );
        AssetDatabase.CreateAsset( settings, SettingsPath );
      }

      settings.NumberOfTeeth = 5;
      settings.ToothLength = 0.12f;
      settings.ToothRadius = new RangeReal( 0.012f, 0.045f );
      settings.EnableExcavationAtTeethEdge = true;
      settings.VerticalBladeSoilMergeDistance = 0.02f;
      settings.PenetrationDepthThreshold = 0.12f;
      settings.PenetrationForceScaling = 0.8f;
      settings.MaxPenetrationForce = MaxPenetrationForceN;
      settings.NoMergeExtensionDistance = 0.25f;
      settings.MinimumSubmergedContactLengthFraction = 0.25f;
      settings.RemoveContacts = false;
      EditorUtility.SetDirty( settings );
      return settings;
    }

    private static Bounds CalculateOwnVisualLocalBounds( Transform bucket, out int meshCount )
    {
      meshCount = 0;
      var hasBounds = false;
      var bounds = new Bounds( Vector3.zero, Vector3.zero );

      foreach ( var filter in CollectOwnMeshFilters( bucket ) ) {
        if ( filter == null || filter.sharedMesh == null )
          continue;

        var mesh = filter.sharedMesh;
        foreach ( var vertex in mesh.vertices ) {
          var world = filter.transform.TransformPoint( vertex );
          var local = bucket.InverseTransformPoint( world );
          if ( !hasBounds ) {
            bounds = new Bounds( local, Vector3.zero );
            hasBounds = true;
          }
          else {
            bounds.Encapsulate( local );
          }
        }

        meshCount++;
      }

      return bounds;
    }

    private static List<MeshFilter> CollectOwnMeshFilters( Transform bucket )
    {
      var visual = new List<MeshFilter>();
      CollectOwnMeshFiltersRecursive( bucket, bucket, visual, requireVisualName: true );
      if ( visual.Count > 0 )
        return visual;

      var fallback = new List<MeshFilter>();
      CollectOwnMeshFiltersRecursive( bucket, bucket, fallback, requireVisualName: false );
      return fallback;
    }

    private static void CollectOwnMeshFiltersRecursive( Transform root,
                                                       Transform current,
                                                       List<MeshFilter> filters,
                                                       bool requireVisualName )
    {
      if ( current != root && current.GetComponent<RigidBody>() != null )
        return;

      var filter = current.GetComponent<MeshFilter>();
      var renderer = current.GetComponent<MeshRenderer>();
      if ( filter != null &&
           filter.sharedMesh != null &&
           renderer != null &&
           ( !requireVisualName || current.name.IndexOf( "VisualMesh", StringComparison.OrdinalIgnoreCase ) >= 0 ) )
        filters.Add( filter );

      foreach ( Transform child in current )
        CollectOwnMeshFiltersRecursive( root, child, filters, requireVisualName );
    }

    private static Transform ResolveBucket( Transform root )
    {
      var rig = root.GetComponent<ExcavatorYuLong>();
      if ( rig != null ) {
        rig.ResolveReferences();
        if ( rig.BucketReference != null )
          return rig.BucketReference;
      }

	      return FindChildRecursive( root, "bucket" );
    }

    private static Transform FindChildRecursive( Transform root, string objectName )
    {
      if ( root == null )
        return null;
      if ( string.Equals( root.name, objectName, StringComparison.OrdinalIgnoreCase ) )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, objectName );
        if ( result != null )
          return result;
      }

      return null;
    }

    private static int GetLargestAxis( Vector3 vector )
    {
      if ( vector.x >= vector.y && vector.x >= vector.z )
        return 0;
      return vector.y >= vector.z ? 1 : 2;
    }

    private static int GetSmallestAxis( Vector3 vector )
    {
      if ( vector.x <= vector.y && vector.x <= vector.z )
        return 0;
      return vector.y <= vector.z ? 1 : 2;
    }

    private static int GetRemainingAxis( int first, int second )
    {
      for ( var axis = 0; axis < 3; axis++ ) {
        if ( axis != first && axis != second )
          return axis;
      }

      return 1;
    }

    private static float GetMin( Bounds bounds, int axis )
    {
      return axis == 0 ? bounds.min.x : axis == 1 ? bounds.min.y : bounds.min.z;
    }

    private static float GetMax( Bounds bounds, int axis )
    {
      return axis == 0 ? bounds.max.x : axis == 1 ? bounds.max.y : bounds.max.z;
    }

    private static float GetSize( Vector3 size, int axis )
    {
      return axis == 0 ? size.x : axis == 1 ? size.y : size.z;
    }

    private static void SetAxis( ref Vector3 vector, int axis, float value )
    {
      if ( axis == 0 )
        vector.x = value;
      else if ( axis == 1 )
        vector.y = value;
      else
        vector.z = value;
    }

    private static string AxisName( int axis )
    {
      return axis == 0 ? "x" : axis == 1 ? "y" : "z";
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; YuLong shovel tool did not switch scenes.",
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
                                     "AGXUnity_Excavator_before_yulong_shovel_" +
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

    private static string FormatVector( Vector3 value )
    {
      return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
    }

    private static void Append( ref string text, string value )
    {
      if ( string.IsNullOrEmpty( text ) )
        text = value;
      else
        text += "; " + value;
    }

    private static void WriteResult( bool success, string message, ShovelResult result )
    {
      if ( result == null )
        result = new ShovelResult();

      result.success = success;
      result.message = message;

      var outputDirectory = GetProjectRelativeAbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      File.WriteAllText( Path.Combine( outputDirectory, "result.json" ), JsonUtility.ToJson( result, true ) );
    }

    [Serializable]
    private sealed class ShovelResult
    {
      public bool success;
      public string source;
      public string message;
      public string scene_backup_path;
      public bool prefab_updated;
      public int scene_buckets_updated;
      public int shovels_configured;
      public int mass_trackers_bound;
      public string shovel_settings;
      public string warnings;
    }
  }
}
#endif
