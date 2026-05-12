#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexExcavatorConstraintRepairUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorConstraintRepair.request";
    private const string OutputDirectory = "Temp/CodexExcavatorConstraintRepair";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorConstraintRepairUtility()
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
        WriteResult( new RepairResult { success = false, message = "Could not delete request file: " + exception.Message } );
        return;
      }

      RunRepair();
    }

    private static void RunRepair()
    {
      s_isRunning = true;
      var result = new RepairResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          result.message = "Target scene could not be opened.";
          WriteResult( result );
          return;
        }

        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          result.message = "Excavator root was not found.";
          WriteResult( result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        result.scene_backup_path = BackupSceneFile();

        foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
          if ( constraint == null || constraint.AttachmentPair == null )
            continue;

          if ( !ShouldRepairCoincidentFrames( constraint ) ) {
            result.skipped_constraints++;
            continue;
          }

          var pair = constraint.AttachmentPair;
          var beforeDistance = Vector3.Distance( pair.ReferenceFrame.Position, pair.ConnectedFrame.Position );
          var beforeAxisAngle = Vector3.Angle( pair.ReferenceFrame.Rotation * Vector3.forward,
                                               pair.ConnectedFrame.Rotation * Vector3.forward );

          Undo.RecordObject( constraint, "Repair excavator coincident constraint frames" );
          pair.ConnectedFrame.Position = pair.ReferenceFrame.Position;
          pair.ConnectedFrame.Rotation = pair.ReferenceFrame.Rotation;
          EditorUtility.SetDirty( constraint );

          result.repaired_constraints++;
          result.max_repaired_position_error_m = Mathf.Max( result.max_repaired_position_error_m, beforeDistance );
          result.max_repaired_axis_error_deg = Mathf.Max( result.max_repaired_axis_error_deg, beforeAxisAngle );
          if ( result.samples.Count < 80 )
            result.samples.Add( constraint.name + " " + constraint.Type + " " +
                                FormatFloat( beforeDistance ) + "m " +
                                FormatFloat( beforeAxisAngle ) + "deg" );
        }

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.success = true;
        result.message = "Repaired coincident hinge-style constraint frames while leaving prismatic actuator offsets unchanged.";
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

    private static bool ShouldRepairCoincidentFrames( Constraint constraint )
    {
      return constraint.Type == ConstraintType.Hinge ||
             constraint.Type == ConstraintType.BallJoint ||
             constraint.Type == ConstraintType.LockJoint ||
             constraint.Type == ConstraintType.AngularLockJoint ||
             constraint.Type == ConstraintType.PlaneJoint ||
             constraint.Type == ConstraintType.GenericConstraint1DOF;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;
      if ( activeScene.IsValid() && activeScene.isDirty )
        EditorSceneManager.SaveScene( activeScene );
      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static string BackupSceneFile()
    {
      var sceneAbsolutePath = GetProjectRelativeAbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = GetProjectRelativeAbsolutePath( "Temp/CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_constraint_repair_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static GameObject ResolveExcavatorRoot()
    {
      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;
        if ( component.GetType().FullName != "AGXUnity.Excavator" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
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

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( RepairResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( Path.Combine( GetProjectRelativeAbsolutePath( OutputDirectory ), "result.json" ),
                         JsonUtility.ToJson( result, true ) );
    }

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    [Serializable]
    private sealed class RepairResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public string scene_backup_path;
      public int repaired_constraints;
      public int skipped_constraints;
      public float max_repaired_position_error_m;
      public float max_repaired_axis_error_deg;
      public List<string> samples = new List<string>();
    }
  }
}
#endif
