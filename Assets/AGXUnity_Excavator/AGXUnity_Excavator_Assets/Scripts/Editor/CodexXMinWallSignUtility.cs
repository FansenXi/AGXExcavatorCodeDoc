#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexXMinWallSignUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexXMinWallSplitText.request";
    private const string OutputDirectory = "Temp/CodexXMinWallSplitText";
    private const string MaterialDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Materials/CodexFactory";
    private const string FontDirectory = "Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Fonts";
    private const string PingfanSourceFontPath = FontDirectory + "/NotoSansSC-VF.ttf";
    private const string WallName = "FactoryWall_XMin";
    private const string LowerWallName = "FactoryWall_XMin_LowerWhite";
    private const string CanvasName = "FactoryWall_XMin_TextCanvas";
    private const string ChineseTextName = "FactoryWall_XMin_Text_Chinese";
    private const string PinyinTextName = "FactoryWall_XMin_Text_Pinyin";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexXMinWallSignUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Apply XMin Split Wall Text" )]
    public static void ApplyXMinSplitWallTextFromMenu()
    {
      ApplyXMinSplitWallText( "menu" );
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

      ApplyXMinSplitWallText( "request-file" );
    }

    private static void ApplyXMinSplitWallText( string source )
    {
      s_isRunning = true;
      var result = new XMinWallTextResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );
        EnsureMaterialDirectory();

        var wall = FindSceneObject( WallName );
        if ( wall == null ) {
          WriteResult( false, $"Could not find {WallName} in the scene.", result );
          return;
        }

        var wallMaterial = GetOrCreateMaterial( "CodexFactory_Wall.mat", new Color( 0.62f, 0.82f, 0.94f, 1.0f ), false );
        var whiteMaterial = GetOrCreateMaterial( "CodexFactory_SignWhite.mat", Color.white, false );
        var font = GetPingfanFont();

        var parent = wall.transform.parent;
        var originalPosition = wall.transform.localPosition;
        var originalScale = wall.transform.localScale;
        var totalHeight = Mathf.Abs( originalScale.y );
        var wallBottom = originalPosition.y - totalHeight * 0.5f;
        var lowerHeight = totalHeight * 0.8f;
        var upperHeight = totalHeight - lowerHeight;
        var lowerY = wallBottom + lowerHeight * 0.5f;
        var upperY = wallBottom + lowerHeight + upperHeight * 0.5f;

        RemoveObjectsNamed( "FactoryWall_XMin_PingfanTechSign_Base" );
        RemoveObjectsNamed( "FactoryWall_XMin_PingfanTechSign_Text" );
        RemoveObjectsNamed( LowerWallName );
        RemoveObjectsNamed( CanvasName );
        RemoveObjectsNamed( ChineseTextName );
        RemoveObjectsNamed( PinyinTextName );

        wall.transform.localPosition = new Vector3( originalPosition.x, upperY, originalPosition.z );
        wall.transform.localScale = new Vector3( originalScale.x, upperHeight, originalScale.z );
        ApplyMaterialAndRemoveCollider( wall, wallMaterial );
        EditorUtility.SetDirty( wall );

        var lowerWall = CreateCube( parent,
                                    LowerWallName,
                                    new Vector3( originalPosition.x, lowerY, originalPosition.z ),
                                    new Vector3( originalScale.x, lowerHeight, originalScale.z ),
                                    whiteMaterial );

        var canvas = CreateTextCanvas( parent,
                                       new Vector3( originalPosition.x + Mathf.Abs( originalScale.x ) * 0.5f + 0.012f,
                                                    lowerY,
                                                    originalPosition.z ),
                                       Mathf.Abs( originalScale.z ),
                                       lowerHeight,
                                       font );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.upper_wall = GetHierarchyPath( wall );
        result.lower_wall = GetHierarchyPath( lowerWall );
        result.text_canvas = GetHierarchyPath( canvas );
        result.lower_wall_local_position = FormatVector( lowerWall.transform.localPosition );
        result.lower_wall_local_scale = FormatVector( lowerWall.transform.localScale );
        result.upper_wall_local_position = FormatVector( wall.transform.localPosition );
        result.upper_wall_local_scale = FormatVector( wall.transform.localScale );
        result.message = $"Applied XMin split wall text from {source}. Lower white wall is 80% of the original wall height.";
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

    private static GameObject CreateTextCanvas( Transform parent,
                                                Vector3 localPosition,
                                                float wallWidth,
                                                float lowerHeight,
                                                Font font )
    {
      const float canvasScale = 0.01f;
      var canvasObject = new GameObject( CanvasName, typeof( RectTransform ), typeof( Canvas ) );
      canvasObject.transform.SetParent( parent, false );
      canvasObject.transform.localPosition = localPosition;
      canvasObject.transform.localRotation = Quaternion.Euler( 0.0f, 90.0f, 0.0f );
      canvasObject.transform.localScale = Vector3.one * canvasScale;

      var canvasRect = canvasObject.GetComponent<RectTransform>();
      canvasRect.sizeDelta = new Vector2( wallWidth / canvasScale, lowerHeight / canvasScale );

      var canvas = canvasObject.GetComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvas.sortingOrder = 5;

      CreateTextElement( canvasObject.transform,
                         ChineseTextName,
                         "平　凡　技　术",
                         new Vector2( 0.0f, lowerHeight * 0.16f / canvasScale ),
                         new Vector2( wallWidth * 0.92f / canvasScale, lowerHeight * 0.34f / canvasScale ),
                         118,
                         font );

      CreateTextElement( canvasObject.transform,
                         PinyinTextName,
                         "P i n g f a n   T e c h",
                         new Vector2( 0.0f, -lowerHeight * 0.22f / canvasScale ),
                         new Vector2( wallWidth * 0.92f / canvasScale, lowerHeight * 0.22f / canvasScale ),
                         54,
                         font );

      EditorUtility.SetDirty( canvasObject );
      return canvasObject;
    }

    private static void CreateTextElement( Transform parent,
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

      EditorUtility.SetDirty( textObject );
    }

    private static GameObject CreateCube( Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material )
    {
      var cube = GameObject.CreatePrimitive( PrimitiveType.Cube );
      cube.name = name;
      cube.transform.SetParent( parent, false );
      cube.transform.localPosition = localPosition;
      cube.transform.localRotation = Quaternion.identity;
      cube.transform.localScale = localScale;
      ApplyMaterialAndRemoveCollider( cube, material );
      return cube;
    }

    private static void ApplyMaterialAndRemoveCollider( GameObject gameObject, Material material )
    {
      var renderer = gameObject.GetComponent<Renderer>();
      if ( renderer != null )
        renderer.sharedMaterial = material;

      var collider = gameObject.GetComponent<Collider>();
      if ( collider != null )
        UnityEngine.Object.DestroyImmediate( collider );
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

    private static Material GetOrCreateMaterial( string fileName, Color color, bool transparent )
    {
      var assetPath = $"{MaterialDirectory}/{fileName}";
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
        if ( material.HasProperty( "_ZWrite" ) )
          material.SetFloat( "_ZWrite", 0.0f );
        material.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
        material.EnableKeyword( "_ALPHABLEND_ON" );
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

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; XMin split wall text utility did not switch scenes.",
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
                                     "AGXUnity_Excavator_before_xmin_split_text_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
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

    private static GameObject FindSceneObject( string objectName )
    {
      var transforms = Resources.FindObjectsOfTypeAll<Transform>();
      foreach ( var transform in transforms ) {
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name == objectName )
          return transform.gameObject;
      }

      return null;
    }

    private static void RemoveObjectsNamed( string objectName )
    {
      var transforms = Resources.FindObjectsOfTypeAll<Transform>();
      for ( var index = transforms.Length - 1; index >= 0; --index ) {
        var transform = transforms[ index ];
        if ( transform == null || !transform.gameObject.scene.IsValid() )
          continue;
        if ( transform.name == objectName )
          UnityEngine.Object.DestroyImmediate( transform.gameObject );
      }
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

    private static void WriteResult( bool success, string message, XMinWallTextResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new XMinWallTextResult();

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
    private sealed class XMinWallTextResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public string upper_wall;
      public string lower_wall;
      public string text_canvas;
      public string upper_wall_local_position;
      public string upper_wall_local_scale;
      public string lower_wall_local_position;
      public string lower_wall_local_scale;
    }
  }
}
#endif
