#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexFactoryLayoutUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexFactoryLayout.request";
    private const string OutputDirectory = "Temp/CodexFactoryLayout";
    private const string LayoutRootName = "CodexFactoryLayout";
    private const string MaterialDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials/CodexFactory";
    private const string FontDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Fonts";
    private const string PingfanSourceFontPath = FontDirectory + "/NotoSansSC-VF.ttf";

    private const float EnvironmentScale = 1.25f;
    private const float RoomSizeX = 7.0f * EnvironmentScale;
    private const float RoomSizeZ = 7.0f * EnvironmentScale;
    private const float RoomHeight = 3.5f * EnvironmentScale;
    private const float BoardThickness = 0.05f * EnvironmentScale;
    private const float AreaSizeX = 2.5f * EnvironmentScale;
    private const float AreaSizeZ = 3.0f * EnvironmentScale;
    private const float AreaHeight = 0.7f * EnvironmentScale;
    private const float ExcavatorCenterlineZ = 5.1f * EnvironmentScale;
    private const float ExcavatorBoomBaseX = 3.2f * EnvironmentScale;

    private static readonly Vector2 DumpMin = new Vector2( 0.7f * EnvironmentScale, 0.2f * EnvironmentScale );
    private static readonly Vector2 DigMin = new Vector2( 4.0f * EnvironmentScale, 3.6f * EnvironmentScale );

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexFactoryLayoutUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Build Measured Factory Layout" )]
    public static void BuildMeasuredFactoryLayoutFromMenu()
    {
      BuildMeasuredFactoryLayout( "menu" );
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

      BuildMeasuredFactoryLayout( "request-file" );
    }

    private static void BuildMeasuredFactoryLayout( string source )
    {
      s_isRunning = true;
      var result = new FactoryLayoutResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );
        RemovePreviousGeneratedObjects();

        EnsureMaterialDirectory();
        var materials = new FactoryMaterials();
        materials.Floor = GetOrCreateMaterial( "CodexFactory_Floor.mat", new Color( 0.45f, 0.48f, 0.46f, 1.0f ), false );
        materials.Wall = GetOrCreateMaterial( "CodexFactory_Wall.mat", new Color( 0.62f, 0.82f, 0.94f, 1.0f ), false );
        materials.WallDarkBlue = GetOrCreateMaterial( "CodexFactory_WallDarkBlue.mat", new Color( 0.05f, 0.17f, 0.42f, 1.0f ), false );
        materials.Ceiling = GetOrCreateMaterial( "CodexFactory_Ceiling.mat", new Color( 0.78f, 0.82f, 0.82f, 1.0f ), false );
        materials.Window = GetOrCreateMaterial( "CodexFactory_Window.mat", new Color( 0.86f, 0.96f, 1.0f, 0.18f ), true );
        materials.DigBoard = GetOrCreateMaterial( "CodexFactory_DigBoard.mat", new Color( 0.88f, 0.48f, 0.21f, 1.0f ), false );
        materials.DumpBoard = GetOrCreateMaterial( "CodexFactory_DumpBoard.mat", new Color( 0.18f, 0.56f, 0.76f, 1.0f ), false );
        materials.DigFill = GetOrCreateMaterial( "CodexFactory_DigFill.mat", new Color( 0.95f, 0.63f, 0.24f, 0.24f ), true );
        materials.DumpFill = GetOrCreateMaterial( "CodexFactory_DumpFill.mat", new Color( 0.18f, 0.72f, 0.62f, 0.20f ), true );
        materials.Marker = GetOrCreateMaterial( "CodexFactory_Marker.mat", new Color( 0.96f, 0.13f, 0.2f, 1.0f ), false );
        materials.Centerline = GetOrCreateMaterial( "CodexFactory_Centerline.mat", new Color( 0.94f, 0.9f, 0.2f, 1.0f ), false );
        materials.SignBase = GetOrCreateMaterial( "CodexFactory_SignWhite.mat", Color.white, false );
        materials.Roof = GetOrCreateMaterial( "CodexFactory_RoofGreyBlack.mat", new Color( 0.12f, 0.12f, 0.12f, 1.0f ), false );

        var layoutRoot = new GameObject( LayoutRootName );
        var sceneRoot = FindSceneObject( "=== Scene ===" );
        if ( sceneRoot != null )
          layoutRoot.transform.SetParent( sceneRoot.transform, false );

        CreateFactoryRoom( layoutRoot.transform, materials );
        var dumpCenter = CreateAreaBoards( layoutRoot.transform, "Dump", DumpMin, materials.DumpBoard, materials.DumpFill );
        var digCenter = CreateAreaBoards( layoutRoot.transform, "Dig", DigMin, materials.DigBoard, materials.DigFill );
        CreateAlignmentGuides( layoutRoot.transform, digCenter, dumpCenter, materials );

        result.dump_min = FormatVector2( DumpMin );
        result.dump_center = FormatVector( dumpCenter );
        result.dig_min = FormatVector2( DigMin );
        result.dig_center = FormatVector( digCenter );

        UpdateExistingDigArea( digCenter, result );
        UpdateExistingDumpSensor( dumpCenter, result );
        HideLegacyObjects( result );
        AlignExcavatorToMeasuredLayout( result );
        CodexEnvironmentPhysicsUtility.ApplyEnvironmentPhysicsFromFactoryBuilder();

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        result.screenshots = new ScreenshotSet();

        result.message = $"Built measured factory layout from {source}. Room=8.75m x 8.75m x 4.375m; " +
                         "dump min=(0.875,0.25), dig min=(5.0,4.5), excavator centerline z=6.375 and boom-base target x=4.0.";
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
                     $"Active scene '{activeScene.path}' has unsaved changes; factory layout builder did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static void RemovePreviousGeneratedObjects()
    {
      DetachExcavatorFromLayoutRoot();
      RemoveObjectIfPresent( LayoutRootName );
      RemoveObjectIfPresent( "DigAreaFillRuntime" );
      RemoveObjectIfPresent( "CodexSceneProbe_Marker" );
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
                                     "AGXUnity_Excavator_before_environment_1p25_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static void DetachExcavatorFromLayoutRoot()
    {
      var layoutRoot = FindSceneObject( LayoutRootName );
      var excavatorRoot = ResolveExcavatorRoot();
      if ( layoutRoot == null || excavatorRoot == null )
        return;

      var current = excavatorRoot.transform.parent;
      while ( current != null ) {
        if ( current == layoutRoot.transform ) {
          excavatorRoot.transform.SetParent( layoutRoot.transform.parent, true );
          EditorUtility.SetDirty( excavatorRoot.transform );
          return;
        }

        current = current.parent;
      }
    }

    private static void CreateFactoryRoom( Transform parent, FactoryMaterials materials )
    {
      CreateCube( parent, "FactoryFloor", new Vector3( RoomSizeX * 0.5f, -0.025f * EnvironmentScale, RoomSizeZ * 0.5f ),
                  new Vector3( RoomSizeX, 0.05f * EnvironmentScale, RoomSizeZ ), materials.Floor );

      const float frameThickness = 0.06f * EnvironmentScale;
      const float wallPanelThickness = 0.035f * EnvironmentScale;
      const float wallPanelBottom = frameThickness;
      const float wallPanelHeight = RoomHeight - wallPanelBottom - frameThickness * 0.5f;
      const float wallPanelY = wallPanelBottom + wallPanelHeight * 0.5f;
      const float wallPanelFaceOffset = frameThickness * 0.5f;
      const float wallPanelSpanX = RoomSizeX - frameThickness * 2.0f;
      const float wallPanelSpanZ = RoomSizeZ - frameThickness * 2.0f;

      CreateCube( parent, "FactoryWall_ZMin", new Vector3( RoomSizeX * 0.5f, wallPanelY, wallPanelFaceOffset ),
                  new Vector3( wallPanelSpanX, wallPanelHeight, wallPanelThickness ), materials.Wall );
      CreateCube( parent, "FactoryWall_ZMax", new Vector3( RoomSizeX * 0.5f, wallPanelY, RoomSizeZ - wallPanelFaceOffset ),
                  new Vector3( wallPanelSpanX, wallPanelHeight, wallPanelThickness ), materials.Wall );
      CreateXMinWallWithText( parent, wallPanelFaceOffset, wallPanelThickness, wallPanelBottom, wallPanelHeight, wallPanelSpanZ, materials );
      CreateXMaxWallWithWindows( parent,
                                 materials,
                                 frameThickness,
                                 wallPanelThickness,
                                 wallPanelBottom,
                                 wallPanelHeight,
                                 wallPanelFaceOffset,
                                 wallPanelSpanZ );

      CreateCube( parent, "FactoryPost_00", new Vector3( 0.0f, RoomHeight * 0.5f, 0.0f ),
                  new Vector3( frameThickness, RoomHeight, frameThickness ), materials.Wall );
      CreateCube( parent, "FactoryPost_X0", new Vector3( RoomSizeX, RoomHeight * 0.5f, 0.0f ),
                  new Vector3( frameThickness, RoomHeight, frameThickness ), materials.Wall );
      CreateCube( parent, "FactoryPost_0Z", new Vector3( 0.0f, RoomHeight * 0.5f, RoomSizeZ ),
                  new Vector3( frameThickness, RoomHeight, frameThickness ), materials.Wall );
      CreateCube( parent, "FactoryPost_XZ", new Vector3( RoomSizeX, RoomHeight * 0.5f, RoomSizeZ ),
                  new Vector3( frameThickness, RoomHeight, frameThickness ), materials.Wall );

      CreateCube( parent, "FactoryTopBeam_ZMin", new Vector3( RoomSizeX * 0.5f, RoomHeight, 0.0f ),
                  new Vector3( RoomSizeX, frameThickness, frameThickness ), materials.Ceiling );
      CreateCube( parent, "FactoryTopBeam_ZMax", new Vector3( RoomSizeX * 0.5f, RoomHeight, RoomSizeZ ),
                  new Vector3( RoomSizeX, frameThickness, frameThickness ), materials.Ceiling );
      CreateCube( parent, "FactoryTopBeam_XMin", new Vector3( 0.0f, RoomHeight, RoomSizeZ * 0.5f ),
                  new Vector3( frameThickness, frameThickness, RoomSizeZ ), materials.Ceiling );
      CreateCube( parent, "FactoryTopBeam_XMax", new Vector3( RoomSizeX, RoomHeight, RoomSizeZ * 0.5f ),
                  new Vector3( frameThickness, frameThickness, RoomSizeZ ), materials.Ceiling );

      CreateFactoryRoof( parent, frameThickness, materials );

      CreateCube( parent, "FactoryFloorEdge_ZMin", new Vector3( RoomSizeX * 0.5f, 0.035f * EnvironmentScale, 0.0f ),
                  new Vector3( RoomSizeX, frameThickness, frameThickness ), materials.Wall );
      CreateCube( parent, "FactoryFloorEdge_ZMax", new Vector3( RoomSizeX * 0.5f, 0.035f * EnvironmentScale, RoomSizeZ ),
                  new Vector3( RoomSizeX, frameThickness, frameThickness ), materials.Wall );
      CreateCube( parent, "FactoryFloorEdge_XMin", new Vector3( 0.0f, 0.035f * EnvironmentScale, RoomSizeZ * 0.5f ),
                  new Vector3( frameThickness, frameThickness, RoomSizeZ ), materials.Wall );
      CreateCube( parent, "FactoryFloorEdge_XMax", new Vector3( RoomSizeX, 0.035f * EnvironmentScale, RoomSizeZ * 0.5f ),
                  new Vector3( frameThickness, frameThickness, RoomSizeZ ), materials.Wall );
    }

    private static void CreateFactoryRoof( Transform parent, float frameThickness, FactoryMaterials materials )
    {
      const float roofExtensionHeight = 1.0f * EnvironmentScale;
      const float roofThickness = 0.10f * EnvironmentScale;

      var roofBaseY = RoomHeight + frameThickness * 0.5f;
      var roofPeakY = roofBaseY + roofExtensionHeight;
      var xMin = 0.0f;
      var xMax = RoomSizeX;
      var zMin = 0.0f;
      var zMax = RoomSizeZ;
      var zCenter = RoomSizeZ * 0.5f;

      CreateCube( parent,
                  "FactoryRoof_XMax_Extension",
                  new Vector3( xMax, roofBaseY + roofExtensionHeight * 0.5f, zCenter ),
                  new Vector3( frameThickness, roofExtensionHeight, RoomSizeZ ),
                  materials.Roof );

      var run = xMax - xMin;
      var roofLength = Mathf.Sqrt( run * run + roofExtensionHeight * roofExtensionHeight );
      var roofAngle = Mathf.Atan2( roofExtensionHeight, run ) * Mathf.Rad2Deg;
      CreateCube( parent,
                  "FactoryRoof_SlopedSlab",
                  new Vector3( ( xMin + xMax ) * 0.5f, ( roofBaseY + roofPeakY ) * 0.5f, zCenter ),
                  new Vector3( roofLength, roofThickness, RoomSizeZ ),
                  materials.Roof,
                  Quaternion.Euler( 0.0f, 0.0f, roofAngle ) );

      CreateRoofSidePanel( parent,
                           "FactoryRoofSide_ZMin",
                           new Vector3( xMin, roofBaseY, zMin ),
                           new Vector3( xMax, roofBaseY, zMin ),
                           new Vector3( xMax, roofPeakY, zMin ),
                           materials.Roof );
      CreateRoofSidePanel( parent,
                           "FactoryRoofSide_ZMax",
                           new Vector3( xMin, roofBaseY, zMax ),
                           new Vector3( xMax, roofBaseY, zMax ),
                           new Vector3( xMax, roofPeakY, zMax ),
                           materials.Roof );
    }

    private static void CreateRoofSidePanel( Transform parent, string name, Vector3 a, Vector3 b, Vector3 c, Material material )
    {
      var panel = new GameObject( name );
      panel.transform.SetParent( parent, false );
      panel.transform.position = Vector3.zero;
      panel.transform.rotation = Quaternion.identity;
      panel.transform.localScale = Vector3.one;

      var mesh = new Mesh { name = name + "_Mesh" };
      mesh.vertices = new[] { a, b, c };
      mesh.triangles = new[] { 0, 1, 2, 2, 1, 0 };
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();

      var meshFilter = panel.AddComponent<MeshFilter>();
      meshFilter.sharedMesh = mesh;
      var renderer = panel.AddComponent<MeshRenderer>();
      renderer.sharedMaterial = material;
    }

    private static void CreateXMinWallWithText( Transform parent,
                                                float wallPanelFaceOffset,
                                                float wallPanelThickness,
                                                float wallPanelBottom,
                                                float wallPanelHeight,
                                                float wallPanelSpanZ,
                                                FactoryMaterials materials )
    {
      var lowerWallHeight = wallPanelHeight * 0.8f;
      var upperWallHeight = wallPanelHeight - lowerWallHeight;
      var lowerWallY = wallPanelBottom + lowerWallHeight * 0.5f;
      var upperWallY = wallPanelBottom + lowerWallHeight + upperWallHeight * 0.5f;

      CreateCube( parent,
                  "FactoryWall_XMin_LowerWhite",
                  new Vector3( wallPanelFaceOffset, lowerWallY, RoomSizeZ * 0.5f ),
                  new Vector3( wallPanelThickness, lowerWallHeight, wallPanelSpanZ ),
                  materials.SignBase );

      CreateCube( parent,
                  "FactoryWall_XMin",
                  new Vector3( wallPanelFaceOffset, upperWallY, RoomSizeZ * 0.5f ),
                  new Vector3( wallPanelThickness, upperWallHeight, wallPanelSpanZ ),
                  materials.Wall );

      CreateXMinWallTextCanvas( parent, wallPanelFaceOffset, wallPanelThickness, lowerWallY, lowerWallHeight, wallPanelSpanZ );
    }

    private static void CreateXMinWallTextCanvas( Transform parent,
                                                  float wallPanelFaceOffset,
                                                  float wallPanelThickness,
                                                  float lowerWallY,
                                                  float lowerWallHeight,
                                                  float wallPanelSpanZ )
    {
      const float faceGap = 0.012f;
      var canvasObject = new GameObject( "FactoryWall_XMin_TextCanvas", typeof( RectTransform ), typeof( Canvas ) );
      const float canvasScale = 0.01f;
      canvasObject.transform.SetParent( parent, false );
      canvasObject.transform.position = new Vector3( wallPanelFaceOffset + wallPanelThickness * 0.5f + faceGap,
                                                     lowerWallY,
                                                     RoomSizeZ * 0.5f );
      canvasObject.transform.rotation = Quaternion.Euler( 0.0f, 90.0f, 0.0f );
      canvasObject.transform.localScale = Vector3.one * canvasScale;

      var canvasRect = canvasObject.GetComponent<RectTransform>();
      canvasRect.sizeDelta = new Vector2( wallPanelSpanZ / canvasScale, lowerWallHeight / canvasScale );

      var canvas = canvasObject.GetComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.sortingOrder = 5;

      var font = GetPingfanFont();
      CreateXMinWallTextElement( canvasObject.transform,
                                 "FactoryWall_XMin_Text_Chinese",
                                 "平　凡　技　术",
                                 new Vector2( 0.0f, lowerWallHeight * 0.16f / canvasScale ),
                                 new Vector2( wallPanelSpanZ * 0.92f / canvasScale, lowerWallHeight * 0.34f / canvasScale ),
                                 118,
                                 font );

      CreateXMinWallTextElement( canvasObject.transform,
                                 "FactoryWall_XMin_Text_Pinyin",
                                 "P i n g f a n   T e c h",
                                 new Vector2( 0.0f, -lowerWallHeight * 0.22f / canvasScale ),
                                 new Vector2( wallPanelSpanZ * 0.92f / canvasScale, lowerWallHeight * 0.22f / canvasScale ),
                                 54,
                                 font );
    }

    private static void CreateXMinWallTextElement( Transform parent,
                                                   string name,
                                                   string text,
                                                   Vector2 anchoredPosition,
                                                   Vector2 size,
                                                   int fontSize,
                                                   Font font )
    {
      var textObject = new GameObject( name, typeof( RectTransform ), typeof( Text ) );
      textObject.transform.SetParent( parent, false );

      var rectTransform = textObject.GetComponent<RectTransform>();
      rectTransform.anchorMin = new Vector2( 0.5f, 0.5f );
      rectTransform.anchorMax = new Vector2( 0.5f, 0.5f );
      rectTransform.pivot = new Vector2( 0.5f, 0.5f );
      rectTransform.anchoredPosition = anchoredPosition;
      rectTransform.sizeDelta = size;

      var textComponent = textObject.GetComponent<Text>();
      textComponent.text = text;
      textComponent.font = font;
      textComponent.fontSize = fontSize;
      textComponent.fontStyle = FontStyle.Bold;
      textComponent.color = Color.black;
      textComponent.alignment = TextAnchor.MiddleCenter;
      textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
      textComponent.verticalOverflow = VerticalWrapMode.Overflow;
      textComponent.raycastTarget = false;
    }

    private static void CreateXMaxWallWithWindows( Transform parent,
                                                   FactoryMaterials materials,
                                                   float frameThickness,
                                                   float wallPanelThickness,
                                                   float wallPanelBottom,
                                                   float wallPanelHeight,
                                                   float wallPanelFaceOffset,
                                                   float wallPanelSpanZ )
    {
      var wallX = RoomSizeX - wallPanelFaceOffset;
      var wallTop = wallPanelBottom + wallPanelHeight;
      var sillHeight = 0.40f * EnvironmentScale;
      var sillTop = wallPanelBottom + sillHeight;
      var windowHeight = wallTop - sillTop;
      var windowY = sillTop + windowHeight * 0.5f;
      var darkBandHeight = 1.20f * EnvironmentScale;
      var darkBandTop = wallPanelBottom + darkBandHeight;
      var upperWallHeight = wallTop - darkBandTop;
      var upperWallY = darkBandTop + upperWallHeight * 0.5f;
      var lowerSegmentHeight = darkBandTop - sillTop;
      var lowerSegmentY = sillTop + lowerSegmentHeight * 0.5f;

      CreateCube( parent,
                  "FactoryWall_XMax_BottomBand",
                  new Vector3( wallX, wallPanelBottom + sillHeight * 0.5f, RoomSizeZ * 0.5f ),
                  new Vector3( wallPanelThickness, sillHeight, wallPanelSpanZ ),
                  materials.WallDarkBlue );

      var leftWindowWidth = 0.98f * EnvironmentScale;
      var groupGapWidth = 1.00f * EnvironmentScale;
      var rightWindowsTotalWidth = 2.30f * EnvironmentScale;
      var rightWindowDividerWidth = frameThickness;
      var rightWindowWidth = rightWindowsTotalWidth * 0.5f;

      var leftWindowStart = 0.0f;
      var leftWindowEnd = leftWindowStart + leftWindowWidth;
      var centerWallStart = leftWindowEnd;
      var centerWallEnd = centerWallStart + groupGapWidth;
      var rightWindowAStart = centerWallEnd;
      var rightWindowAEnd = rightWindowAStart + rightWindowWidth;
      var rightDividerStart = rightWindowAEnd;
      var rightDividerEnd = rightDividerStart + rightWindowDividerWidth;
      var rightWindowBStart = rightDividerEnd;
      var rightWindowBEnd = rightWindowBStart + rightWindowWidth;

      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_WindowGroupGap_Lower", wallX, lowerSegmentY, lowerSegmentHeight,
                                     centerWallStart, centerWallEnd, wallPanelThickness, materials.WallDarkBlue );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_RightWindowDivider_Lower", wallX, lowerSegmentY, lowerSegmentHeight,
                                     rightDividerStart, rightDividerEnd, wallPanelThickness, materials.WallDarkBlue );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_RightRemainder_Lower", wallX, lowerSegmentY, lowerSegmentHeight,
                                     rightWindowBEnd, wallPanelSpanZ, wallPanelThickness, materials.WallDarkBlue );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_WindowGroupGap_Upper", wallX, upperWallY, upperWallHeight,
                                     centerWallStart, centerWallEnd, wallPanelThickness, materials.Wall );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_RightWindowDivider_Upper", wallX, upperWallY, upperWallHeight,
                                     rightDividerStart, rightDividerEnd, wallPanelThickness, materials.Wall );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWall_XMax_RightRemainder_Upper", wallX, upperWallY, upperWallHeight,
                                     rightWindowBEnd, wallPanelSpanZ, wallPanelThickness, materials.Wall );

      var windowThickness = 0.012f * EnvironmentScale;
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWindow_XMax_Left", wallX, windowY, windowHeight,
                                     leftWindowStart, leftWindowEnd, windowThickness, materials.Window );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWindow_XMax_RightA", wallX, windowY, windowHeight,
                                     rightWindowAStart, rightWindowAEnd, windowThickness, materials.Window );
      CreateXMaxWallSegmentFromLeft( parent, "FactoryWindow_XMax_RightB", wallX, windowY, windowHeight,
                                     rightWindowBStart, rightWindowBEnd, windowThickness, materials.Window );
    }

    private static void CreateXMaxWallSegmentFromLeft( Transform parent,
                                                       string name,
                                                       float wallX,
                                                       float y,
                                                       float height,
                                                       float startFromLeft,
                                                       float endFromLeft,
                                                       float thickness,
                                                       Material material )
    {
      var width = endFromLeft - startFromLeft;
      if ( width <= 0.0f )
        return;

      var zMax = RoomSizeZ - 0.06f * EnvironmentScale;
      var centerFromLeft = ( startFromLeft + endFromLeft ) * 0.5f;
      var z = zMax - centerFromLeft;
      CreateCube( parent, name, new Vector3( wallX, y, z ), new Vector3( thickness, height, width ), material );
    }

    private static Vector3 CreateAreaBoards( Transform parent,
                                             string areaName,
                                             Vector2 minCorner,
                                             Material boardMaterial,
                                             Material fillMaterial )
    {
      var root = new GameObject( $"Codex{areaName}AreaBoards" );
      root.transform.SetParent( parent, false );

      var center = new Vector3( minCorner.x + AreaSizeX * 0.5f, 0.0f, minCorner.y + AreaSizeZ * 0.5f );
      var boardY = AreaHeight * 0.5f;

      CreateCube( root.transform,
                  $"{areaName}_XMin_Board",
                  new Vector3( minCorner.x + BoardThickness * 0.5f, boardY, center.z ),
                  new Vector3( BoardThickness, AreaHeight, AreaSizeZ ),
                  boardMaterial );
      CreateCube( root.transform,
                  $"{areaName}_XMax_Board",
                  new Vector3( minCorner.x + AreaSizeX - BoardThickness * 0.5f, boardY, center.z ),
                  new Vector3( BoardThickness, AreaHeight, AreaSizeZ ),
                  boardMaterial );
      CreateCube( root.transform,
                  $"{areaName}_ZMin_Board",
                  new Vector3( center.x, boardY, minCorner.y + BoardThickness * 0.5f ),
                  new Vector3( AreaSizeX, AreaHeight, BoardThickness ),
                  boardMaterial );
      CreateCube( root.transform,
                  $"{areaName}_ZMax_Board",
                  new Vector3( center.x, boardY, minCorner.y + AreaSizeZ - BoardThickness * 0.5f ),
                  new Vector3( AreaSizeX, AreaHeight, BoardThickness ),
                  boardMaterial );
      CreateCube( root.transform,
                  $"{areaName}_Footprint",
                  new Vector3( center.x, 0.015f * EnvironmentScale, center.z ),
                  new Vector3( AreaSizeX, 0.03f * EnvironmentScale, AreaSizeZ ),
                  fillMaterial );

      return center;
    }

    private static void CreateAlignmentGuides( Transform parent, Vector3 digCenter, Vector3 dumpCenter, FactoryMaterials materials )
    {
      CreateCube( parent,
                  "Excavator_Centerline_Z6p375",
                  new Vector3( RoomSizeX * 0.5f, 0.035f * EnvironmentScale, ExcavatorCenterlineZ ),
                  new Vector3( RoomSizeX, 0.03f * EnvironmentScale, 0.025f * EnvironmentScale ),
                  materials.Centerline );

      var boomMarker = GameObject.CreatePrimitive( PrimitiveType.Sphere );
      boomMarker.name = "Excavator_BoomBase_Target_X4p0_Z6p375";
      boomMarker.transform.SetParent( parent, false );
      boomMarker.transform.position = new Vector3( ExcavatorBoomBaseX, 0.16f * EnvironmentScale, ExcavatorCenterlineZ );
      boomMarker.transform.localScale = new Vector3( 0.16f, 0.16f, 0.16f ) * EnvironmentScale;
      ApplyMaterialAndRemoveCollider( boomMarker, materials.Marker );

      CreateCube( parent, "Factory_X_Axis", new Vector3( RoomSizeX * 0.5f, 0.028f * EnvironmentScale, 0.18f * EnvironmentScale ),
                  new Vector3( RoomSizeX - 0.45f * EnvironmentScale, 0.025f * EnvironmentScale, 0.025f * EnvironmentScale ), materials.Marker );
      CreateCube( parent, "Factory_Z_Axis", new Vector3( 0.18f * EnvironmentScale, 0.029f * EnvironmentScale, RoomSizeZ * 0.5f ),
                  new Vector3( 0.025f * EnvironmentScale, 0.025f * EnvironmentScale, RoomSizeZ - 0.45f * EnvironmentScale ), materials.Marker );

      CreateCube( parent, "Dig_To_Dump_ReferenceLine",
                  new Vector3( ( digCenter.x + dumpCenter.x ) * 0.5f, 0.04f * EnvironmentScale, ( digCenter.z + dumpCenter.z ) * 0.5f ),
                  new Vector3( 0.02f * EnvironmentScale, 0.018f * EnvironmentScale, Vector3.Distance( digCenter, dumpCenter ) ),
                  materials.Centerline,
                  Quaternion.LookRotation( ( digCenter - dumpCenter ).normalized, Vector3.up ) );
    }

    private static void UpdateExistingDigArea( Vector3 digCenter, FactoryLayoutResult result )
    {
      var digArea = FindSceneObject( "AGXUnity.RigidBody.DigArea" );
      if ( digArea == null ) {
        result.warnings.Add( "Existing AGXUnity.RigidBody.DigArea was not found; only visual dig boards were created." );
        return;
      }

      digArea.transform.position = new Vector3( digCenter.x, 0.025f * EnvironmentScale, digCenter.z );
      digArea.transform.rotation = Quaternion.identity;
      SetFirstAgxBoxHalfExtents( digArea, new Vector3( AreaSizeX * 0.5f, 0.025f * EnvironmentScale, AreaSizeZ * 0.5f ) );
      EditorUtility.SetDirty( digArea );
      result.updated_dig_area = true;

      MoveContourIfPresent( "DigAreaContour", digCenter, new Vector3( AreaSizeX, 0.03f * EnvironmentScale, AreaSizeZ ) );
      MoveContourIfPresent( "DigAreaContourRuntime", digCenter, new Vector3( AreaSizeX, 0.03f * EnvironmentScale, AreaSizeZ ) );
    }

    private static void UpdateExistingDumpSensor( Vector3 dumpCenter, FactoryLayoutResult result )
    {
      var submergedBox = FindSceneObject( "SubmergedBox" );
      if ( submergedBox == null ) {
        result.warnings.Add( "Existing SubmergedBox dump sensor was not found; only visual dump boards were created." );
        return;
      }

      submergedBox.SetActive( true );
      submergedBox.transform.position = new Vector3( dumpCenter.x, AreaHeight * 0.5f, dumpCenter.z );
      submergedBox.transform.rotation = Quaternion.identity;
      SetFirstAgxBoxHalfExtents( submergedBox, new Vector3( AreaSizeX * 0.5f, AreaHeight * 0.5f, AreaSizeZ * 0.5f ) );
      SetAgxBoxVisualRenderers( submergedBox, false );
      EditorUtility.SetDirty( submergedBox );
      result.updated_dump_sensor = true;
    }

    private static void HideLegacyObjects( FactoryLayoutResult result )
    {
      var bedTruck = FindSceneObject( "BedTruck" );
      if ( bedTruck != null ) {
        bedTruck.SetActive( false );
        EditorUtility.SetDirty( bedTruck );
        result.hidden_legacy_objects.Add( "BedTruck" );
      }

      var terrainMesh = FindSceneObject( "Terrain mesh" );
      if ( terrainMesh != null ) {
        terrainMesh.SetActive( false );
        EditorUtility.SetDirty( terrainMesh );
        result.hidden_legacy_objects.Add( "Terrain mesh" );
      }
    }

    private static void AlignExcavatorToMeasuredLayout( FactoryLayoutResult result )
    {
      var excavatorRoot = ResolveExcavatorRoot();
      if ( excavatorRoot == null ) {
        result.warnings.Add( "Excavator root was not found; target boom-base marker and centerline were created for manual alignment." );
        return;
      }

      var finalBounds = CalculateRendererBounds( excavatorRoot );
      var finalBoomBase = EstimateBoomBasePosition( excavatorRoot, finalBounds );

      result.excavator_root = GetHierarchyPath( excavatorRoot );
      result.excavator_scale_factor_applied = "1";
      result.excavator_boom_base_estimate = FormatVector( finalBoomBase );
      result.excavator_bounds_center = finalBounds.HasValue ? FormatVector( finalBounds.Value.center ) : "unknown";
      result.excavator_bounds_size = finalBounds.HasValue ? FormatVector( finalBounds.Value.size ) : "unknown";

      result.warnings.Add( "Excavator transform, size and internal AGX geometry were preserved; the 1.25x environment was built around the current machine pose." );
    }

    private static void RestoreExcavatorVisualChildren( GameObject excavatorRoot, FactoryLayoutResult result )
    {
      var transforms = excavatorRoot.GetComponentsInChildren<Transform>( true );
      foreach ( var transform in transforms ) {
        if ( transform == null || transform.gameObject.activeSelf )
          continue;

        var objectName = transform.gameObject.name;
        if ( objectName != "Cube" && !objectName.StartsWith( "Cube (", StringComparison.Ordinal ) )
          continue;

        transform.gameObject.SetActive( true );
        EditorUtility.SetDirty( transform.gameObject );
        result.restored_excavator_visuals.Add( objectName );
      }
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var preferredBobcat = FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
                            FindSceneObject( "Excavator_BobcatE85" );
      if ( preferredBobcat != null )
        return preferredBobcat;

      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        if ( component.GetType().FullName != "AGXUnity.Excavator" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() && prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
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

    private static Bounds? CalculateRendererBounds( GameObject root )
    {
      var renderers = root.GetComponentsInChildren<Renderer>( true );
      var hasBounds = false;
      var bounds = new Bounds();
      foreach ( var renderer in renderers ) {
        if ( renderer == null || renderer is ParticleSystemRenderer )
          continue;

        if ( !hasBounds ) {
          bounds = renderer.bounds;
          hasBounds = true;
        }
        else {
          bounds.Encapsulate( renderer.bounds );
        }
      }

      return hasBounds ? bounds : (Bounds?) null;
    }

    private static Vector3 EstimateBoomBasePosition( GameObject excavatorRoot, Bounds? fallbackBounds )
    {
      var candidates = new List<Vector3>();
      var components = excavatorRoot.GetComponentsInChildren<Component>( true );
      foreach ( var component in components ) {
        if ( ComponentHasTagValue( component, "boom_rotaty" ) ||
             ( component != null && component.name.IndexOf( "boom", StringComparison.OrdinalIgnoreCase ) >= 0 ) )
          candidates.Add( component.transform.position );
      }

      if ( candidates.Count > 0 ) {
        candidates.Sort( ( left, right ) => left.x.CompareTo( right.x ) );
        var lowCount = Mathf.Max( 1, Mathf.CeilToInt( candidates.Count * 0.25f ) );
        var sum = Vector3.zero;
        for ( var index = 0; index < lowCount; ++index )
          sum += candidates[ index ];
        return sum / lowCount;
      }

      if ( fallbackBounds.HasValue ) {
        var bounds = fallbackBounds.Value;
        return new Vector3( bounds.min.x + bounds.size.x * 0.38f, bounds.center.y, bounds.center.z );
      }

      return excavatorRoot.transform.position;
    }

    private static bool ComponentHasTagValue( Component component, string expected )
    {
      if ( component == null )
        return false;

      var type = component.GetType();
      const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

      while ( type != null ) {
        foreach ( var field in type.GetFields( flags ) ) {
          if ( field.FieldType != typeof( string ) || field.Name.IndexOf( "Tag", StringComparison.OrdinalIgnoreCase ) < 0 )
            continue;

          try {
            var value = field.GetValue( component ) as string;
            if ( !string.IsNullOrEmpty( value ) && value.IndexOf( expected, StringComparison.OrdinalIgnoreCase ) >= 0 )
              return true;
          }
          catch {
            // Reflection across Unity/AGX objects can throw for native-backed fields; skip that field.
          }
        }

        foreach ( var property in type.GetProperties( flags ) ) {
          if ( property.PropertyType != typeof( string ) || property.Name.IndexOf( "Tag", StringComparison.OrdinalIgnoreCase ) < 0 || property.GetIndexParameters().Length > 0 )
            continue;

          try {
            var value = property.GetValue( component, null ) as string;
            if ( !string.IsNullOrEmpty( value ) && value.IndexOf( expected, StringComparison.OrdinalIgnoreCase ) >= 0 )
              return true;
          }
          catch {
            // Reflection across Unity/AGX objects can throw for native-backed properties; skip that property.
          }
        }

        type = type.BaseType;
      }

      return false;
    }

    private static void CreateOrUpdateFactoryCameras( Transform parent )
    {
      CreateCamera( parent,
                    "CodexFactoryTopCamera",
                    new Vector3( 3.5f, 8.2f, 3.5f ),
                    new Vector3( 3.5f, 0.0f, 3.5f ),
                    4.15f,
                    true );
      CreateCamera( parent,
                    "CodexFactoryOverviewCamera",
                    new Vector3( 8.6f, 4.6f, -2.7f ),
                    new Vector3( 3.35f, 0.9f, 3.75f ),
                    52.0f,
                    false );
      CreateCamera( parent,
                    "CodexFactoryDigDumpCamera",
                    new Vector3( 3.65f, 3.1f, 8.75f ),
                    new Vector3( 3.65f, 0.55f, 3.55f ),
                    45.0f,
                    false );
      CreateCamera( parent,
                    "CodexFactoryExcavatorAlignmentCamera",
                    new Vector3( 1.7f, 2.0f, 2.15f ),
                    new Vector3( 3.75f, 0.62f, 5.1f ),
                    38.0f,
                    false );
    }

    private static void CreateCamera( Transform parent,
                                      string name,
                                      Vector3 position,
                                      Vector3 lookAt,
                                      float sizeOrFov,
                                      bool orthographic )
    {
      var cameraObject = new GameObject( name );
      cameraObject.transform.SetParent( parent, false );
      cameraObject.transform.position = position;
      cameraObject.transform.LookAt( lookAt );

      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.Skybox;
      camera.nearClipPlane = 0.02f;
      camera.farClipPlane = 100.0f;
      camera.orthographic = orthographic;
      if ( orthographic )
        camera.orthographicSize = sizeOrFov;
      else
        camera.fieldOfView = sizeOrFov;
    }

    private static GameObject CreateCube( Transform parent,
                                          string name,
                                          Vector3 position,
                                          Vector3 scale,
                                          Material material,
                                          Quaternion? rotation = null )
    {
      var cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
      cube.name = name;
      cube.transform.SetParent( parent, false );
      cube.transform.position = position;
      cube.transform.rotation = rotation ?? Quaternion.identity;
      cube.transform.localScale = scale;
      ApplyMaterialAndRemoveCollider( cube, material );
      return cube;
    }

    private static void ApplyMaterialAndRemoveCollider( GameObject gameObject, Material material )
    {
      var renderer = gameObject.GetComponent<Renderer>();
      if ( renderer != null ) {
        renderer.sharedMaterial = material;
        if ( material != null && material.name.IndexOf( "Window", StringComparison.OrdinalIgnoreCase ) >= 0 ) {
          renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          renderer.receiveShadows = false;
          renderer.allowOcclusionWhenDynamic = false;
        }
      }

      var collider = gameObject.GetComponent<Collider>();
      if ( collider != null )
        UnityEngine.Object.DestroyImmediate( collider );
    }

    private static void SetFirstAgxBoxHalfExtents( GameObject root, Vector3 halfExtents )
    {
      var components = root.GetComponentsInChildren<Component>( true );
      foreach ( var component in components ) {
        if ( component == null || component.GetType().FullName != "AGXUnity.Collide.Box" )
          continue;

        if ( TrySetHalfExtentsByProperty( component, halfExtents ) ||
             TrySetHalfExtentsBySerialization( component, halfExtents ) ) {
          EditorUtility.SetDirty( component );
          return;
        }
      }
    }

    private static void SetAgxBoxVisualRenderers( GameObject root, bool enabled )
    {
      var renderers = root.GetComponentsInChildren<Renderer>( true );
      foreach ( var renderer in renderers ) {
        if ( renderer == null || renderer.gameObject.name.IndexOf( "Box_Visual", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        renderer.enabled = enabled;
        EditorUtility.SetDirty( renderer );
      }
    }

    private static bool TrySetHalfExtentsByProperty( Component component, Vector3 halfExtents )
    {
      var property = component.GetType().GetProperty( "HalfExtents", BindingFlags.Instance | BindingFlags.Public );
      if ( property == null || !property.CanWrite )
        return false;

      try {
        property.SetValue( component, halfExtents, null );
        return true;
      }
      catch {
        return false;
      }
    }

    private static bool TrySetHalfExtentsBySerialization( Component component, Vector3 halfExtents )
    {
      try {
        var serializedObject = new SerializedObject( component );
        var property = serializedObject.FindProperty( "m_halfExtents" );
        if ( property == null )
          return false;

        property.vector3Value = halfExtents;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        return true;
      }
      catch {
        return false;
      }
    }

    private static void MoveContourIfPresent( string objectName, Vector3 center, Vector3 scale )
    {
      var contour = FindSceneObject( objectName );
      if ( contour == null )
        return;

      contour.transform.position = new Vector3( center.x, 0.035f, center.z );
      contour.transform.rotation = Quaternion.identity;
      contour.transform.localScale = scale;
      EditorUtility.SetDirty( contour );
    }

    private static string CaptureNamedCamera( string cameraName, string fileName )
    {
      var cameraObject = FindSceneObject( cameraName );
      var camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
      return camera == null ? null : CaptureCamera( camera, fileName );
    }

    private static string CaptureMainCamera()
    {
      var camera = Camera.main;
      if ( camera == null ) {
        var cameras = Resources.FindObjectsOfTypeAll<Camera>();
        foreach ( var candidate in cameras ) {
          if ( candidate != null && candidate.gameObject.scene.IsValid() ) {
            camera = candidate;
            break;
          }
        }
      }

      return camera == null ? null : CaptureCamera( camera, "main_camera.png" );
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

    private static void EnsureMaterialDirectory()
    {
      if ( AssetDatabase.IsValidFolder( MaterialDirectory ) )
        return;

      var parent = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials";
      if ( !AssetDatabase.IsValidFolder( parent ) )
        AssetDatabase.CreateFolder( "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets", "Materials" );

      AssetDatabase.CreateFolder( parent, "CodexFactory" );
    }

    private static Material GetOrCreateMaterial( string fileName, Color color, bool transparent )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
      var isWindowMaterial = fileName.IndexOf( "Window", StringComparison.OrdinalIgnoreCase ) >= 0;
      var material = AssetDatabase.LoadAssetAtPath<Material>( assetPath );
      if ( material == null ) {
        var shader = Shader.Find( "Universal Render Pipeline/Lit" ) ??
                     Shader.Find( "Universal Render Pipeline/Unlit" ) ??
                     Shader.Find( "Standard" );
        material = new Material( shader ) { name = Path.GetFileNameWithoutExtension( fileName ) };
        AssetDatabase.CreateAsset( material, assetPath );
      }

      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", color );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", color );

      if ( transparent ) {
        if ( material.HasProperty( "_Surface" ) )
          material.SetFloat( "_Surface", 1.0f );
        if ( material.HasProperty( "_SrcBlend" ) )
          material.SetFloat( "_SrcBlend", isWindowMaterial ? (float) UnityEngine.Rendering.BlendMode.One : (float) UnityEngine.Rendering.BlendMode.SrcAlpha );
        if ( material.HasProperty( "_DstBlend" ) )
          material.SetFloat( "_DstBlend", (float) UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha );
        if ( material.HasProperty( "_ZWrite" ) )
          material.SetFloat( "_ZWrite", 0.0f );
        if ( material.HasProperty( "_Mode" ) )
          material.SetFloat( "_Mode", isWindowMaterial ? 3.0f : 2.0f );
        if ( isWindowMaterial && material.HasProperty( "_Glossiness" ) )
          material.SetFloat( "_Glossiness", 0.96f );
        if ( isWindowMaterial && material.HasProperty( "_Metallic" ) )
          material.SetFloat( "_Metallic", 0.0f );
        material.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
        if ( isWindowMaterial ) {
          material.EnableKeyword( "_ALPHAPREMULTIPLY_ON" );
          material.DisableKeyword( "_ALPHABLEND_ON" );
        }
        else {
          material.EnableKeyword( "_ALPHABLEND_ON" );
          material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
        }
        material.renderQueue = (int) UnityEngine.Rendering.RenderQueue.Transparent;
      }
      else {
        if ( material.HasProperty( "_Surface" ) )
          material.SetFloat( "_Surface", 0.0f );
        if ( material.HasProperty( "_ZWrite" ) )
          material.SetFloat( "_ZWrite", 1.0f );
        if ( material.HasProperty( "_Mode" ) )
          material.SetFloat( "_Mode", 0.0f );
        material.DisableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
        material.DisableKeyword( "_ALPHABLEND_ON" );
        material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
        material.renderQueue = -1;
      }

      EditorUtility.SetDirty( material );
      return material;
    }

    private static Font GetPingfanFont()
    {
      var sourceFont = AssetDatabase.LoadAssetAtPath<Font>( PingfanSourceFontPath );
      if ( sourceFont == null ) {
        AssetDatabase.ImportAsset( PingfanSourceFontPath, ImportAssetOptions.ForceUpdate );
        sourceFont = AssetDatabase.LoadAssetAtPath<Font>( PingfanSourceFontPath );
      }

      return sourceFont != null ? sourceFont : Resources.GetBuiltinResource<Font>( "Arial.ttf" );
    }

    private static Material GetOrCreateTexturedMaterial( string fileName, string texturePath )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
      var material = AssetDatabase.LoadAssetAtPath<Material>( assetPath );
      if ( material == null ) {
        var shader = Shader.Find( "Universal Render Pipeline/Unlit" ) ??
                     Shader.Find( "Unlit/Texture" ) ??
                     Shader.Find( "Standard" );
        material = new Material( shader ) { name = Path.GetFileNameWithoutExtension( fileName ) };
        AssetDatabase.CreateAsset( material, assetPath );
      }

      var texture = AssetDatabase.LoadAssetAtPath<Texture2D>( texturePath );
      if ( texture != null ) {
        if ( material.HasProperty( "_BaseMap" ) )
          material.SetTexture( "_BaseMap", texture );
        if ( material.HasProperty( "_MainTex" ) )
          material.SetTexture( "_MainTex", texture );
      }

      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", Color.white );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", Color.white );
      if ( material.HasProperty( "_Surface" ) )
        material.SetFloat( "_Surface", 0.0f );
      if ( material.HasProperty( "_ZWrite" ) )
        material.SetFloat( "_ZWrite", 1.0f );
      if ( material.HasProperty( "_Mode" ) )
        material.SetFloat( "_Mode", 0.0f );
      if ( material.HasProperty( "_Cull" ) )
        material.SetFloat( "_Cull", (float) UnityEngine.Rendering.CullMode.Off );

      material.DisableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
      material.DisableKeyword( "_ALPHABLEND_ON" );
      material.DisableKeyword( "_ALPHAPREMULTIPLY_ON" );
      material.renderQueue = -1;

      EditorUtility.SetDirty( material );
      return material;
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

    private static string FormatVector( Vector3 vector )
    {
      return $"({vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###})";
    }

    private static string FormatVector2( Vector2 vector )
    {
      return $"({vector.x:0.###}, {vector.y:0.###})";
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

    private static void WriteResult( bool success, string message, FactoryLayoutResult result )
    {
      EnsureOutputDirectoryExists();
      if ( result == null )
        result = new FactoryLayoutResult();

      result.success = success;
      result.message = message;

      var json = JsonUtility.ToJson( result, true );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ), json );
      Debug.Log( $"Codex factory layout: {message}" );
    }

    private sealed class FactoryMaterials
    {
      public Material Floor;
      public Material Wall;
      public Material WallDarkBlue;
      public Material Ceiling;
      public Material Window;
      public Material DigBoard;
      public Material DumpBoard;
      public Material DigFill;
      public Material DumpFill;
      public Material Marker;
      public Material Centerline;
      public Material SignBase;
      public Material Roof;
    }

    [Serializable]
    private sealed class FactoryLayoutResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public bool updated_dig_area;
      public bool updated_dump_sensor;
      public string dump_min;
      public string dump_center;
      public string dig_min;
      public string dig_center;
      public string excavator_root;
      public string excavator_scale_factor_applied;
      public string excavator_boom_base_estimate;
      public string excavator_bounds_center;
      public string excavator_bounds_size;
      public List<string> hidden_legacy_objects = new List<string>();
      public List<string> restored_excavator_visuals = new List<string>();
      public List<string> warnings = new List<string>();
      public ScreenshotSet screenshots;
    }

    [Serializable]
    private sealed class ScreenshotSet
    {
      public string top_down;
      public string overview;
      public string dig_dump;
      public string excavator_alignment;
      public string main_camera;
    }
  }
}
#endif
