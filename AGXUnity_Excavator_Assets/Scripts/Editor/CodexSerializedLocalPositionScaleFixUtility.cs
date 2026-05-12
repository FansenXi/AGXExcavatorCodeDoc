#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity;
using AGXUnity.Model;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexSerializedLocalPositionScaleFixUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexSerializedLocalPositionScaleFix.request";
    private const string OutputDirectory = "Temp/CodexSerializedLocalPositionScaleFix";
    private const float LocalPositionScale = 0.8f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexSerializedLocalPositionScaleFixUtility()
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
        ScaleConstraintFrames( root, result );
        ScaleTrackWheelFrames( root, result );
        ScaleShovelLines( root, result );

        foreach ( var component in root.GetComponentsInChildren<Component>( true ) ) {
          if ( component == null || component is Transform )
            continue;

          result.inspected_components++;
          if ( result.sample_properties.Count < 80 )
            CollectLocalPropertySamples( component, result.sample_properties );
        }

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.message = $"Scaled {result.direct_scaled_local_positions} AGX frame local positions by {LocalPositionScale}.";
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

    private static void ScaleConstraintFrames( GameObject root, FixResult result )
    {
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
        if ( constraint == null || constraint.AttachmentPair == null )
          continue;

        Undo.RecordObject( constraint, "Scale AGX constraint frames" );
        constraint.AttachmentPair.ReferenceFrame.LocalPosition *= LocalPositionScale;
        constraint.AttachmentPair.ConnectedFrame.LocalPosition *= LocalPositionScale;
        EditorUtility.SetDirty( constraint );
        result.direct_scaled_components++;
        result.direct_scaled_local_positions += 2;
        if ( result.paths.Count < 80 )
          result.paths.Add( GetHierarchyPath( constraint.gameObject ) + " :: Constraint frames" );
      }
    }

    private static void ScaleTrackWheelFrames( GameObject root, FixResult result )
    {
      foreach ( var trackWheel in root.GetComponentsInChildren<TrackWheel>( true ) ) {
        if ( trackWheel == null || trackWheel.Frame == null )
          continue;

        Undo.RecordObject( trackWheel, "Scale AGX track wheel frame" );
        trackWheel.Frame.LocalPosition *= LocalPositionScale;
        EditorUtility.SetDirty( trackWheel );
        result.direct_scaled_components++;
        result.direct_scaled_local_positions++;
        if ( result.paths.Count < 80 )
          result.paths.Add( GetHierarchyPath( trackWheel.gameObject ) + " :: TrackWheel frame" );
      }
    }

    private static void ScaleShovelLines( GameObject root, FixResult result )
    {
      foreach ( var shovel in root.GetComponentsInChildren<DeformableTerrainShovel>( true ) ) {
        if ( shovel == null )
          continue;

        Undo.RecordObject( shovel, "Scale AGX shovel frames" );
        var scaled = 0;
        scaled += ScaleLine( shovel.TopEdge );
        scaled += ScaleLine( shovel.CuttingEdge );
        scaled += ScaleLine( shovel.ToothDirection );
        EditorUtility.SetDirty( shovel );

        if ( scaled <= 0 )
          continue;

        result.direct_scaled_components++;
        result.direct_scaled_local_positions += scaled;
        if ( result.paths.Count < 80 )
          result.paths.Add( GetHierarchyPath( shovel.gameObject ) + " :: Shovel lines (" + scaled + ")" );
      }
    }

    private static int ScaleLine( Line line )
    {
      if ( line == null )
        return 0;

      var scaled = 0;
      if ( line.Start != null ) {
        line.Start.LocalPosition *= LocalPositionScale;
        scaled++;
      }
      if ( line.End != null ) {
        line.End.LocalPosition *= LocalPositionScale;
        scaled++;
      }

      return scaled;
    }

    private static void CollectLocalPropertySamples( Component component, List<string> samples )
    {
      var typeName = component.GetType().FullName;
      if ( typeName == null ||
           ( typeName.IndexOf( "Constraint", StringComparison.OrdinalIgnoreCase ) < 0 &&
             typeName.IndexOf( "TrackWheel", StringComparison.OrdinalIgnoreCase ) < 0 &&
             typeName.IndexOf( "Shovel", StringComparison.OrdinalIgnoreCase ) < 0 ) )
        return;

      var serializedObject = new SerializedObject( component );
      var iterator = serializedObject.GetIterator();
      while ( samples.Count < 80 && iterator.Next( true ) ) {
        if ( iterator.propertyPath.IndexOf( "local", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        samples.Add( GetHierarchyPath( component.gameObject ) + " :: " + component.GetType().Name + " :: " + iterator.propertyPath + " :: " + iterator.propertyType );
      }
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.IsValid() && activeScene.path != ScenePath && activeScene.isDirty )
        return default;

      AssetDatabase.Refresh();
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
      public int inspected_components;
      public int direct_scaled_components;
      public int direct_scaled_local_positions;
      public int scaled_components;
      public int scaled_local_positions;
      public List<string> paths = new List<string>();
      public List<string> sample_properties = new List<string>();
    }
  }
}
#endif
