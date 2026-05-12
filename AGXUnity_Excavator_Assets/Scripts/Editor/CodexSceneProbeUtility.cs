#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexSceneProbeUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string MarkerName = "CodexSceneProbe_Marker";
    private const string RequestPath = "Temp/CodexSceneProbe.request";
    private const string RefreshRequestPath = "Temp/CodexAssetRefresh.request";
    private const string OutputDirectory = "Temp/CodexSceneProbe";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexSceneProbeUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Run Scene Probe" )]
    public static void RunSceneProbeFromMenu()
    {
      RunSceneProbe( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var refreshRequestPath = GetProjectRelativeAbsolutePath( RefreshRequestPath );
      if ( File.Exists( refreshRequestPath ) ) {
        File.Delete( refreshRequestPath );
        AssetDatabase.Refresh();
        return;
      }

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}", null, null );
        return;
      }

      RunSceneProbe( "request-file" );
    }

    private static void RunSceneProbe( string source )
    {
      s_isRunning = true;
      string probeScreenshot = null;
      string mainCameraScreenshot = null;

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", null, null );
          return;
        }

        var marker = CreateOrUpdateMarker();
        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );

        EnsureOutputDirectoryExists();
        probeScreenshot = CaptureProbeCamera( marker.transform.position );
        mainCameraScreenshot = CaptureMainCamera();

        WriteResult( true,
                     $"Scene probe completed from {source}. Marker '{MarkerName}' is present in the main scene.",
                     probeScreenshot,
                     mainCameraScreenshot );
      }
      catch ( Exception exception ) {
        WriteResult( false, exception.ToString(), probeScreenshot, mainCameraScreenshot );
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
                     $"Active scene '{activeScene.path}' has unsaved changes; probe did not switch scenes.",
                     null,
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static GameObject CreateOrUpdateMarker()
    {
      var marker = FindSceneObject( MarkerName );
      if ( marker == null ) {
        marker = GameObject.CreatePrimitive( PrimitiveType.Cube );
        marker.name = MarkerName;
        var collider = marker.GetComponent<Collider>();
        if ( collider != null )
          UnityEngine.Object.DestroyImmediate( collider );
      }

      var reference = FindSceneObject( "DigAreaContour" ) ?? FindSceneObject( "SubmergedBox" );
      var basePosition = reference != null ? reference.transform.position : Vector3.zero;
      marker.transform.position = basePosition + new Vector3( 0.0f, 1.25f, 0.0f );
      marker.transform.rotation = Quaternion.identity;
      marker.transform.localScale = new Vector3( 0.6f, 0.6f, 0.6f );

      var renderer = marker.GetComponent<MeshRenderer>();
      if ( renderer != null )
        renderer.sharedMaterial = CreateProbeMaterial();

      marker.SetActive( true );
      EditorUtility.SetDirty( marker );
      return marker;
    }

    private static Material CreateProbeMaterial()
    {
      var shader = Shader.Find( "Universal Render Pipeline/Unlit" ) ??
                   Shader.Find( "Unlit/Color" ) ??
                   Shader.Find( "Standard" );
      var material = new Material( shader ) {
        name = "CodexSceneProbe_Magenta"
      };

      var color = new Color( 1.0f, 0.0f, 0.85f, 1.0f );
      if ( material.HasProperty( "_BaseColor" ) )
        material.SetColor( "_BaseColor", color );
      if ( material.HasProperty( "_Color" ) )
        material.SetColor( "_Color", color );

      return material;
    }

    private static string CaptureProbeCamera( Vector3 targetPosition )
    {
      var cameraObject = new GameObject( "CodexSceneProbe_Camera" );
      cameraObject.hideFlags = HideFlags.HideAndDontSave;
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.Skybox;
      camera.fieldOfView = 45.0f;
      camera.nearClipPlane = 0.05f;
      camera.farClipPlane = 500.0f;
      camera.transform.position = targetPosition + new Vector3( 7.5f, 5.0f, -7.5f );
      camera.transform.LookAt( targetPosition );

      try {
        return CaptureCamera( camera, "probe_camera.png" );
      }
      finally {
        UnityEngine.Object.DestroyImmediate( cameraObject );
      }
    }

    private static string CaptureMainCamera()
    {
      var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
      return camera == null ? null : CaptureCamera( camera, "main_camera.png" );
    }

    private static string CaptureCamera( Camera camera, string fileName )
    {
      const int width = 1280;
      const int height = 720;

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

    private static GameObject FindSceneObject( string objectName )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      for ( var index = 0; index < objects.Length; ++index ) {
        var candidate = objects[ index ];
        if ( candidate == null || candidate.name != objectName || !candidate.scene.IsValid() )
          continue;

        return candidate;
      }

      return null;
    }

    private static void EnsureOutputDirectoryExists()
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
    }

    private static void WriteResult( bool success, string message, string probeScreenshot, string mainCameraScreenshot )
    {
      EnsureOutputDirectoryExists();
      var resultPath = Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" );
      var json = JsonUtility.ToJson( new ProbeResult {
        success = success,
        message = message,
        probe_screenshot = probeScreenshot,
        main_camera_screenshot = mainCameraScreenshot,
        timestamp_utc = DateTime.UtcNow.ToString( "O" )
      }, true );
      File.WriteAllText( resultPath, json );
      Debug.Log( $"Codex scene probe: {message}" );
    }

    private static string GetProjectRelativeAbsolutePath( string relativePath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName ?? Directory.GetCurrentDirectory();
      return Path.GetFullPath( Path.Combine( projectRoot, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );
    }

    [Serializable]
    private class ProbeResult
    {
      public bool success;
      public string message;
      public string probe_screenshot;
      public string main_camera_screenshot;
      public string timestamp_utc;
    }
  }
}
#endif
