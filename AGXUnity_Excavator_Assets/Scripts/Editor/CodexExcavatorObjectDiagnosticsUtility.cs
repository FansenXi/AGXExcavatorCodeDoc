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
  public static class CodexExcavatorObjectDiagnosticsUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorObjectDiagnostics.request";
    private const string OutputDirectory = "Temp/CodexExcavatorObjectDiagnostics";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorObjectDiagnosticsUtility()
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

      RunDiagnostics();
    }

    private static void RunDiagnostics()
    {
      s_isRunning = true;
      var result = new DiagnosticsResult();

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
        var interesting = new[] {
          "UnderCarriageBody/Solid1_Cylinder (2)",
          "UnderCarriageBody",
          "Bucket/Solid1_Trimesh (3)",
          "ChassieBody/Solid_Trimesh (10)",
          "Arm/Boom_Trimesh"
        };

        foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) ) {
          var path = GetHierarchyPath( transform.gameObject );
          foreach ( var token in interesting ) {
            if ( path.IndexOf( token, StringComparison.OrdinalIgnoreCase ) < 0 )
              continue;

            result.objects.Add( DescribeObject( transform.gameObject ) );
            break;
          }
        }

        result.message = $"Collected {result.objects.Count} diagnostic objects.";
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

    private static DiagnosticObject DescribeObject( GameObject gameObject )
    {
      var item = new DiagnosticObject {
        path = GetHierarchyPath( gameObject ),
        local_position = FormatVector( gameObject.transform.localPosition ),
        local_scale = FormatVector( gameObject.transform.localScale ),
        lossy_scale = FormatVector( gameObject.transform.lossyScale ),
        world_position = FormatVector( gameObject.transform.position ),
        world_euler = FormatVector( gameObject.transform.eulerAngles )
      };

      var renderer = gameObject.GetComponent<Renderer>();
      if ( renderer != null ) {
        item.renderer_bounds_size = FormatVector( renderer.bounds.size );
        item.renderer_bounds_center = FormatVector( renderer.bounds.center );
      }

      foreach ( var component in gameObject.GetComponents<Component>() ) {
        if ( component == null )
          continue;

        item.components.Add( component.GetType().FullName );
      }

      var box = gameObject.GetComponent<Box>();
      if ( box != null )
        item.shape_info = "Box halfExtents " + FormatVector( box.HalfExtents );
      var cylinder = gameObject.GetComponent<Cylinder>();
      if ( cylinder != null )
        item.shape_info = "Cylinder radius " + FormatFloat( cylinder.Radius ) + " height " + FormatFloat( cylinder.Height );
      var mesh = gameObject.GetComponent<AGXUnity.Collide.Mesh>();
      if ( mesh != null )
        item.shape_info = "Mesh sourceCount " + mesh.SourceObjects.Length.ToString( CultureInfo.InvariantCulture );
      var track = gameObject.GetComponent<Track>();
      if ( track != null )
        item.track_info = "Track width " + FormatFloat( track.Width ) + " thickness " + FormatFloat( track.Thickness );
      var wheel = gameObject.GetComponent<TrackWheel>();
      if ( wheel != null )
        item.track_info = "TrackWheel radius " + FormatFloat( wheel.Radius );

      return item;
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
      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
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

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( bool success, string message, DiagnosticsResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new DiagnosticsResult();

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
    private sealed class DiagnosticsResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public List<DiagnosticObject> objects = new List<DiagnosticObject>();
    }

    [Serializable]
    private sealed class DiagnosticObject
    {
      public string path;
      public string local_position;
      public string local_scale;
      public string lossy_scale;
      public string world_position;
      public string world_euler;
      public string renderer_bounds_size;
      public string renderer_bounds_center;
      public string shape_info;
      public string track_info;
      public List<string> components = new List<string>();
    }
  }
}
#endif
