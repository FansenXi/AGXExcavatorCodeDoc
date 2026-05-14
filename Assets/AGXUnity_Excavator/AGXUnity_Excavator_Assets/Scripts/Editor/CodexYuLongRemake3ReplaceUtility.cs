#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexYuLongRemake3ReplaceUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexYuLongRemake3Replace.request";
    private const string OutputDirectory = "Temp/CodexYuLongRemake3Replace";
    private const string OldPrefabPath = "Assets/remake2/urdf/remake2 (1).prefab";
    private const string NewPrefabPath = "Assets/remake3/urdf/remake3.prefab";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongRemake3ReplaceUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Replace YuLong Remake2 With Remake3" )]
    public static void ReplaceFromMenu()
    {
      ReplaceRemake2WithRemake3( "menu" );
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

      ReplaceRemake2WithRemake3( "request-file" );
    }

    private static void ReplaceRemake2WithRemake3( string source )
    {
      s_isRunning = true;
      var result = new ReplaceResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );

        EnsureNewPrefabHasYuLongRig( result );

        var newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>( NewPrefabPath );
        if ( newPrefab == null ) {
          WriteResult( false, $"Could not load new prefab: {NewPrefabPath}", result );
          return;
        }

        var oldRoots = FindOldPrefabInstanceRoots();
        if ( oldRoots.Count == 0 ) {
          WriteResult( false, $"No scene instances found for {OldPrefabPath}.", result );
          return;
        }

        foreach ( var oldRoot in oldRoots )
          ReplaceOneInstance( scene, oldRoot, newPrefab, result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.message = $"Replaced {result.replaced_count} remake2 instance(s) with remake3 from {source}. Updated {result.reference_replacements} scene references.";
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

    private static void EnsureNewPrefabHasYuLongRig( ReplaceResult result )
    {
      var prefabRoot = PrefabUtility.LoadPrefabContents( NewPrefabPath );
      try {
        if ( prefabRoot == null )
          throw new InvalidOperationException( $"Could not load prefab contents: {NewPrefabPath}" );

        var rig = prefabRoot.GetComponent<ExcavatorYuLong>();
        if ( rig == null ) {
          rig = prefabRoot.AddComponent<ExcavatorYuLong>();
          result.added_yulong_to_prefab = true;
        }

        rig.ResolveReferences();
        ConfigureJointRangeAndControllers( rig.SwingHinge );
        ConfigureJointRangeAndControllers( rig.BoomConstraint );
        ConfigureJointRangeAndControllers( rig.StickConstraint );
        ConfigureJointRangeAndControllers( rig.BucketConstraint );

        EditorUtility.SetDirty( rig );
        PrefabUtility.SaveAsPrefabAsset( prefabRoot, NewPrefabPath );
      }
      finally {
        if ( prefabRoot != null )
          PrefabUtility.UnloadPrefabContents( prefabRoot );
      }
    }

    private static List<GameObject> FindOldPrefabInstanceRoots()
    {
      var roots = new List<GameObject>();
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var gameObject in objects ) {
        if ( gameObject == null || !gameObject.scene.IsValid() )
          continue;
        if ( PrefabUtility.GetNearestPrefabInstanceRoot( gameObject ) != gameObject )
          continue;

        var source = PrefabUtility.GetCorrespondingObjectFromSource( gameObject );
        if ( source == null )
          continue;

        var sourcePath = AssetDatabase.GetAssetPath( source );
        if ( string.Equals( sourcePath, OldPrefabPath, StringComparison.OrdinalIgnoreCase ) )
          roots.Add( gameObject );
      }

      return roots;
    }

    private static void ReplaceOneInstance( Scene scene, GameObject oldRoot, GameObject newPrefab, ReplaceResult result )
    {
      var oldTransform = oldRoot.transform;
      var oldYuLong = oldRoot.GetComponent<ExcavatorYuLong>();
      var oldBucket = FindChildRecursive( oldTransform, "watou" );
      var parent = oldTransform.parent;
      var siblingIndex = oldTransform.GetSiblingIndex();
      var localPosition = oldTransform.localPosition;
      var localRotation = oldTransform.localRotation;
      var localScale = oldTransform.localScale;
      var wasActive = oldRoot.activeSelf;
      var oldPath = GetHierarchyPath( oldRoot );

      var instantiated = PrefabUtility.InstantiatePrefab( newPrefab, scene ) as GameObject;
      if ( instantiated == null )
        throw new InvalidOperationException( $"Could not instantiate prefab: {NewPrefabPath}" );

      var newTransform = instantiated.transform;
      newTransform.SetParent( parent, false );
      newTransform.SetSiblingIndex( siblingIndex );
      newTransform.localPosition = localPosition;
      newTransform.localRotation = localRotation;
      newTransform.localScale = localScale;
      instantiated.SetActive( wasActive );

      var newYuLong = instantiated.GetComponent<ExcavatorYuLong>();
      if ( newYuLong == null )
        newYuLong = instantiated.AddComponent<ExcavatorYuLong>();

      newYuLong.ResolveReferences();
      ConfigureJointRangeAndControllers( newYuLong.SwingHinge );
      ConfigureJointRangeAndControllers( newYuLong.BoomConstraint );
      ConfigureJointRangeAndControllers( newYuLong.StickConstraint );
      ConfigureJointRangeAndControllers( newYuLong.BucketConstraint );

      var newBucket = newYuLong.BucketReference != null ?
                      newYuLong.BucketReference :
                      FindChildRecursive( newTransform, "watou" );

      result.reference_replacements += ReplaceSceneObjectReferences( scene,
                                                                     oldTransform,
                                                                     newTransform,
                                                                     oldYuLong,
                                                                     newYuLong,
                                                                     oldBucket,
                                                                     newBucket );

      UnityEngine.Object.DestroyImmediate( oldRoot );
      result.replaced_count++;
      Append( ref result.replaced_paths, $"{oldPath} -> {GetHierarchyPath( instantiated )}" );
      Append( ref result.new_roots, GetHierarchyPath( instantiated ) );
      Append( ref result.new_bucket_references, newBucket != null ? GetHierarchyPath( newBucket.gameObject ) : "<missing watou>" );
    }

    private static void ConfigureJointRangeAndControllers( Constraint constraint )
    {
      if ( constraint == null )
        return;

      var range = constraint.GetController<RangeController>();
      if ( range != null ) {
        range.Enable = true;
        range.Range = new RangeReal( -3.14f, 3.14f );
      }

      var targetSpeed = constraint.GetController<TargetSpeedController>();
      if ( targetSpeed != null ) {
        targetSpeed.Enable = true;
      }

      var lockController = constraint.GetController<LockController>();
      if ( lockController != null ) {
        lockController.Enable = false;
      }

      EditorUtility.SetDirty( constraint );
    }

    private static int ReplaceSceneObjectReferences( Scene scene,
                                                     Transform oldRoot,
                                                     Transform newRoot,
                                                     ExcavatorYuLong oldYuLong,
                                                     ExcavatorYuLong newYuLong,
                                                     Transform oldBucket,
                                                     Transform newBucket )
    {
      var replacementCount = 0;
      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
        if ( component == null || !component.gameObject.scene.IsValid() || component.gameObject.scene != scene )
          continue;

        var serializedObject = new SerializedObject( component );
        var iterator = serializedObject.GetIterator();
        var changed = false;
        while ( iterator.NextVisible( true ) ) {
          if ( iterator.propertyType != SerializedPropertyType.ObjectReference )
            continue;

          var current = iterator.objectReferenceValue;
          UnityEngine.Object replacement = null;
          if ( current == oldRoot )
            replacement = newRoot;
          else if ( oldYuLong != null && current == oldYuLong )
            replacement = newYuLong;
          else if ( oldBucket != null && current == oldBucket )
            replacement = newBucket;

          if ( replacement == null )
            continue;

          iterator.objectReferenceValue = replacement;
          replacementCount++;
          changed = true;
        }

        if ( changed ) {
          serializedObject.ApplyModifiedPropertiesWithoutUndo();
          EditorUtility.SetDirty( component );
        }
      }

      return replacementCount;
    }

    private static Transform FindChildRecursive( Transform root, string objectName )
    {
      if ( root == null )
        return null;
      if ( root.name == objectName )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, objectName );
        if ( result != null )
          return result;
      }

      return null;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; remake3 replacement tool did not switch scenes.",
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
                                     "AGXUnity_Excavator_before_yulong_remake3_replace_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName;
      return projectRoot == null ? projectRelativePath : Path.Combine( projectRoot, projectRelativePath );
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

    private static void Append( ref string text, string value )
    {
      if ( string.IsNullOrEmpty( text ) )
        text = value;
      else
        text += "; " + value;
    }

    private static void WriteResult( bool success, string message, ReplaceResult result )
    {
      if ( result == null )
        result = new ReplaceResult();

      result.success = success;
      result.message = message;

      var outputDirectory = GetProjectRelativeAbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      var outputPath = Path.Combine( outputDirectory, "result.json" );
      File.WriteAllText( outputPath, JsonUtility.ToJson( result, true ) );
    }

    [Serializable]
    private sealed class ReplaceResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public bool added_yulong_to_prefab;
      public int replaced_count;
      public int reference_replacements;
      public string replaced_paths;
      public string new_roots;
      public string new_bucket_references;
    }
  }
}
#endif
