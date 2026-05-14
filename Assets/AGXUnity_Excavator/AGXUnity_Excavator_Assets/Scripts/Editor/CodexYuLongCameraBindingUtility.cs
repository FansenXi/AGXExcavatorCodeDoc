#if UNITY_EDITOR
using System;
using System.IO;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexYuLongCameraBindingUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexYuLongCameraBinding.request";
    private const string OutputDirectory = "Temp/CodexYuLongCameraBinding";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongCameraBindingUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Bind YuLong Cameras" )]
    public static void BindFromMenu()
    {
      BindYuLongCameras( "menu" );
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

      BindYuLongCameras( "request-file" );
    }

    private static void BindYuLongCameras( string source )
    {
      s_isRunning = true;
      var result = new CameraBindingResult { Source = source };

      try {
        var scene = EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
        if ( !scene.IsValid() )
          throw new InvalidOperationException( $"Could not open scene: {ScenePath}" );

        var backupPath = BackupScene();
        result.BackupPath = backupPath;

        var yuLong = FindActiveYuLong();
        if ( yuLong == null )
          throw new InvalidOperationException( "Could not find an active ExcavatorYuLong scene instance." );

        var machineRoot = yuLong.transform;
        var bucketReference = yuLong.BucketReference;
        var followTarget = FindPreferredFollowTarget( machineRoot );
        var machineController = FindMachineControllerForRoot( machineRoot );

        result.MachineRoot = GetPath( machineRoot );
        result.BucketReference = GetPath( bucketReference );
        result.FollowTarget = GetPath( followTarget );
        result.MachineController = machineController != null ? GetPath( machineController.transform ) : null;

        if ( followTarget == null )
          throw new InvalidOperationException( "Could not find YuLong follow target; expected child named 'controller' or 'base_link'." );

        var mainCamera = FindSceneObject( "Main Camera" );
        if ( mainCamera == null )
          throw new InvalidOperationException( "Could not find Main Camera." );

        BindMainCamera( mainCamera, machineRoot, followTarget );
        result.MainCamera = $"Main Camera -> root {GetPath( machineRoot )}, follow {GetPath( followTarget )}";

        var bucketCamera = FindSceneObject( "BucketCamera" );
        if ( bucketCamera != null && machineController != null ) {
          BindTrackedCamera( bucketCamera, machineController, TrackedCameraWindow.AnchorMode.BucketReference, null );
          result.BucketCamera = $"BucketCamera -> {GetPath( machineController.transform )}.BucketReference";
        }
        else {
          result.BucketCamera = bucketCamera == null ? "BucketCamera not found" : "BucketCamera skipped: missing machine controller";
        }

        var followCamera = FindSceneObject( "FollowCamera" );
        if ( followCamera != null && machineController != null ) {
          BindTrackedCamera( followCamera, machineController, TrackedCameraWindow.AnchorMode.CustomTransform, followTarget );
          result.FollowCamera = $"FollowCamera -> custom target {GetPath( followTarget )}";
        }
        else {
          result.FollowCamera = followCamera == null ? "FollowCamera not found" : "FollowCamera skipped: missing machine controller";
        }

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );
        AssetDatabase.SaveAssets();

        WriteResult( true, "YuLong camera bindings updated.", result );
      }
      catch ( Exception exception ) {
        WriteResult( false, exception.ToString(), result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void BindMainCamera( GameObject mainCamera, Transform machineRoot, Transform followTarget )
    {
      var linkCamera = mainCamera.GetComponent<LinkCamera>();
      if ( linkCamera == null )
        linkCamera = mainCamera.AddComponent<LinkCamera>();

      linkCamera.enabled = true;
      linkCamera.Enabled = true;

      var serialized = new SerializedObject( linkCamera );
      SetObjectReference( serialized, "m_machineRoot", machineRoot );
      SetObjectReference( serialized, "m_follow_object", followTarget.gameObject );
      serialized.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty( linkCamera );
    }

    private static void BindTrackedCamera( GameObject cameraObject,
                                           ExcavatorMachineController machineController,
                                           TrackedCameraWindow.AnchorMode anchorMode,
                                           Transform customTarget )
    {
      var trackedCamera = cameraObject.GetComponent<TrackedCameraWindow>();
      if ( trackedCamera == null )
        trackedCamera = cameraObject.AddComponent<TrackedCameraWindow>();

      trackedCamera.enabled = true;

      var serialized = new SerializedObject( trackedCamera );
      SetObjectReference( serialized, "m_machineController", machineController );
      var anchorModeProperty = serialized.FindProperty( "m_anchorMode" );
      if ( anchorModeProperty != null )
        anchorModeProperty.enumValueIndex = (int)anchorMode;
      SetObjectReference( serialized, "m_customTarget", customTarget );
      serialized.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty( trackedCamera );
    }

    private static ExcavatorYuLong FindActiveYuLong()
    {
      ExcavatorYuLong fallback = null;
      foreach ( var candidate in UnityEngine.Object.FindObjectsByType<ExcavatorYuLong>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( candidate == null )
          continue;

        if ( fallback == null )
          fallback = candidate;

        if ( candidate.gameObject.activeInHierarchy && candidate.name.Contains( "remake3", StringComparison.OrdinalIgnoreCase ) )
          return candidate;
      }

      return fallback;
    }

    private static ExcavatorMachineController FindMachineControllerForRoot( Transform machineRoot )
    {
      ExcavatorMachineController fallback = null;
      foreach ( var candidate in UnityEngine.Object.FindObjectsByType<ExcavatorMachineController>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( candidate == null )
          continue;

        if ( fallback == null )
          fallback = candidate;

        if ( candidate.MachineRoot == machineRoot )
          return candidate;
      }

      return fallback;
    }

    private static Transform FindPreferredFollowTarget( Transform machineRoot )
    {
      if ( machineRoot == null )
        return null;

      var directController = FindDirectChild( machineRoot, "controller" );
      if ( directController != null )
        return directController;

      var controller = FindChildByExactName( machineRoot, "controller" );
      if ( controller != null )
        return controller;

      var baseLink = FindDirectChild( machineRoot, "base_link" );
      if ( baseLink != null )
        return baseLink;

      return FindChildByExactName( machineRoot, "base_link" ) ?? machineRoot;
    }

    private static Transform FindDirectChild( Transform root, string childName )
    {
      foreach ( Transform child in root ) {
        if ( string.Equals( child.name, childName, StringComparison.OrdinalIgnoreCase ) )
          return child;
      }

      return null;
    }

    private static Transform FindChildByExactName( Transform root, string childName )
    {
      if ( root == null )
        return null;

      if ( string.Equals( root.name, childName, StringComparison.OrdinalIgnoreCase ) )
        return root;

      foreach ( Transform child in root ) {
        var match = FindChildByExactName( child, childName );
        if ( match != null )
          return match;
      }

      return null;
    }

    private static GameObject FindSceneObject( string name )
    {
      foreach ( var candidate in UnityEngine.Object.FindObjectsByType<GameObject>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( candidate != null && candidate.name == name )
          return candidate;
      }

      return null;
    }

    private static void SetObjectReference( SerializedObject serialized, string propertyName, UnityEngine.Object value )
    {
      var property = serialized.FindProperty( propertyName );
      if ( property == null )
        throw new InvalidOperationException( $"Serialized property '{propertyName}' was not found on {serialized.targetObject}." );

      property.objectReferenceValue = value;
    }

    private static string BackupScene()
    {
      var backupDirectory = Path.Combine( Directory.GetCurrentDirectory(), "CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );

      var backupPath = Path.Combine(
        backupDirectory,
        $"AGXUnity_Excavator_before_yulong_camera_binding_{DateTime.Now:yyyyMMdd_HHmmss}.unity" );
      File.Copy( Path.Combine( Directory.GetCurrentDirectory(), ScenePath ), backupPath, true );
      return backupPath;
    }

    private static string GetPath( Transform transform )
    {
      if ( transform == null )
        return null;

      var path = transform.name;
      var current = transform.parent;
      while ( current != null ) {
        path = $"{current.name}/{path}";
        current = current.parent;
      }

      return path;
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static void WriteResult( bool success, string message, CameraBindingResult result )
    {
      var outputDirectory = GetProjectRelativeAbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      var outputPath = Path.Combine( outputDirectory, "result.json" );
      var payload = new ResultEnvelope
      {
        Success = success,
        Message = message,
        Result = result
      };
      File.WriteAllText( outputPath, JsonUtility.ToJson( payload, true ) );
      Debug.Log( $"CodexYuLongCameraBindingUtility: {( success ? "success" : "failed" )}: {message}" );
    }

    [Serializable]
    private class ResultEnvelope
    {
      public bool Success;
      public string Message;
      public CameraBindingResult Result;
    }

    [Serializable]
    private class CameraBindingResult
    {
      public string Source;
      public string BackupPath;
      public string MachineRoot;
      public string BucketReference;
      public string FollowTarget;
      public string MachineController;
      public string MainCamera;
      public string BucketCamera;
      public string FollowCamera;
    }
  }
}
#endif
