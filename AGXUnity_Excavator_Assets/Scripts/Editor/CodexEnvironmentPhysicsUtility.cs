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
  public static class CodexEnvironmentPhysicsUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexEnvironmentPhysics.request";
    private const string OutputDirectory = "Temp/CodexEnvironmentPhysics";

    private const string FrameMaterialPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/FrameMaterial.asset";
    private const string GroundMaterialPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/Excavator_GroundMaterial.asset";
    private const string GranularMaterialPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/GranularTerrainMaterial.asset";
    private const string TerrainMaterialPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/Excavator Deformable Terrain material.asset";
    private const string DigTerrainPropertiesPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/Excavator Deformable Terrain Properties.asset";
    private const string DumpTerrainPropertiesPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/Codex Dump Terrain Properties.asset";
    private const string ParticleGranulePrefabPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/models/ScaledRock.prefab";

    private const float DigSoilParticleSizeScaling = 1.0f;
    private const float DigSoilParticleGrowthRate = 0.125f;
    private const float PassiveSoilParticleSizeScaling = 2.5f;
    private const float PassiveSoilParticleGrowthRate = 0.035f;
    private const float RuntimeAvalancheDecayFraction = 0.1f;
    private const float RuntimeAvalancheMaxHeightGrowth = float.PositiveInfinity;
    private const float RuntimeMaximumParticleActivationVolume = float.PositiveInfinity;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexEnvironmentPhysicsUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Apply Environment AGX Physics" )]
    public static void ApplyEnvironmentPhysicsFromMenu()
    {
      ApplyEnvironmentPhysics( "menu" );
    }

    public static void ApplyEnvironmentPhysicsFromFactoryBuilder()
    {
      ApplyEnvironmentPhysics( "factory-builder" );
    }

    public static void ApplyEnvironmentPhysicsFromTerrainBuilder()
    {
      ApplyEnvironmentPhysics( "terrain-builder" );
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

      ApplyEnvironmentPhysics( "request-file" );
    }

    private static void ApplyEnvironmentPhysics( string source )
    {
      s_isRunning = true;
      var result = new EnvironmentPhysicsResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        var frameMaterial = AssetDatabase.LoadAssetAtPath<ShapeMaterial>( FrameMaterialPath );
        var groundMaterial = AssetDatabase.LoadAssetAtPath<ShapeMaterial>( GroundMaterialPath );
        var granularMaterial = AssetDatabase.LoadAssetAtPath<ShapeMaterial>( GranularMaterialPath );
        var terrainMaterial = AssetDatabase.LoadAssetAtPath<DeformableTerrainMaterial>( TerrainMaterialPath );

        result.static_bodies_configured = ConfigureStaticEnvironmentBodies( frameMaterial, groundMaterial, result );
        AuditFactoryAgxBodies( result );

        var digTerrain = FindSceneObject( "DigTerrain" )?.GetComponent<DeformableTerrain>();
        var dumpTerrain = FindSceneObject( "DumpTerrainReceiver" )?.GetComponent<DeformableTerrain>();
        var digProperties = EnsureDigTerrainProperties( result );
        var dumpProperties = EnsureDumpTerrainProperties( result );

        ConfigureTerrainPhysics( digTerrain, groundMaterial, granularMaterial, terrainMaterial, digProperties, result, "DigTerrain" );
        ConfigureTerrainPhysics( dumpTerrain, groundMaterial, granularMaterial, terrainMaterial, dumpProperties, result, "DumpTerrainReceiver" );
        ConfigureDumpCompactor( digTerrain, dumpTerrain, result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        result.screenshots = new ScreenshotSet {
          dump = CaptureNamedCamera( "CodexTerrainAreasDumpCamera", "dump_receiver_physics.png" ),
          overview = CaptureNamedCamera( "CodexTerrainAreasOverviewCamera", "environment_physics_overview.png" )
        };

        result.message = $"Applied AGX environment physics from {source}: static AGX bodies for factory/area solids and dump particle-to-static-terrain compactor.";
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

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; environment physics tool did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static int ConfigureStaticEnvironmentBodies( ShapeMaterial frameMaterial,
                                                        ShapeMaterial groundMaterial,
                                                        EnvironmentPhysicsResult result )
    {
      var configured = 0;
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var gameObject in objects ) {
        if ( gameObject == null || !gameObject.scene.IsValid() || !IsMeasuredEnvironmentSolid( gameObject.name ) )
          continue;

        var meshRenderer = gameObject.GetComponent<MeshRenderer>();
        var meshFilter = gameObject.GetComponent<MeshFilter>();
        if ( meshRenderer == null || meshFilter == null )
          continue;

        var rb = EnsureStaticRigidBody( gameObject );
        var shapeMaterial = gameObject.name == "FactoryFloor" ? groundMaterial : frameMaterial;
        if ( ShouldUseMeshShape( gameObject.name ) )
          ConfigureMeshShape( gameObject, meshFilter, shapeMaterial, result );
        else
          ConfigureBoxShape( gameObject, shapeMaterial );

        EditorUtility.SetDirty( rb );
        EditorUtility.SetDirty( gameObject );

        configured++;
        result.static_body_names.Add( GetHierarchyPath( gameObject ) );
      }

      return configured;
    }

    private static RigidBody EnsureStaticRigidBody( GameObject gameObject )
    {
      var rb = gameObject.GetComponent<RigidBody>();
      if ( rb == null )
        rb = gameObject.AddComponent<RigidBody>();

      rb.MotionControl = agx.RigidBody.MotionControl.STATIC;
      return rb;
    }

    private static void ConfigureBoxShape( GameObject gameObject, ShapeMaterial material )
    {
      var box = gameObject.GetComponent<Box>();
      if ( box == null )
        box = gameObject.AddComponent<Box>();

      var extents = gameObject.transform.lossyScale;
      box.HalfExtents = new Vector3( Mathf.Abs( extents.x ) * 0.5f,
                                     Mathf.Abs( extents.y ) * 0.5f,
                                     Mathf.Abs( extents.z ) * 0.5f );
      ConfigureCommonShapeSettings( box, material );
      EditorUtility.SetDirty( box );
    }

    private static void ConfigureMeshShape( GameObject gameObject,
                                            MeshFilter meshFilter,
                                            ShapeMaterial material,
                                            EnvironmentPhysicsResult result )
    {
      var box = gameObject.GetComponent<Box>();
      if ( box != null )
        UnityEngine.Object.DestroyImmediate( box );

      var meshShape = gameObject.GetComponent<AGXUnity.Collide.Mesh>();
      if ( meshShape == null )
        meshShape = gameObject.AddComponent<AGXUnity.Collide.Mesh>();

      if ( meshFilter.sharedMesh == null )
        result.warnings.Add( $"{GetHierarchyPath( gameObject )}: no MeshFilter.sharedMesh for AGX mesh shape." );
      else if ( !meshShape.SetSourceObject( meshFilter.sharedMesh ) )
        result.warnings.Add( $"{GetHierarchyPath( gameObject )}: AGX mesh shape did not accept source mesh." );

      ConfigureCommonShapeSettings( meshShape, material );
      EditorUtility.SetDirty( meshShape );
    }

    private static void ConfigureCommonShapeSettings( Shape shape, ShapeMaterial material )
    {
      shape.CollisionsEnabled = true;
      shape.IsSensor = false;
      shape.Material = material;
    }

    private static bool IsMeasuredEnvironmentSolid( string objectName )
    {
      if ( string.IsNullOrEmpty( objectName ) )
        return false;

      return objectName == "FactoryFloor" ||
             objectName.StartsWith( "FactoryPost_", StringComparison.Ordinal ) ||
             objectName.StartsWith( "FactoryTopBeam_", StringComparison.Ordinal ) ||
             objectName.StartsWith( "FactoryFloorEdge_", StringComparison.Ordinal ) ||
             objectName.StartsWith( "FactoryWall_", StringComparison.Ordinal ) ||
             objectName.StartsWith( "FactoryWindow_", StringComparison.Ordinal ) ||
             objectName == "FactoryRoof_XMax_Extension" ||
             objectName == "FactoryRoof_SlopedSlab" ||
             objectName.StartsWith( "FactoryRoofSide_", StringComparison.Ordinal ) ||
             objectName == "FactoryCeilingTubeLight_01_Fixture" ||
             objectName == "FactoryCeilingTubeLight_01_Tube" ||
             objectName.StartsWith( "Dig_", StringComparison.Ordinal ) && objectName.EndsWith( "_Board", StringComparison.Ordinal ) ||
             objectName.StartsWith( "Dump_", StringComparison.Ordinal ) && objectName.EndsWith( "_Board", StringComparison.Ordinal );
    }

    private static bool ShouldUseMeshShape( string objectName )
    {
      return objectName.StartsWith( "FactoryRoofSide_", StringComparison.Ordinal );
    }

    private static void AuditFactoryAgxBodies( EnvironmentPhysicsResult result )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var gameObject in objects ) {
        if ( gameObject == null || !gameObject.scene.IsValid() || !IsMeasuredEnvironmentSolid( gameObject.name ) )
          continue;

        if ( gameObject.GetComponent<MeshRenderer>() == null || gameObject.GetComponent<MeshFilter>() == null )
          continue;

        result.static_body_candidates_checked++;
        var rb = gameObject.GetComponent<RigidBody>();
        var shape = gameObject.GetComponent<Shape>();
        var path = GetHierarchyPath( gameObject );

        if ( rb == null ) {
          result.agx_missing_or_incomplete.Add( $"{path}: missing AGX RigidBody" );
          continue;
        }

        if ( rb.MotionControl != agx.RigidBody.MotionControl.STATIC )
          result.agx_missing_or_incomplete.Add( $"{path}: AGX RigidBody is not STATIC" );

        if ( shape == null ) {
          result.agx_missing_or_incomplete.Add( $"{path}: missing AGX collision Shape" );
          continue;
        }

        if ( !shape.CollisionsEnabled )
          result.agx_missing_or_incomplete.Add( $"{path}: AGX collision Shape has CollisionsEnabled=false" );

        if ( shape.IsSensor )
          result.agx_missing_or_incomplete.Add( $"{path}: AGX collision Shape is configured as a sensor" );
      }
    }

    private static DeformableTerrainProperties EnsureDigTerrainProperties( EnvironmentPhysicsResult result )
    {
      var properties = AssetDatabase.LoadAssetAtPath<DeformableTerrainProperties>( DigTerrainPropertiesPath );
      if ( properties == null ) {
        properties = ScriptableObject.CreateInstance<DeformableTerrainProperties>();
        AssetDatabase.CreateAsset( properties, DigTerrainPropertiesPath );
        result.created_dig_terrain_properties = true;
      }

      ConfigureCommonTerrainProperties( properties, DigSoilParticleSizeScaling, DigSoilParticleGrowthRate );
      properties.SoilMergeSpeedThreshold = 4.0f;
      properties.SoilParticleMergeRate = 9.0f;
      properties.CreateParticlesEnabled = true;
      properties.CreateDynamicMassEnabled = true;

      EditorUtility.SetDirty( properties );
      result.dig_terrain_properties = AssetDatabase.GetAssetPath( properties );
      return properties;
    }

    private static DeformableTerrainProperties EnsureDumpTerrainProperties( EnvironmentPhysicsResult result )
    {
      var properties = AssetDatabase.LoadAssetAtPath<DeformableTerrainProperties>( DumpTerrainPropertiesPath );
      if ( properties == null ) {
        properties = ScriptableObject.CreateInstance<DeformableTerrainProperties>();
        AssetDatabase.CreateAsset( properties, DumpTerrainPropertiesPath );
        result.created_dump_terrain_properties = true;
      }

      ConfigureCommonTerrainProperties( properties, DigSoilParticleSizeScaling, DigSoilParticleGrowthRate );
      properties.SoilMergeSpeedThreshold = 4.0f;
      properties.SoilParticleMergeRate = 9.0f;
      properties.CreateParticlesEnabled = true;
      properties.CreateDynamicMassEnabled = true;

      EditorUtility.SetDirty( properties );
      result.dump_terrain_properties = AssetDatabase.GetAssetPath( properties );
      return properties;
    }

    private static void ConfigureCommonTerrainProperties( DeformableTerrainProperties properties,
                                                         float soilParticleSizeScaling,
                                                         float soilParticleGrowthRate )
    {
      properties.SoilParticleLifeTime = float.PositiveInfinity;
      properties.SoilParticleSizeScaling = soilParticleSizeScaling;
      properties.SoilParticleGrowthRate = soilParticleGrowthRate;
      properties.AvalancheDecayFraction = RuntimeAvalancheDecayFraction;
      properties.AvalancheMaxHeightGrowth = RuntimeAvalancheMaxHeightGrowth;
      properties.DeformationEnabled = true;
      properties.SoilCompactionEnabled = true;
      properties.DeleteSoilParticlesOutsideBoundsEnabled = false;
      properties.LockedBorderEnabled = false;
      properties.AvalanchingEnabled = true;
      properties.MaximumParticleActivationVolume = RuntimeMaximumParticleActivationVolume;
    }

    private static void ConfigureTerrainPhysics( DeformableTerrain terrain,
                                                 ShapeMaterial groundMaterial,
                                                 ShapeMaterial granularMaterial,
                                                 DeformableTerrainMaterial terrainMaterial,
                                                 DeformableTerrainProperties properties,
                                                 EnvironmentPhysicsResult result,
                                                 string terrainName )
    {
      if ( terrain == null ) {
        result.warnings.Add( $"{terrainName} was not found; terrain AGX material configuration skipped." );
        return;
      }

      if ( groundMaterial != null )
        terrain.Material = groundMaterial;
      if ( granularMaterial != null )
        terrain.ParticleMaterial = granularMaterial;
      if ( terrainMaterial != null )
        terrain.DefaultTerrainMaterial = terrainMaterial;
      if ( properties != null )
        terrain.TerrainProperties = properties;

      if ( terrainName == "DigTerrain" )
        EnsureTerrainParticleRenderer( terrain, result, terrainName );

      EditorUtility.SetDirty( terrain );
      result.terrain_physics_configured.Add( GetHierarchyPath( terrain.gameObject ) );
    }

    private static void EnsureTerrainParticleRenderer( DeformableTerrain terrain,
                                                       EnvironmentPhysicsResult result,
                                                       string terrainName )
    {
      var particleRenderer = terrain.GetComponent<AGXUnity.Rendering.DeformableTerrainParticleRenderer>();
      if ( particleRenderer == null )
        particleRenderer = terrain.gameObject.AddComponent<AGXUnity.Rendering.DeformableTerrainParticleRenderer>();

      particleRenderer.enabled = true;
      particleRenderer.RenderMode = AGXUnity.Rendering.DeformableTerrainParticleRenderer.GranuleRenderMode.DrawMeshInstanced;
      particleRenderer.SyncMode = AGXUnity.Rendering.DeformableTerrainParticleRenderer.SynchronizeMode.PostStepForward;
      particleRenderer.FilterParticles = false;

      var granulePrefab = AssetDatabase.LoadAssetAtPath<GameObject>( ParticleGranulePrefabPath );
      if ( granulePrefab != null )
        particleRenderer.GranuleInstance = granulePrefab;
      else
        result.warnings.Add( $"{terrainName} particle renderer could not find granule prefab at {ParticleGranulePrefabPath}." );

      EditorUtility.SetDirty( particleRenderer );
      result.terrain_particle_renderers.Add( GetHierarchyPath( terrain.gameObject ) );
    }

    private static void ConfigureDumpCompactor( DeformableTerrain digTerrain,
                                                DeformableTerrain dumpTerrain,
                                                EnvironmentPhysicsResult result )
    {
      if ( dumpTerrain == null ) {
        result.warnings.Add( "DumpTerrainReceiver was not found; dump particle compactor was not created." );
        return;
      }

      var compactor = dumpTerrain.GetComponent<global::DumpParticleStaticTerrainCompactor>();
      if ( compactor == null )
        compactor = dumpTerrain.gameObject.AddComponent<global::DumpParticleStaticTerrainCompactor>();

      var measurementFrame = FindSceneObject( "SubmergedBox" )?.transform ?? dumpTerrain.transform;
      var halfExtents = new Vector3( 1.25f, 0.5f, 1.5f );
      var measurementBox = measurementFrame != null ? measurementFrame.GetComponent<Box>() : null;
      if ( measurementBox != null )
        halfExtents = measurementBox.HalfExtents;
      var sources = digTerrain != null && digTerrain != dumpTerrain ?
                    new DeformableTerrainBase[] { digTerrain, dumpTerrain } :
                    new DeformableTerrainBase[] { dumpTerrain };

      compactor.Configure( dumpTerrain,
                           sources,
                           measurementFrame,
                           Vector3.zero,
                           halfExtents,
                           0.35f,
                           0.85f,
                           0.7f,
                           1600.0f );

      EditorUtility.SetDirty( compactor );
      EditorUtility.SetDirty( dumpTerrain.gameObject );
      result.dump_compactor = GetHierarchyPath( compactor.gameObject );
      result.dump_compactor_measurement_frame = measurementFrame != null ? GetHierarchyPath( measurementFrame.gameObject ) : "none";
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

    private static void EnsureOutputDirectoryExists()
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
    }

    private static string CaptureNamedCamera( string cameraName, string fileName )
    {
      var cameraObject = FindSceneObject( cameraName );
      var camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
      return camera == null ? null : CaptureCamera( camera, fileName );
    }

    private static string CaptureCamera( Camera camera, string fileName )
    {
      const int width = 1600;
      const int height = 1000;

      var absolutePath = Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), fileName );
      var previousTarget = camera.targetTexture;
      var previousActive = RenderTexture.active;
      var texture = new RenderTexture( width, height, 24, RenderTextureFormat.ARGB32 );
      var image = new Texture2D( width, height, TextureFormat.RGB24, false );

      try {
        camera.targetTexture = texture;
        RenderTexture.active = texture;
        camera.Render();
        image.ReadPixels( new Rect( 0, 0, width, height ), 0, 0 );
        image.Apply();
        File.WriteAllBytes( absolutePath, image.EncodeToPNG() );
      }
      finally {
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate( texture );
        UnityEngine.Object.DestroyImmediate( image );
      }

      return absolutePath;
    }

    private static void WriteResult( bool success, string message, EnvironmentPhysicsResult result )
    {
      EnsureOutputDirectoryExists();
      if ( result == null )
        result = new EnvironmentPhysicsResult();

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
    private sealed class EnvironmentPhysicsResult
    {
      public bool success;
      public string message;
      public int static_bodies_configured;
      public string dump_compactor;
      public string dump_compactor_measurement_frame;
      public string dig_terrain_properties;
      public string dump_terrain_properties;
      public bool created_dig_terrain_properties;
      public bool created_dump_terrain_properties;
      public int static_body_candidates_checked;
      public List<string> static_body_names = new List<string>();
      public List<string> agx_missing_or_incomplete = new List<string>();
      public List<string> terrain_physics_configured = new List<string>();
      public List<string> terrain_particle_renderers = new List<string>();
      public List<string> warnings = new List<string>();
      public ScreenshotSet screenshots;
    }

    [Serializable]
    private sealed class ScreenshotSet
    {
      public string dump;
      public string overview;
    }
  }
}
#endif
