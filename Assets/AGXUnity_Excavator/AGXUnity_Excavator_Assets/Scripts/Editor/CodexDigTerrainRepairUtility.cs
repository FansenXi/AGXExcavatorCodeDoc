#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexDigTerrainRepairUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexDigTerrainRepair.request";
    private const string OutputDirectory = "Temp/CodexDigTerrainRepair";
    private const string TerrainAssetDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Terrains";
    private const string DigTerrainAssetPath = TerrainAssetDirectory + "/CodexDigTerrain.asset";
    private const string GravelTerrainLayerPath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/materials/Gravel_03-terrainlayer.terrainlayer";
    private const string RuntimeDigAreaFillMaterialName = "DigAreaFillRuntime";
    private const string RuntimeDigAreaContourMaterialName = "DigAreaContourRuntime";

    private const float EnvironmentScale = CodexSceneScaleConfig.DefaultEnvironmentScale;
    private const float DigSoilHeight = CodexSceneScaleConfig.DigSoilHeight;
    private const float TerrainVerticalScale = CodexSceneScaleConfig.TerrainVerticalScale;
    private const float TerrainMaximumDepth = CodexSceneScaleConfig.TerrainMaximumDepth;
    private const int HeightmapResolution = 33;

    private static readonly Vector2 DigMin = new Vector2( 4.0f * EnvironmentScale, 3.6f * EnvironmentScale );

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexDigTerrainRepairUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Repair Dig Terrain Only" )]
    public static void RepairDigTerrainFromMenu()
    {
      RepairDigTerrain( "menu" );
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

      RepairDigTerrain( "request-file" );
    }

    private static void RepairDigTerrain( string source )
    {
      s_isRunning = true;
      var result = new DigTerrainRepairResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        Directory.CreateDirectory( GetProjectRelativeAbsolutePath( TerrainAssetDirectory ) );

        var terrainObject = FindSceneObject( "DigTerrain" );
        if ( terrainObject == null )
          terrainObject = CreateTerrainObject( "DigTerrain" );

        terrainObject.SetActive( true );

        var terrain = terrainObject.GetComponent<Terrain>();
        if ( terrain == null )
          terrain = terrainObject.AddComponent<Terrain>();

        var collider = terrainObject.GetComponent<TerrainCollider>();
        if ( collider == null )
          collider = terrainObject.AddComponent<TerrainCollider>();

        var terrainSpec = CodexAreaFootprintUtility.ResolveTerrainSpec( "Dig", DigMin );
        var terrainData = CreateOrUpdateDigTerrainData( terrainSpec.InnerSizeXZ );
        var worldPosition = terrainSpec.TerrainWorldMin;

        terrain.transform.position = worldPosition;
        terrain.transform.rotation = Quaternion.identity;
        terrain.transform.localScale = Vector3.one;
        terrain.terrainData = terrainData;
        terrain.drawHeightmap = true;
        terrain.drawTreesAndFoliage = false;
        terrain.heightmapPixelError = 1.0f;

        collider.enabled = true;
        collider.terrainData = terrainData;

        var deformableTerrain = terrainObject.GetComponent<DeformableTerrain>();
        if ( deformableTerrain != null ) {
          deformableTerrain.MaximumDepth = TerrainMaximumDepth;

          var serializedObject = new SerializedObject( deformableTerrain );
          var maximumDepth = serializedObject.FindProperty( "m_maximumDepth" );
          if ( maximumDepth != null )
            maximumDepth.floatValue = TerrainMaximumDepth;
          var initialCompaction = serializedObject.FindProperty( "<InitialCompaction>k__BackingField" );
          if ( initialCompaction != null )
            initialCompaction.floatValue = 1.0f;
          serializedObject.ApplyModifiedPropertiesWithoutUndo();

          EditorUtility.SetDirty( deformableTerrain );
        }

        EditorUtility.SetDirty( terrainObject );
        EditorUtility.SetDirty( terrain );
        EditorUtility.SetDirty( collider );
        EditorUtility.SetDirty( terrainData );

        result.runtime_area_material_references_removed = ClearSavedRuntimeAreaMaterials();
        result.runtime_area_material_cleanup =
          $"{result.runtime_area_material_references_removed} saved runtime DigArea material reference(s) removed";

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );

        result.dig_terrain_object = GetHierarchyPath( terrainObject );
        result.world_position = FormatVector( terrain.transform.position );
        result.world_size = FormatVector( terrainData.size );
        result.soil_top_height_m = DigSoilHeight.ToString( "0.###", CultureInfo.InvariantCulture );
        result.inner_board_size_xz = FormatVector( terrainSpec.InnerSizeXZ );
        result.board_thickness_m = terrainSpec.BoardThickness.ToString( "0.###", CultureInfo.InvariantCulture );
        result.message = $"Repaired DigTerrain from {source}: moved it into the dig frame and rebuilt the filled soil height.";
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

    private static TerrainData CreateOrUpdateDigTerrainData( Vector2 sizeXZ )
    {
      var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>( DigTerrainAssetPath );
      if ( terrainData == null ) {
        terrainData = new TerrainData();
        AssetDatabase.CreateAsset( terrainData, DigTerrainAssetPath );
      }

      if ( terrainData.heightmapResolution != HeightmapResolution )
        terrainData.heightmapResolution = HeightmapResolution;

      terrainData.size = new Vector3( sizeXZ.x, TerrainVerticalScale, sizeXZ.y );
      terrainData.alphamapResolution = Mathf.Max( 16, terrainData.alphamapResolution );
      terrainData.baseMapResolution = Mathf.Max( 16, terrainData.baseMapResolution );

      var terrainLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>( GravelTerrainLayerPath );
      if ( terrainLayer != null )
        terrainData.terrainLayers = new[] { terrainLayer };

      var normalizedHeight = Mathf.Clamp01( DigSoilHeight / TerrainVerticalScale );
      var heights = new float[ HeightmapResolution, HeightmapResolution ];
      for ( var z = 0; z < HeightmapResolution; ++z ) {
        for ( var x = 0; x < HeightmapResolution; ++x )
          heights[ z, x ] = normalizedHeight;
      }

      terrainData.SetHeights( 0, 0, heights );
      return terrainData;
    }

    private static int ClearSavedRuntimeAreaMaterials()
    {
      var removedReferences = 0;
      var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
      foreach ( var renderer in renderers ) {
        if ( renderer == null || !renderer.gameObject.scene.IsValid() )
          continue;

        var sharedMaterials = renderer.sharedMaterials;
        var changed = false;
        for ( var i = 0; i < sharedMaterials.Length; ++i ) {
          if ( !IsRuntimeDigAreaMaterial( sharedMaterials[ i ] ) )
            continue;

          sharedMaterials[ i ] = null;
          changed = true;
          ++removedReferences;
        }

        if ( changed ) {
          renderer.sharedMaterials = sharedMaterials;
          EditorUtility.SetDirty( renderer );
        }
      }

      var materials = Resources.FindObjectsOfTypeAll<Material>();
      foreach ( var material in materials ) {
        if ( IsRuntimeDigAreaMaterial( material ) && !AssetDatabase.Contains( material ) )
          UnityEngine.Object.DestroyImmediate( material );
      }

      return removedReferences;
    }

    private static bool IsRuntimeDigAreaMaterial( Material material )
    {
      if ( material == null || string.IsNullOrEmpty( material.name ) )
        return false;

      return material.name.StartsWith( RuntimeDigAreaFillMaterialName, StringComparison.Ordinal ) ||
             material.name.StartsWith( RuntimeDigAreaContourMaterialName, StringComparison.Ordinal );
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

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; dig terrain repair did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
      foreach ( var transform in allTransforms ) {
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name == objectName )
          return transform.gameObject;
      }

      return null;
    }

    private static string GetHierarchyPath( GameObject gameObject )
    {
      if ( gameObject == null )
        return string.Empty;

      var path = gameObject.name;
      var current = gameObject.transform.parent;
      while ( current != null ) {
        path = current.name + "/" + path;
        current = current.parent;
      }

      return path;
    }

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture,
                            "({0:0.###}, {1:0.###}, {2:0.###})",
                            value.x,
                            value.y,
                            value.z );
    }

    private static string FormatVector( Vector2 value )
    {
      return string.Format( CultureInfo.InvariantCulture,
                            "({0:0.###}, {1:0.###})",
                            value.x,
                            value.y );
    }

    private static void WriteResult( bool success, string message, DigTerrainRepairResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new DigTerrainRepairResult();

      result.success = success;
      result.message = message;

      File.WriteAllText( GetProjectRelativeAbsolutePath( Path.Combine( OutputDirectory, "result.json" ) ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string path )
    {
      return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), path ) );
    }

    [Serializable]
    private class DigTerrainRepairResult
    {
      public bool success;
      public string message;
      public string dig_terrain_object;
      public string world_position;
      public string world_size;
      public string soil_top_height_m;
      public string inner_board_size_xz;
      public string board_thickness_m;
      public int runtime_area_material_references_removed;
      public string runtime_area_material_cleanup;
    }
  }
}
#endif
