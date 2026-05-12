#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity.Collide;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexPrimitiveRenderDataVisualScaleFixUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexPrimitiveRenderDataVisualScaleFix.request";
    private const string OutputDirectory = "Temp/CodexPrimitiveRenderDataVisualScaleFix";
    private const float VisualScale = 0.8f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexPrimitiveRenderDataVisualScaleFixUtility()
    {
      EditorApplication.update += PollForRequest;
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

      RunFix();
    }

    private static void RunFix()
    {
      s_isRunning = true;
      var result = new FixResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene could not be opened.", result );
          return;
        }

        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          WriteResult( false, "Excavator root was not found.", result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        foreach ( var shape in root.GetComponentsInChildren<Shape>( true ) ) {
          if ( shape == null || shape is AGXUnity.Collide.Mesh )
            continue;

          foreach ( var visual in shape.GetComponentsInChildren<AGXUnity.Rendering.ShapeVisualRenderData>( true ) ) {
            if ( visual == null || visual.Shape != shape )
              continue;

            if ( visual.transform.localScale.x < 0.99f ||
                 visual.transform.localScale.y < 0.99f ||
                 visual.transform.localScale.z < 0.99f )
              continue;

            Undo.RecordObject( visual.transform, "Scale primitive render-data visual" );
            visual.transform.localScale *= VisualScale;
            EditorUtility.SetDirty( visual.transform );
            result.scaled_visuals++;
            result.paths.Add( GetHierarchyPath( visual.gameObject ) );
          }
        }

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.message = $"Scaled {result.scaled_visuals} primitive render-data visuals by {VisualScale.ToString( "0.###", CultureInfo.InvariantCulture )}.";
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

      if ( activeScene.IsValid() && activeScene.isDirty )
        return default;

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static GameObject ResolveExcavatorRoot()
    {
      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        if ( component.GetType().FullName != "AGXUnity.Excavator" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() )
          return prefabRoot;
      }

      foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == "Excavator_BobcatE85" )
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

    private static void WriteResult( bool success, string message, FixResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new FixResult();

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
    private sealed class FixResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public int scaled_visuals;
      public List<string> paths = new List<string>();
    }
  }
}
#endif
