#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity.Collide;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexMeasuredTerrainAreasUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexMeasuredTerrainAreas.request";
    private const string OutputDirectory = "Temp/CodexMeasuredTerrainAreas";
    private const string TerrainAssetDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Terrains";
    private const string GravelTerrainLayerPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/materials/Gravel_03-terrainlayer.terrainlayer";

    private const float EnvironmentScale = CodexSceneScaleConfig.DefaultEnvironmentScale;
    private const float DigSoilHeight = CodexSceneScaleConfig.DigSoilHeight;
    private const float TerrainVerticalScale = CodexSceneScaleConfig.TerrainVerticalScale;
    private const float TerrainMaximumDepth = CodexSceneScaleConfig.TerrainMaximumDepth;
    private const int HeightmapResolution = 33;

    private static readonly Vector2 DumpMin = new Vector2( 0.7f * EnvironmentScale, 0.2f * EnvironmentScale );
    private static readonly Vector2 DigMin = new Vector2( 4.0f * EnvironmentScale, 3.6f * EnvironmentScale );

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexMeasuredTerrainAreasUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Build Measured Dig And Dump Terrains" )]
    public static void BuildMeasuredTerrainAreasFromMenu()
    {
      BuildMeasuredTerrainAreas( "menu" );
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
      catch ( Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}", null );
        return;
      }

      BuildMeasuredTerrainAreas( "request-file" );
    }

    private static void BuildMeasuredTerrainAreas( string source )
    {
      s_isRunning = true;
      var result = new TerrainAreasResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        EnsureTerrainAssetDirectory();
        RemoveObjectIfPresent( "DigAreaFillRuntime" );

        var originalTerrain = ResolvePrimaryTerrain();
        if ( originalTerrain == null )
          originalTerrain = CreateTerrainObject( "DigTerrain" ).GetComponent<Terrain>();

        var originalDeformableTerrain = originalTerrain.GetComponent<DeformableTerrain>();
        var digTerrain = ConfigureDigTerrain( originalTerrain, result );
        var dumpTerrain = ConfigureDumpTerrain( originalDeformableTerrain ?? digTerrain, result );

        UpdateResetServiceTerrains( digTerrain, dumpTerrain, result );
        UpdateTerrainReferences( digTerrain, dumpTerrain, result );
        UpdateDumpSensor( digTerrain, result );
        CodexEnvironmentPhysicsUtility.ApplyEnvironmentPhysicsFromTerrainBuilder();

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        result.screenshots = new ScreenshotSet();

        result.message = $"Built measured terrain areas from {source}: DigTerrain and DumpTerrainReceiver now match the measured inner board footprints; DigTerrain is filled to {DigSoilHeight:0.###}m and DumpTerrainReceiver is flat/empty at 0m.";
        WriteResult( true, result.message, result );
      }
      catch ( Exception exception ) {
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
                     $"Active scene '{activeScene.path}' has unsaved changes; terrain builder did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static Terrain ResolvePrimaryTerrain()
    {
      foreach ( var preferredName in new[] { "DigTerrain", "Terrain" } ) {
        var named = FindSceneObject( preferredName );
        if ( named == null )
          continue;

        var terrain = named.GetComponent<Terrain>();
        if ( terrain != null )
          return terrain;
      }

      var deformableTerrains = Resources.FindObjectsOfTypeAll<DeformableTerrain>();
      foreach ( var deformableTerrain in deformableTerrains ) {
        if ( deformableTerrain != null && deformableTerrain.gameObject.scene.IsValid() )
          return deformableTerrain.GetComponent<Terrain>();
      }

      var terrains = Resources.FindObjectsOfTypeAll<Terrain>();
      foreach ( var terrain in terrains ) {
        if ( terrain != null && terrain.gameObject.scene.IsValid() )
          return terrain;
      }

      return null;
    }

    private static DeformableTerrain ConfigureDigTerrain( Terrain terrain, TerrainAreasResult result )
    {
      terrain.gameObject.name = "DigTerrain";
      var terrainSpec = CodexAreaFootprintUtility.ResolveTerrainSpec( "Dig", DigMin, result.warnings );
      var terrainData = CreateOrUpdateTerrainData( "CodexDigTerrain.asset", terrainSpec.InnerSizeXZ, DigSoilHeight );

      ConfigureUnityTerrain( terrain,
                             terrainData,
                             terrainSpec.TerrainWorldMin,
                             drawHeightmap: true );

      var deformableTerrain = terrain.GetComponent<DeformableTerrain>();
      if ( deformableTerrain == null )
        deformableTerrain = terrain.gameObject.AddComponent<DeformableTerrain>();

      ConfigureDeformableTerrain( deformableTerrain, deformableTerrain );
      EnsureResetTerrain( terrain.gameObject );

      result.dig_terrain_object = GetHierarchyPath( terrain.gameObject );
      result.dig_terrain_asset = AssetDatabase.GetAssetPath( terrainData );
      result.dig_world_min = FormatVector( terrain.transform.position );
      result.dig_world_size = FormatVector( terrainData.size );
      result.dig_soil_top_height_m = DigSoilHeight.ToString( "0.###", CultureInfo.InvariantCulture );
      AddAreaSpecDiagnostics( "Dig", terrainSpec, result );
      AddTerrainScaleDiagnostics( "DigTerrain", terrainData, result );

      return deformableTerrain;
    }

    private static DeformableTerrain ConfigureDumpTerrain( DeformableTerrain sourceSettings, TerrainAreasResult result )
    {
      var terrainObject = FindSceneObject( "DumpTerrainReceiver" );
      if ( terrainObject == null )
        terrainObject = CreateTerrainObject( "DumpTerrainReceiver" );

      var terrain = terrainObject.GetComponent<Terrain>();
      var collider = terrainObject.GetComponent<TerrainCollider>();
      var deformableTerrain = terrainObject.GetComponent<DeformableTerrain>();
      if ( deformableTerrain == null )
        deformableTerrain = terrainObject.AddComponent<DeformableTerrain>();

      var terrainSpec = CodexAreaFootprintUtility.ResolveTerrainSpec( "Dump", DumpMin, result.warnings );
      var terrainData = CreateOrUpdateTerrainData( "CodexDumpTerrainReceiver.asset", terrainSpec.InnerSizeXZ, 0.0f );

      ConfigureUnityTerrain( terrain,
                             terrainData,
                             terrainSpec.TerrainWorldMin,
                             drawHeightmap: true );
      if ( collider != null )
        collider.enabled = true;

      ConfigureDeformableTerrain( deformableTerrain, sourceSettings );
      EnsureResetTerrain( terrainObject );

      result.dump_terrain_object = GetHierarchyPath( terrainObject );
      result.dump_terrain_asset = AssetDatabase.GetAssetPath( terrainData );
      result.dump_world_min = FormatVector( terrainObject.transform.position );
      result.dump_world_size = FormatVector( terrainData.size );
      result.dump_initial_height_m = "0";
      AddAreaSpecDiagnostics( "Dump", terrainSpec, result );
      AddTerrainScaleDiagnostics( "DumpTerrainReceiver", terrainData, result );

      return deformableTerrain;
    }

    private static GameObject CreateTerrainObject( string name )
    {
      var gameObject = new GameObject( name );
      var sceneRoot = FindSceneObject( "=== Scene ===" );
      if ( sceneRoot != null )
        gameObject.transform.SetParent( sceneRoot.transform, true );

      gameObject.AddComponent<Terrain>();
      gameObject.AddComponent<TerrainCollider>();
      return gameObject;
    }

    private static TerrainData CreateOrUpdateTerrainData( string assetName, Vector2 sizeXZ, float topHeight )
    {
      var assetPath = $"{TerrainAssetDirectory}/{assetName}";
      var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>( assetPath );
      if ( terrainData == null ) {
        terrainData = new TerrainData();
        AssetDatabase.CreateAsset( terrainData, assetPath );
      }

      if ( terrainData.heightmapResolution != HeightmapResolution )
        terrainData.heightmapResolution = HeightmapResolution;

      terrainData.size = new Vector3( sizeXZ.x, TerrainVerticalScale, sizeXZ.y );
      terrainData.alphamapResolution = Mathf.Max( 16, terrainData.alphamapResolution );
      terrainData.baseMapResolution = Mathf.Max( 16, terrainData.baseMapResolution );
      ApplyTerrainLayer( terrainData );

      var normalizedHeight = Mathf.Clamp01( topHeight / TerrainVerticalScale );
      var heights = new float[ HeightmapResolution, HeightmapResolution ];
      for ( var z = 0; z < HeightmapResolution; ++z ) {
        for ( var x = 0; x < HeightmapResolution; ++x )
          heights[ z, x ] = normalizedHeight;
      }

      terrainData.SetHeights( 0, 0, heights );
      EditorUtility.SetDirty( terrainData );
      return terrainData;
    }

    private static void ApplyTerrainLayer( TerrainData terrainData )
    {
      var terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>( GravelTerrainLayerPath );
      if ( terrainLayer == null )
        return;

      terrainData.terrainLayers = new[] { terrainLayer };
    }

    private static void ConfigureUnityTerrain( Terrain terrain,
                                               TerrainData terrainData,
                                               Vector3 worldPosition,
                                               bool drawHeightmap )
    {
      terrain.transform.position = worldPosition;
      terrain.transform.rotation = Quaternion.identity;
      terrain.transform.localScale = Vector3.one;
      terrain.terrainData = terrainData;
      terrain.drawHeightmap = drawHeightmap;
      terrain.drawTreesAndFoliage = false;
      terrain.heightmapPixelError = 1.0f;

      var collider = terrain.GetComponent<TerrainCollider>();
      if ( collider == null )
        collider = terrain.gameObject.AddComponent<TerrainCollider>();

      collider.terrainData = terrainData;

      EditorUtility.SetDirty( terrain );
      EditorUtility.SetDirty( collider );
    }

    private static void ConfigureDeformableTerrain( DeformableTerrain terrain, DeformableTerrain sourceSettings )
    {
      if ( terrain == null )
        return;

      terrain.MaximumDepth = TerrainMaximumDepth;

      if ( sourceSettings != null && sourceSettings != terrain )
        CopySerializedObjectReference( sourceSettings, terrain, "m_material" );
      if ( sourceSettings != null && sourceSettings != terrain )
        CopySerializedObjectReference( sourceSettings, terrain, "m_particleMaterial" );
      if ( sourceSettings != null && sourceSettings != terrain )
        CopySerializedObjectReference( sourceSettings, terrain, "m_defaultTerrainMaterial" );
      if ( sourceSettings != null && sourceSettings != terrain )
        CopySerializedObjectReference( sourceSettings, terrain, "m_terrainProperties" );

      var serializedObject = new SerializedObject( terrain );
      var maximumDepth = serializedObject.FindProperty( "m_maximumDepth" );
      if ( maximumDepth != null )
        maximumDepth.floatValue = TerrainMaximumDepth;

      var initialCompaction = serializedObject.FindProperty( "<InitialCompaction>k__BackingField" );
      if ( initialCompaction != null )
        initialCompaction.floatValue = 1.0f;

      serializedObject.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty( terrain );
    }

    private static void CopySerializedObjectReference( DeformableTerrain source, DeformableTerrain target, string fieldName )
    {
      var sourceObject = new SerializedObject( source );
      var targetObject = new SerializedObject( target );
      var sourceProperty = sourceObject.FindProperty( fieldName );
      var targetProperty = targetObject.FindProperty( fieldName );
      if ( sourceProperty == null || targetProperty == null )
        return;

      targetProperty.objectReferenceValue = sourceProperty.objectReferenceValue;
      targetObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void UpdateResetServiceTerrains( DeformableTerrain digTerrain,
                                                    DeformableTerrain dumpTerrain,
                                                    TerrainAreasResult result )
    {
      var resetTerrains = new List<global::ResetTerrain>();
      var digReset = digTerrain != null ? digTerrain.GetComponent<global::ResetTerrain>() : null;
      var dumpReset = dumpTerrain != null ? dumpTerrain.GetComponent<global::ResetTerrain>() : null;
      if ( digReset != null )
        resetTerrains.Add( digReset );
      if ( dumpReset != null )
        resetTerrains.Add( dumpReset );

      var services = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in services ) {
        if ( component == null || !component.gameObject.scene.IsValid() ||
             component.GetType().FullName != "AGXUnity_Excavator.Scripts.Experiment.SceneResetService" )
          continue;

        var serializedObject = new SerializedObject( component );
        SetObjectArray( serializedObject.FindProperty( "m_resetTerrains" ), resetTerrains );
        SetObjectArray( serializedObject.FindProperty( "m_fallbackTerrains" ), new List<DeformableTerrain> { digTerrain, dumpTerrain } );
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( component );
        result.updated_reset_service = true;
      }
    }

    private static void UpdateTerrainReferences( DeformableTerrain digTerrain,
                                                 DeformableTerrain dumpTerrain,
                                                 TerrainAreasResult result )
    {
      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
        if ( component == null || !component.gameObject.scene.IsValid() )
          continue;

        var serializedObject = new SerializedObject( component );
        var property = serializedObject.GetIterator();
        var changed = false;
        if ( property.NextVisible( true ) ) {
          do {
            if ( property.propertyType != SerializedPropertyType.ObjectReference ||
                 property.name != "m_terrain" ||
                 property.objectReferenceValue == null )
              continue;

            if ( property.objectReferenceValue is DeformableTerrain )
            {
              property.objectReferenceValue = digTerrain;
              changed = true;
            }
          }
          while ( property.NextVisible( false ) );
        }

        if ( changed ) {
          serializedObject.ApplyModifiedPropertiesWithoutUndo();
          EditorUtility.SetDirty( component );
          result.updated_m_terrain_references++;
        }
      }

      if ( dumpTerrain == null )
        result.warnings.Add( "Dump terrain receiver could not be created or resolved." );
    }

    private static void UpdateDumpSensor( DeformableTerrain digTerrain, TerrainAreasResult result )
    {
      var dumpArea = CodexAreaFootprintUtility.FindMassSensorObjectByTargetName( "DumpArea" ) ??
                     FindSceneObject( "DumpArea" ) ??
                     FindSceneObject( "SubmergedBox" );
      if ( dumpArea == null ) {
        result.warnings.Add( "DumpArea sensor was not found; dump receiver terrain was still created." );
        return;
      }

      var dumpSpec = CodexAreaFootprintUtility.ResolveTerrainSpec( "Dump", DumpMin, result.warnings );
      dumpArea.SetActive( true );
      var box = dumpArea.GetComponent<Box>();
      var halfExtentsY = box != null && box.HalfExtents.y > 0.0f ?
                           box.HalfExtents.y :
                           dumpSpec.FullAreaHalfExtents.y;
      var positionY = dumpArea.transform.position.y;
      if ( dumpSpec.FromFootprint && Mathf.Approximately( positionY, 0.0f ) )
        positionY = dumpSpec.FootprintBottomY + halfExtentsY;

      dumpArea.transform.position = new Vector3( dumpSpec.FootprintCenter.x, positionY, dumpSpec.FootprintCenter.z );
      dumpArea.transform.rotation = Quaternion.identity;

      if ( box != null )
        box.HalfExtents = new Vector3( dumpSpec.FootprintSizeXZ.x * 0.5f,
                                       halfExtentsY,
                                       dumpSpec.FootprintSizeXZ.y * 0.5f );

      var sensor = dumpArea.GetComponent<global::TerrainParticleBoxMassSensor>();
      if ( sensor != null ) {
        sensor.m_terrain = digTerrain;
        EditorUtility.SetDirty( sensor );
      }

      SetBoxVisualRenderers( dumpArea, false );
      if ( box != null )
        EditorUtility.SetDirty( box );
      EditorUtility.SetDirty( dumpArea );
      result.updated_dump_sensor = true;
    }

    private static void SetBoxVisualRenderers( GameObject root, bool enabled )
    {
      var renderers = root.GetComponentsInChildren<Renderer>( true );
      foreach ( var renderer in renderers ) {
        if ( renderer == null || renderer.gameObject.name.IndexOf( "Box_Visual", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        renderer.enabled = enabled;
        EditorUtility.SetDirty( renderer );
      }
    }

    private static global::ResetTerrain EnsureResetTerrain( GameObject terrainObject )
    {
      var resetTerrain = terrainObject.GetComponent<global::ResetTerrain>();
      if ( resetTerrain == null )
        resetTerrain = terrainObject.AddComponent<global::ResetTerrain>();

      return resetTerrain;
    }

    private static void SetObjectArray<T>( SerializedProperty property, List<T> values )
      where T : UnityEngine.Object
    {
      if ( property == null )
        return;

      property.arraySize = values.Count;
      for ( var index = 0; index < values.Count; ++index )
        property.GetArrayElementAtIndex( index ).objectReferenceValue = values[ index ];
    }

    private static void CreateOrUpdateTerrainInspectionCameras()
    {
      CreateOrUpdateCamera( "CodexTerrainAreasTopCamera",
                            new Vector3( 3.5f, 8.0f, 3.5f ),
                            new Vector3( 3.5f, 0.0f, 3.5f ),
                            4.05f,
                            true );
      CreateOrUpdateCamera( "CodexTerrainAreasDigCamera",
                            new Vector3( 2.9f, 2.15f, 7.55f ),
                            new Vector3( 5.25f, 0.48f, 5.1f ),
                            34.0f,
                            false );
      CreateOrUpdateCamera( "CodexTerrainAreasDumpCamera",
                            new Vector3( 4.15f, 2.1f, -1.15f ),
                            new Vector3( 1.95f, 0.32f, 1.7f ),
                            36.0f,
                            false );
      CreateOrUpdateCamera( "CodexTerrainAreasOverviewCamera",
                            new Vector3( 7.9f, 4.3f, -2.6f ),
                            new Vector3( 3.5f, 0.72f, 3.55f ),
                            50.0f,
                            false );
    }

    private static void CreateOrUpdateCamera( string name,
                                              Vector3 position,
                                              Vector3 lookAt,
                                              float sizeOrFov,
                                              bool orthographic )
    {
      var cameraObject = FindSceneObject( name );
      if ( cameraObject == null )
        cameraObject = new GameObject( name );

      cameraObject.transform.position = position;
      cameraObject.transform.LookAt( lookAt );

      var camera = cameraObject.GetComponent<Camera>();
      if ( camera == null )
        camera = cameraObject.AddComponent<Camera>();

      camera.clearFlags = CameraClearFlags.Skybox;
      camera.nearClipPlane = 0.02f;
      camera.farClipPlane = 100.0f;
      camera.orthographic = orthographic;
      if ( orthographic )
        camera.orthographicSize = sizeOrFov;
      else
        camera.fieldOfView = sizeOrFov;

      EditorUtility.SetDirty( cameraObject );
      EditorUtility.SetDirty( camera );
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
      var renderTexture = new RenderTexture( width, height, 24 );
      var texture = new Texture2D( width, height, TextureFormat.RGB24, false );

      try {
        camera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        camera.Render();
        texture.ReadPixels( new Rect( 0, 0, width, height ), 0, 0 );
        texture.Apply();
        File.WriteAllBytes( absolutePath, texture.EncodeToPNG() );
        return absolutePath;
      }
      finally {
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate( texture );
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate( renderTexture );
      }
    }

    private static void EnsureTerrainAssetDirectory()
    {
      if ( AssetDatabase.IsValidFolder( TerrainAssetDirectory ) )
        return;

      AssetDatabase.CreateFolder( "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets", "Terrains" );
    }

    private static void EnsureOutputDirectoryExists()
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var candidate in objects ) {
        if ( candidate == null || candidate.name != objectName || !candidate.scene.IsValid() )
          continue;

        return candidate;
      }

      return null;
    }

    private static void RemoveObjectIfPresent( string objectName )
    {
      var candidate = FindSceneObject( objectName );
      if ( candidate != null )
        UnityEngine.Object.DestroyImmediate( candidate );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath ) );
    }

    private static string GetHierarchyPath( GameObject gameObject )
    {
      if ( gameObject == null )
        return "null";

      var names = new List<string>();
      var current = gameObject.transform;
      while ( current != null ) {
        names.Add( current.name );
        current = current.parent;
      }

      names.Reverse();
      return string.Join( "/", names );
    }

    private static string FormatVector( Vector3 vector )
    {
      return $"({vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###})";
    }

    private static string FormatVector( Vector2 vector )
    {
      return $"({vector.x:0.###}, {vector.y:0.###})";
    }

    private static void AddAreaSpecDiagnostics( string areaName,
                                                CodexAreaFootprintUtility.TerrainSpec terrainSpec,
                                                TerrainAreasResult result )
    {
      if ( result == null )
        return;

      result.terrain_cell_metrics.Add(
        $"{areaName}: footprint_xz={FormatVector( terrainSpec.FootprintSizeXZ )}, board_thickness_m={terrainSpec.BoardThickness.ToString( "0.###", CultureInfo.InvariantCulture )}, inner_board_xz={FormatVector( terrainSpec.InnerSizeXZ )}, terrain_world_min={FormatVector( terrainSpec.TerrainWorldMin )}" );
    }

    private static void AddTerrainScaleDiagnostics( string terrainName, TerrainData terrainData, TerrainAreasResult result )
    {
      if ( terrainData == null || result == null )
        return;

      var denominator = Mathf.Max( 1, terrainData.heightmapResolution - 1 );
      var agxElementSize = terrainData.size.x / denominator;
      var unityZCellSize = terrainData.size.z / denominator;
      result.terrain_cell_metrics.Add(
        $"{terrainName}: agx_element_size_x_m={agxElementSize.ToString( "0.#####", CultureInfo.InvariantCulture )}, unity_z_cell_size_m={unityZCellSize.ToString( "0.#####", CultureInfo.InvariantCulture )}, resolution={terrainData.heightmapResolution}" );

      // AGXUnity.DeformableTerrain passes a single native element size derived from TerrainData.size.x.
      if ( !Mathf.Approximately( terrainData.size.x, terrainData.size.z ) )
        result.warnings.Add( $"{terrainName} is non-square; AGX native terrain uses X cell size while Unity visual terrain also has Z cell size." );
    }

    private static void WriteResult( bool success, string message, TerrainAreasResult result )
    {
      EnsureOutputDirectoryExists();
      if ( result == null )
        result = new TerrainAreasResult();

      result.success = success;
      result.message = message;

      var json = JsonUtility.ToJson( result, true );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ), json );
      Debug.Log( $"Codex measured terrain areas: {message}" );
    }

    [Serializable]
    private sealed class TerrainAreasResult
    {
      public bool success;
      public string message;
      public string dig_terrain_object;
      public string dig_terrain_asset;
      public string dig_world_min;
      public string dig_world_size;
      public string dig_soil_top_height_m;
      public string dump_terrain_object;
      public string dump_terrain_asset;
      public string dump_world_min;
      public string dump_world_size;
      public string dump_initial_height_m;
      public bool updated_reset_service;
      public bool updated_dump_sensor;
      public int updated_m_terrain_references;
      public List<string> terrain_cell_metrics = new List<string>();
      public List<string> warnings = new List<string>();
      public ScreenshotSet screenshots;
    }

    [Serializable]
    private sealed class ScreenshotSet
    {
      public string top_down;
      public string dig_close;
      public string dump_close;
      public string overview;
    }
  }
}
#endif
