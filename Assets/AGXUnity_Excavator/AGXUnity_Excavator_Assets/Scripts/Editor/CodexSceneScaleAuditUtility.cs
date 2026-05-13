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
  public static class CodexSceneScaleAuditUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexSceneScaleAudit.request";
    private const string RepairRequestPath = "Temp/CodexSceneScaleRepair.request";
    private const string OutputDirectory = "Temp/CodexSceneScaleAudit";
    private const string RepairOutputDirectory = "Temp/CodexSceneScaleRepair";
    private const float Tolerance = 0.015f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexSceneScaleAuditUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Audit Scene Environment Scale" )]
    public static void AuditSceneEnvironmentScaleFromMenu()
    {
      AuditSceneEnvironmentScale( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
      {
        var repairRequestPath = GetProjectRelativeAbsolutePath( RepairRequestPath );
        if ( !File.Exists( repairRequestPath ) )
          return;

        try {
          File.Delete( repairRequestPath );
        }
        catch ( System.Exception exception ) {
          WriteRepairResult( new SceneScaleRepairResult {
            success = false,
            message = "Could not delete repair request file: " + exception.Message
          } );
          return;
        }

        RepairSceneEnvironmentDimensions( "request-file" );
        return;
      }

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new SceneScaleAuditResult {
          success = false,
          message = "Could not delete request file: " + exception.Message
        } );
        return;
      }

      AuditSceneEnvironmentScale( "request-file" );
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Repair Audited Scene Environment Dimensions" )]
    public static void RepairSceneEnvironmentDimensionsFromMenu()
    {
      RepairSceneEnvironmentDimensions( "menu" );
    }

    private static void AuditSceneEnvironmentScale( string source )
    {
      s_isRunning = true;
      var result = new SceneScaleAuditResult {
        source = source,
        expected_environment_scale = FormatFloat( CodexSceneScaleConfig.DefaultEnvironmentScale ),
        expected_room_size = FormatVector( new Vector3( CodexSceneScaleConfig.RoomSizeX,
                                                        CodexSceneScaleConfig.RoomHeight,
                                                        CodexSceneScaleConfig.RoomSizeZ ) ),
        expected_area_size = FormatVector( new Vector3( CodexSceneScaleConfig.AreaSizeX,
                                                        CodexSceneScaleConfig.AreaHeight,
                                                        CodexSceneScaleConfig.AreaSizeZ ) ),
        expected_board_thickness = FormatFloat( CodexSceneScaleConfig.BoardThickness ),
        expected_dig_soil_height = FormatFloat( CodexSceneScaleConfig.DigSoilHeight ),
        expected_terrain_size = FormatVector( ExpectedTerrainSize() )
      };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          result.message = "Target scene is not open and could not be opened safely.";
          WriteResult( result );
          return;
        }

        result.scene_path = scene.path;
        AddInferredScaleChecks( result );
        AddFactoryChecks( result );
        AddAreaChecks( "Dig", result );
        AddAreaChecks( "Dump", result );
        AddAgxBoxCheck( "AGXUnity.RigidBody.DigArea",
                        new Vector3( CodexSceneScaleConfig.AreaSizeX * 0.5f,
                                     0.025f * CodexSceneScaleConfig.DefaultEnvironmentScale,
                                     CodexSceneScaleConfig.AreaSizeZ * 0.5f ),
                        result );
        AddAgxBoxCheck( "SubmergedBox",
                        new Vector3( CodexSceneScaleConfig.AreaSizeX * 0.5f,
                                     CodexSceneScaleConfig.AreaHeight * 0.5f,
                                     CodexSceneScaleConfig.AreaSizeZ * 0.5f ),
                        result );
        AddTerrainCheck( "DigTerrain", ExpectedTerrainSize(), CodexSceneScaleConfig.TerrainMaximumDepth, result );
        AddTerrainCheck( "DumpTerrainReceiver", ExpectedTerrainSize(), CodexSceneScaleConfig.TerrainMaximumDepth, result );

        result.success = result.failures.Count == 0;
        result.message = result.success ?
                           "Scene scale audit passed." :
                           $"Scene scale audit found {result.failures.Count} mismatch(es).";
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

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( EditorApplication.isPlayingOrWillChangePlaymode ) {
        WriteResult( new SceneScaleAuditResult {
          success = false,
          message = $"Active scene is '{activeScene.path}', and the editor is in play mode; scale audit did not switch scenes."
        } );
        return default;
      }

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( new SceneScaleAuditResult {
          success = false,
          message = $"Active scene '{activeScene.path}' has unsaved changes; scale audit did not switch scenes."
        } );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static void RepairSceneEnvironmentDimensions( string source )
    {
      s_isRunning = true;
      var result = new SceneScaleRepairResult { source = source };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          result.message = "Target scene is not open and could not be opened safely.";
          WriteRepairResult( result );
          return;
        }

        AddFactoryRepairs( result );
        AddAreaRepairs( "Dig", result );
        AddAreaRepairs( "Dump", result );
        RepairAgxBox( "AGXUnity.RigidBody.DigArea",
                      new Vector3( CodexSceneScaleConfig.AreaSizeX * 0.5f,
                                   0.025f * CodexSceneScaleConfig.DefaultEnvironmentScale,
                                   CodexSceneScaleConfig.AreaSizeZ * 0.5f ),
                      result );
        RepairAgxBox( "SubmergedBox",
                      new Vector3( CodexSceneScaleConfig.AreaSizeX * 0.5f,
                                   CodexSceneScaleConfig.AreaHeight * 0.5f,
                                   CodexSceneScaleConfig.AreaSizeZ * 0.5f ),
                      result );
        RepairTerrain( "DigTerrain", ExpectedTerrainSize(), CodexSceneScaleConfig.TerrainMaximumDepth, result );
        RepairTerrain( "DumpTerrainReceiver", ExpectedTerrainSize(), CodexSceneScaleConfig.TerrainMaximumDepth, result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.success = result.failures.Count == 0;
        result.message = result.success ?
                           $"Repaired scene environment dimensions; changed {result.changed_items.Count} item(s)." :
                           $"Scene environment repair completed with {result.failures.Count} failure(s).";
        WriteRepairResult( result );
      }
      catch ( System.Exception exception ) {
        result.success = false;
        result.message = exception.ToString();
        WriteRepairResult( result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static Vector3 ExpectedTerrainSize()
    {
      return new Vector3( CodexSceneScaleConfig.AreaSizeX - 2.0f * CodexSceneScaleConfig.BoardThickness,
                          CodexSceneScaleConfig.TerrainVerticalScale,
                          CodexSceneScaleConfig.AreaSizeZ - 2.0f * CodexSceneScaleConfig.BoardThickness );
    }

    private static void AddInferredScaleChecks( SceneScaleAuditResult result )
    {
      var floor = FindSceneObject( "FactoryFloor" );
      if ( floor != null ) {
        var scale = Mathf.Abs( floor.transform.lossyScale.x ) / CodexSceneScaleConfig.BaseRoomSizeX;
        result.inferred_scale_from_factory_floor = FormatFloat( scale );
        if ( !Approximately( scale, CodexSceneScaleConfig.DefaultEnvironmentScale ) )
          result.failures.Add( $"FactoryFloor inferred scale {FormatFloat( scale )} != {FormatFloat( CodexSceneScaleConfig.DefaultEnvironmentScale )}" );
      }

      var digBox = FindFirstAgxBox( "AGXUnity.RigidBody.DigArea" );
      if ( digBox != null ) {
        var scale = ( digBox.HalfExtents.x * 2.0f ) / CodexSceneScaleConfig.BaseAreaSizeX;
        result.inferred_scale_from_dig_area = FormatFloat( scale );
        if ( !Approximately( scale, CodexSceneScaleConfig.DefaultEnvironmentScale ) )
          result.failures.Add( $"DigArea inferred scale {FormatFloat( scale )} != {FormatFloat( CodexSceneScaleConfig.DefaultEnvironmentScale )}" );
      }

      var submergedBox = FindFirstAgxBox( "SubmergedBox" );
      if ( submergedBox != null ) {
        var scale = ( submergedBox.HalfExtents.x * 2.0f ) / CodexSceneScaleConfig.BaseAreaSizeX;
        result.inferred_scale_from_submerged_box = FormatFloat( scale );
        if ( !Approximately( scale, CodexSceneScaleConfig.DefaultEnvironmentScale ) )
          result.failures.Add( $"SubmergedBox inferred scale {FormatFloat( scale )} != {FormatFloat( CodexSceneScaleConfig.DefaultEnvironmentScale )}" );
      }
    }

    private static void AddFactoryChecks( SceneScaleAuditResult result )
    {
      var scale = CodexSceneScaleConfig.DefaultEnvironmentScale;
      var frameThickness = 0.06f * scale;
      var wallPanelThickness = 0.035f * scale;
      var wallPanelHeight = CodexSceneScaleConfig.RoomHeight - frameThickness - frameThickness * 0.5f;
      var wallPanelSpan = CodexSceneScaleConfig.RoomSizeX - frameThickness * 2.0f;

      AddDimensionCheck( "FactoryFloor",
                         new Vector3( CodexSceneScaleConfig.RoomSizeX, 0.05f * scale, CodexSceneScaleConfig.RoomSizeZ ),
                         result );
      AddDimensionCheck( "FactoryFloorEdge_ZMin",
                         new Vector3( CodexSceneScaleConfig.RoomSizeX, frameThickness, frameThickness ),
                         result );
      AddDimensionCheck( "FactoryFloorEdge_ZMax",
                         new Vector3( CodexSceneScaleConfig.RoomSizeX, frameThickness, frameThickness ),
                         result );
      AddDimensionCheck( "FactoryFloorEdge_XMin",
                         new Vector3( frameThickness, frameThickness, CodexSceneScaleConfig.RoomSizeZ ),
                         result );
      AddDimensionCheck( "FactoryFloorEdge_XMax",
                         new Vector3( frameThickness, frameThickness, CodexSceneScaleConfig.RoomSizeZ ),
                         result );
      AddDimensionCheck( "FactoryWall_ZMin",
                         new Vector3( wallPanelSpan, wallPanelHeight, wallPanelThickness ),
                         result );
      AddDimensionCheck( "FactoryWall_ZMax",
                         new Vector3( wallPanelSpan, wallPanelHeight, wallPanelThickness ),
                         result );
    }

    private static void AddFactoryRepairs( SceneScaleRepairResult result )
    {
      var scale = CodexSceneScaleConfig.DefaultEnvironmentScale;
      var frameThickness = 0.06f * scale;
      var wallPanelThickness = 0.035f * scale;
      var wallPanelHeight = CodexSceneScaleConfig.RoomHeight - frameThickness - frameThickness * 0.5f;
      var wallPanelSpan = CodexSceneScaleConfig.RoomSizeX - frameThickness * 2.0f;

      RepairDimension( "FactoryFloor",
                       new Vector3( CodexSceneScaleConfig.RoomSizeX, 0.05f * scale, CodexSceneScaleConfig.RoomSizeZ ),
                       result );
      RepairDimension( "FactoryFloorEdge_ZMin",
                       new Vector3( CodexSceneScaleConfig.RoomSizeX, frameThickness, frameThickness ),
                       result );
      RepairDimension( "FactoryFloorEdge_ZMax",
                       new Vector3( CodexSceneScaleConfig.RoomSizeX, frameThickness, frameThickness ),
                       result );
      RepairDimension( "FactoryFloorEdge_XMin",
                       new Vector3( frameThickness, frameThickness, CodexSceneScaleConfig.RoomSizeZ ),
                       result );
      RepairDimension( "FactoryFloorEdge_XMax",
                       new Vector3( frameThickness, frameThickness, CodexSceneScaleConfig.RoomSizeZ ),
                       result );
      RepairDimension( "FactoryWall_ZMin", new Vector3( wallPanelSpan, wallPanelHeight, wallPanelThickness ), result );
      RepairDimension( "FactoryWall_ZMax", new Vector3( wallPanelSpan, wallPanelHeight, wallPanelThickness ), result );
    }

    private static void AddAreaChecks( string areaName, SceneScaleAuditResult result )
    {
      var boardThickness = CodexSceneScaleConfig.BoardThickness;
      var areaSizeX = CodexSceneScaleConfig.AreaSizeX;
      var areaSizeZ = CodexSceneScaleConfig.AreaSizeZ;
      var areaHeight = CodexSceneScaleConfig.AreaHeight;
      var footprintHeight = 0.03f * CodexSceneScaleConfig.DefaultEnvironmentScale;

      AddDimensionCheck( areaName + "_XMin_Board", new Vector3( boardThickness, areaHeight, areaSizeZ ), result );
      AddDimensionCheck( areaName + "_XMax_Board", new Vector3( boardThickness, areaHeight, areaSizeZ ), result );
      AddDimensionCheck( areaName + "_ZMin_Board", new Vector3( areaSizeX, areaHeight, boardThickness ), result );
      AddDimensionCheck( areaName + "_ZMax_Board", new Vector3( areaSizeX, areaHeight, boardThickness ), result );
      AddDimensionCheck( areaName + "_Footprint", new Vector3( areaSizeX, footprintHeight, areaSizeZ ), result );
    }

    private static void AddAreaRepairs( string areaName, SceneScaleRepairResult result )
    {
      var boardThickness = CodexSceneScaleConfig.BoardThickness;
      var areaSizeX = CodexSceneScaleConfig.AreaSizeX;
      var areaSizeZ = CodexSceneScaleConfig.AreaSizeZ;
      var areaHeight = CodexSceneScaleConfig.AreaHeight;
      var footprintHeight = 0.03f * CodexSceneScaleConfig.DefaultEnvironmentScale;

      RepairDimension( areaName + "_XMin_Board", new Vector3( boardThickness, areaHeight, areaSizeZ ), result );
      RepairDimension( areaName + "_XMax_Board", new Vector3( boardThickness, areaHeight, areaSizeZ ), result );
      RepairDimension( areaName + "_ZMin_Board", new Vector3( areaSizeX, areaHeight, boardThickness ), result );
      RepairDimension( areaName + "_ZMax_Board", new Vector3( areaSizeX, areaHeight, boardThickness ), result );
      RepairDimension( areaName + "_Footprint", new Vector3( areaSizeX, footprintHeight, areaSizeZ ), result );
    }

    private static void RepairDimension( string objectName, Vector3 expectedDimensions, SceneScaleRepairResult result )
    {
      var gameObject = FindSceneObject( objectName );
      if ( gameObject == null ) {
        result.failures.Add( objectName + " is missing." );
        return;
      }

      var changed = false;
      if ( !VectorApproximately( Abs( gameObject.transform.lossyScale ), expectedDimensions ) ) {
        Undo.RecordObject( gameObject.transform, "Repair scene environment dimensions" );
        gameObject.transform.localScale = expectedDimensions;
        EditorUtility.SetDirty( gameObject.transform );
        changed = true;
      }

      var box = gameObject.GetComponent<Box>();
      if ( box != null ) {
        var expectedHalfExtents = expectedDimensions * 0.5f;
        if ( !VectorApproximately( box.HalfExtents, expectedHalfExtents ) ) {
          Undo.RecordObject( box, "Repair scene environment AGX half extents" );
          box.HalfExtents = expectedHalfExtents;
          EditorUtility.SetDirty( box );
          changed = true;
        }
      }

      if ( changed )
        result.changed_items.Add( objectName );
    }

    private static void RepairAgxBox( string objectName, Vector3 expectedHalfExtents, SceneScaleRepairResult result )
    {
      var box = FindFirstAgxBox( objectName );
      if ( box == null ) {
        result.failures.Add( objectName + " has no AGX Box." );
        return;
      }

      if ( VectorApproximately( box.HalfExtents, expectedHalfExtents ) )
        return;

      Undo.RecordObject( box, "Repair scene AGX half extents" );
      box.HalfExtents = expectedHalfExtents;
      EditorUtility.SetDirty( box );
      result.changed_items.Add( objectName + ".HalfExtents" );
    }

    private static void RepairTerrain( string objectName, Vector3 expectedSize, float expectedMaximumDepth, SceneScaleRepairResult result )
    {
      var gameObject = FindSceneObject( objectName );
      if ( gameObject == null ) {
        result.failures.Add( objectName + " is missing." );
        return;
      }

      var changed = false;
      var terrain = gameObject.GetComponent<Terrain>();
      var terrainData = terrain != null ? terrain.terrainData : null;
      if ( terrainData != null && !VectorApproximately( terrainData.size, expectedSize ) ) {
        Undo.RecordObject( terrainData, "Repair terrain size" );
        terrainData.size = expectedSize;
        EditorUtility.SetDirty( terrainData );
        changed = true;
      }

      var deformableTerrain = gameObject.GetComponent<DeformableTerrain>();
      if ( deformableTerrain != null && !Approximately( deformableTerrain.MaximumDepth, expectedMaximumDepth ) ) {
        Undo.RecordObject( deformableTerrain, "Repair deformable terrain maximum depth" );
        deformableTerrain.MaximumDepth = expectedMaximumDepth;
        var serializedObject = new SerializedObject( deformableTerrain );
        var maximumDepth = serializedObject.FindProperty( "m_maximumDepth" );
        if ( maximumDepth != null )
          maximumDepth.floatValue = expectedMaximumDepth;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( deformableTerrain );
        changed = true;
      }

      if ( changed )
        result.changed_items.Add( objectName );
    }

    private static void AddDimensionCheck( string objectName, Vector3 expectedDimensions, SceneScaleAuditResult result )
    {
      var gameObject = FindSceneObject( objectName );
      var check = new SceneObjectAuditCheck {
        name = objectName,
        expected_dimensions = FormatVector( expectedDimensions )
      };

      if ( gameObject == null ) {
        check.status = "missing";
        result.object_checks.Add( check );
        result.failures.Add( objectName + " is missing." );
        return;
      }

      check.path = GetHierarchyPath( gameObject );
      check.active_in_hierarchy = gameObject.activeInHierarchy;
      check.position = FormatVector( gameObject.transform.position );
      check.local_position = FormatVector( gameObject.transform.localPosition );
      check.lossy_scale = FormatVector( Abs( gameObject.transform.lossyScale ) );

      var renderer = gameObject.GetComponent<Renderer>();
      if ( renderer != null )
        check.renderer_bounds_size = FormatVector( renderer.bounds.size );

      var dimensionsMatch = VectorApproximately( Abs( gameObject.transform.lossyScale ), expectedDimensions );
      check.dimensions_match_expected = dimensionsMatch;

      var box = gameObject.GetComponent<Box>();
      if ( box != null ) {
        var expectedHalfExtents = expectedDimensions * 0.5f;
        check.agx_box_half_extents = FormatVector( box.HalfExtents );
        check.expected_agx_box_half_extents = FormatVector( expectedHalfExtents );
        check.agx_half_extents_match_expected = VectorApproximately( box.HalfExtents, expectedHalfExtents );
      }
      else {
        check.agx_half_extents_match_expected = true;
      }

      check.status = dimensionsMatch && check.agx_half_extents_match_expected ? "ok" : "mismatch";
      if ( check.status != "ok" )
        result.failures.Add( $"{objectName}: dimensions={check.lossy_scale}, expected={check.expected_dimensions}, halfExtents={check.agx_box_half_extents}, expectedHalfExtents={check.expected_agx_box_half_extents}" );

      result.object_checks.Add( check );
    }

    private static void AddAgxBoxCheck( string objectName, Vector3 expectedHalfExtents, SceneScaleAuditResult result )
    {
      var gameObject = FindSceneObject( objectName );
      var check = new AgxBoxAuditCheck {
        name = objectName,
        expected_half_extents = FormatVector( expectedHalfExtents )
      };

      if ( gameObject == null ) {
        check.status = "missing";
        result.agx_box_checks.Add( check );
        result.failures.Add( objectName + " is missing." );
        return;
      }

      var box = gameObject.GetComponentInChildren<Box>( true );
      check.path = GetHierarchyPath( gameObject );
      check.position = FormatVector( gameObject.transform.position );
      check.local_position = FormatVector( gameObject.transform.localPosition );
      if ( box == null ) {
        check.status = "missing-box";
        result.agx_box_checks.Add( check );
        result.failures.Add( objectName + " has no AGX Box." );
        return;
      }

      check.box_path = GetHierarchyPath( box.gameObject );
      check.half_extents = FormatVector( box.HalfExtents );
      check.full_extents = FormatVector( box.HalfExtents * 2.0f );
      check.matches_expected = VectorApproximately( box.HalfExtents, expectedHalfExtents );
      check.status = check.matches_expected ? "ok" : "mismatch";
      if ( !check.matches_expected )
        result.failures.Add( $"{objectName}: halfExtents={check.half_extents}, expected={check.expected_half_extents}" );

      result.agx_box_checks.Add( check );
    }

    private static void AddTerrainCheck( string objectName, Vector3 expectedSize, float expectedMaximumDepth, SceneScaleAuditResult result )
    {
      var gameObject = FindSceneObject( objectName );
      var check = new TerrainAuditCheck {
        name = objectName,
        expected_size = FormatVector( expectedSize ),
        expected_maximum_depth = FormatFloat( expectedMaximumDepth )
      };

      if ( gameObject == null ) {
        check.status = "missing";
        result.terrain_checks.Add( check );
        result.failures.Add( objectName + " is missing." );
        return;
      }

      check.path = GetHierarchyPath( gameObject );
      check.position = FormatVector( gameObject.transform.position );
      var terrain = gameObject.GetComponent<Terrain>();
      var terrainData = terrain != null ? terrain.terrainData : null;
      if ( terrainData != null ) {
        check.size = FormatVector( terrainData.size );
        check.size_matches_expected = VectorApproximately( terrainData.size, expectedSize );
      }

      var deformableTerrain = gameObject.GetComponent<DeformableTerrain>();
      if ( deformableTerrain != null ) {
        check.maximum_depth = FormatFloat( deformableTerrain.MaximumDepth );
        check.maximum_depth_matches_expected = Approximately( deformableTerrain.MaximumDepth, expectedMaximumDepth );
      }
      else {
        check.maximum_depth_matches_expected = true;
      }

      check.status = check.size_matches_expected && check.maximum_depth_matches_expected ? "ok" : "mismatch";
      if ( check.status != "ok" )
        result.failures.Add( $"{objectName}: size={check.size}, expected={check.expected_size}, maximumDepth={check.maximum_depth}, expectedMaximumDepth={check.expected_maximum_depth}" );

      result.terrain_checks.Add( check );
    }

    private static Box FindFirstAgxBox( string objectName )
    {
      var gameObject = FindSceneObject( objectName );
      return gameObject != null ? gameObject.GetComponentInChildren<Box>( true ) : null;
    }

    private static GameObject FindSceneObject( string name )
    {
      foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == name )
          return gameObject;
      }

      return null;
    }

    private static bool VectorApproximately( Vector3 actual, Vector3 expected )
    {
      return Approximately( actual.x, expected.x ) &&
             Approximately( actual.y, expected.y ) &&
             Approximately( actual.z, expected.z );
    }

    private static bool Approximately( float actual, float expected )
    {
      return Mathf.Abs( actual - expected ) <= Tolerance;
    }

    private static Vector3 Abs( Vector3 value )
    {
      return new Vector3( Mathf.Abs( value.x ), Mathf.Abs( value.y ), Mathf.Abs( value.z ) );
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
      return string.Format( CultureInfo.InvariantCulture,
                            "({0:0.###}, {1:0.###}, {2:0.###})",
                            value.x,
                            value.y,
                            value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( SceneScaleAuditResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static void WriteRepairResult( SceneScaleRepairResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( RepairOutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( RepairOutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    [Serializable]
    private sealed class SceneScaleAuditResult
    {
      public bool success;
      public string message;
      public string source;
      public string scene_path;
      public string expected_environment_scale;
      public string inferred_scale_from_factory_floor;
      public string inferred_scale_from_dig_area;
      public string inferred_scale_from_submerged_box;
      public string expected_room_size;
      public string expected_area_size;
      public string expected_board_thickness;
      public string expected_dig_soil_height;
      public string expected_terrain_size;
      public List<SceneObjectAuditCheck> object_checks = new List<SceneObjectAuditCheck>();
      public List<AgxBoxAuditCheck> agx_box_checks = new List<AgxBoxAuditCheck>();
      public List<TerrainAuditCheck> terrain_checks = new List<TerrainAuditCheck>();
      public List<string> failures = new List<string>();
    }

    [Serializable]
    private sealed class SceneScaleRepairResult
    {
      public bool success;
      public string message;
      public string source;
      public List<string> changed_items = new List<string>();
      public List<string> failures = new List<string>();
    }

    [Serializable]
    private sealed class SceneObjectAuditCheck
    {
      public string name;
      public string status;
      public string path;
      public bool active_in_hierarchy;
      public string position;
      public string local_position;
      public string lossy_scale;
      public string renderer_bounds_size;
      public string expected_dimensions;
      public bool dimensions_match_expected;
      public string agx_box_half_extents;
      public string expected_agx_box_half_extents;
      public bool agx_half_extents_match_expected;
    }

    [Serializable]
    private sealed class AgxBoxAuditCheck
    {
      public string name;
      public string status;
      public string path;
      public string box_path;
      public string position;
      public string local_position;
      public string half_extents;
      public string full_extents;
      public string expected_half_extents;
      public bool matches_expected;
    }

    [Serializable]
    private sealed class TerrainAuditCheck
    {
      public string name;
      public string status;
      public string path;
      public string position;
      public string size;
      public string expected_size;
      public bool size_matches_expected;
      public string maximum_depth;
      public string expected_maximum_depth;
      public bool maximum_depth_matches_expected;
    }
  }
}
#endif
