#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexCameraCleanupUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexCameraCleanup.request";
    private const string OutputDirectory = "Temp/CodexCameraCleanup";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexCameraCleanupUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Cleanup Observer Cameras" )]
    public static void CleanupObserverCamerasFromMenu()
    {
      CleanupObserverCameras( "menu" );
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

      CleanupObserverCameras( "request-file" );
    }

    private static void CleanupObserverCameras( string source )
    {
      s_isRunning = true;
      var result = new CameraCleanupResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        RemoveCodexObserverCameras( result );
        PlaceMainCamera( result );

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );
        AssetDatabase.SaveAssets();

        result.message = $"Cleaned observer cameras from {source}; Main Camera is unparented and not following any object.";
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
                     $"Active scene '{activeScene.path}' has unsaved changes; camera cleanup did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static void RemoveCodexObserverCameras( CameraCleanupResult result )
    {
      var cameras = Resources.FindObjectsOfTypeAll<Camera>();
      for ( var index = cameras.Length - 1; index >= 0; --index ) {
        var camera = cameras[index];
        if ( camera == null || camera.gameObject == null || !camera.gameObject.scene.IsValid() )
          continue;

        var objectName = camera.gameObject.name;
        if ( !objectName.StartsWith( "Codex", StringComparison.Ordinal ) ||
             objectName.IndexOf( "Camera", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        result.removed_cameras.Add( GetHierarchyPath( camera.gameObject ) );
        UnityEngine.Object.DestroyImmediate( camera.gameObject );
      }
    }

    private static void PlaceMainCamera( CameraCleanupResult result )
    {
      var mainCameraObject = GameObject.FindWithTag( "MainCamera" ) ?? FindSceneObject( "Main Camera" );
      if ( mainCameraObject == null ) {
        mainCameraObject = new GameObject( "Main Camera" );
        mainCameraObject.tag = "MainCamera";
        mainCameraObject.AddComponent<Camera>();
        mainCameraObject.AddComponent<AudioListener>();
        result.created_main_camera = true;
      }

      var anchor = FindSceneObject( "Main Camera Anchor" );
      if ( anchor != null )
        ClearFollowObjectReferences( anchor, result );

      mainCameraObject.transform.SetParent( null, true );
      mainCameraObject.transform.position = new Vector3( 3.5f, 2.4f, -0.85f );
      mainCameraObject.transform.rotation = Quaternion.LookRotation( new Vector3( 0.08f, -0.35f, 1.0f ).normalized, Vector3.up );
      mainCameraObject.transform.localScale = Vector3.one;
      mainCameraObject.SetActive( true );
      mainCameraObject.tag = "MainCamera";

      var camera = mainCameraObject.GetComponent<Camera>();
      if ( camera == null )
        camera = mainCameraObject.AddComponent<Camera>();
      camera.enabled = true;
      camera.clearFlags = CameraClearFlags.Skybox;
      camera.fieldOfView = 60.0f;
      camera.nearClipPlane = 0.05f;
      camera.farClipPlane = 100.0f;
      camera.targetTexture = null;

      var audioListener = mainCameraObject.GetComponent<AudioListener>();
      if ( audioListener == null )
        audioListener = mainCameraObject.AddComponent<AudioListener>();
      audioListener.enabled = true;

      ClearFollowObjectReferences( mainCameraObject, result );

      if ( anchor != null && anchor.transform.childCount == 0 ) {
        result.removed_camera_anchor = GetHierarchyPath( anchor );
        UnityEngine.Object.DestroyImmediate( anchor );
      }

      result.main_camera_path = GetHierarchyPath( mainCameraObject );
      result.main_camera_position = FormatVector( mainCameraObject.transform.position );
      result.main_camera_euler = FormatVector( mainCameraObject.transform.eulerAngles );
    }

    private static void ClearFollowObjectReferences( GameObject gameObject, CameraCleanupResult result )
    {
      var behaviours = gameObject.GetComponents<MonoBehaviour>();
      foreach ( var behaviour in behaviours ) {
        if ( behaviour == null )
          continue;

        var serializedObject = new SerializedObject( behaviour );
        var property = serializedObject.GetIterator();
        var changed = false;
        if ( property.NextVisible( true ) ) {
          do {
            if ( property.propertyType != SerializedPropertyType.ObjectReference )
              continue;

            if ( property.name.IndexOf( "follow", StringComparison.OrdinalIgnoreCase ) < 0 &&
                 property.name.IndexOf( "target", StringComparison.OrdinalIgnoreCase ) < 0 )
              continue;

            if ( property.objectReferenceValue == null )
              continue;

            property.objectReferenceValue = null;
            changed = true;
          }
          while ( property.NextVisible( false ) );
        }

        if ( changed ) {
          serializedObject.ApplyModifiedPropertiesWithoutUndo();
          result.cleared_follow_components.Add( $"{GetHierarchyPath( gameObject )}/{behaviour.GetType().Name}" );
        }

        if ( behaviour != null && behaviour.GetType() != typeof( Camera ) )
          behaviour.enabled = false;

        EditorUtility.SetDirty( behaviour );
      }
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

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static void WriteResult( bool success, string message, CameraCleanupResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new CameraCleanupResult();

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
    private sealed class CameraCleanupResult
    {
      public bool success;
      public string message;
      public bool created_main_camera;
      public string main_camera_path;
      public string main_camera_position;
      public string main_camera_euler;
      public string removed_camera_anchor;
      public List<string> removed_cameras = new List<string>();
      public List<string> cleared_follow_components = new List<string>();
    }
  }
}
#endif
