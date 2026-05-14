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
  public static class CodexYuLongControlTuningUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexYuLongControlTuning.request";
    private const string OutputDirectory = "Temp/CodexYuLongControlTuning";
    private const string PrefabPath = "Assets/remake3/urdf/remake3.prefab";

    private const float SwingTorqueLimit = 100000.0f;
    private const float BoomTorqueLimit = 80000.0f;
    private const float StickTorqueLimit = 60000.0f;
    private const float BucketTorqueLimit = 50000.0f;

    private const float SwingMinAngle = -2.094395f;  // -120 deg
    private const float SwingMaxAngle = 2.094395f;   //  120 deg
    private const float BoomMinAngle = -0.785398f;   //  -45 deg
    private const float BoomMaxAngle = 1.221730f;    //   70 deg
    private const float StickMinAngle = -1.745329f;  // -100 deg
    private const float StickMaxAngle = 0.872665f;   //   50 deg
    private const float BucketMinAngle = -1.570796f; //  -90 deg
    private const float BucketMaxAngle = 1.047198f;  //   60 deg

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongControlTuningUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Tune YuLong Control Strength" )]
    public static void TuneFromMenu()
    {
      TuneYuLongControl( "menu" );
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

      TuneYuLongControl( "request-file" );
    }

    private static void TuneYuLongControl( string source )
    {
      s_isRunning = true;
      var result = new TuneResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );

        TunePrefab( result );
        TuneSceneInstances( result );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.message = $"Tuned YuLong control strength from {source}. Prefab tuned={result.prefab_tuned}; scene rigs tuned={result.scene_rigs_tuned}.";
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

    private static void TunePrefab( TuneResult result )
    {
      var prefabRoot = PrefabUtility.LoadPrefabContents( PrefabPath );
      try {
        if ( prefabRoot == null )
          throw new InvalidOperationException( $"Could not load prefab contents: {PrefabPath}" );

        var rig = prefabRoot.GetComponent<ExcavatorYuLong>();
        if ( rig == null )
          rig = prefabRoot.AddComponent<ExcavatorYuLong>();

        TuneRigRoot( prefabRoot.transform, rig, result );
        PrefabUtility.SaveAsPrefabAsset( prefabRoot, PrefabPath );
        result.prefab_tuned = true;
      }
      finally {
        if ( prefabRoot != null )
          PrefabUtility.UnloadPrefabContents( prefabRoot );
      }
    }

    private static void TuneSceneInstances( TuneResult result )
    {
      var rigs = Resources.FindObjectsOfTypeAll<ExcavatorYuLong>();
      foreach ( var rig in rigs ) {
        if ( rig == null || !rig.gameObject.scene.IsValid() )
          continue;

        TuneRigRoot( rig.transform, rig, result );
        result.scene_rigs_tuned++;
      }
    }

    private static void TuneRigRoot( Transform root, ExcavatorYuLong rig, TuneResult result )
    {
      if ( root == null || rig == null )
        return;

      rig.ResolveReferences();

      TuneBody( root, "base_link", 814.3f, agx.RigidBody.MotionControl.STATIC, result );
      TuneBody( root, "controller", 1543.7f, agx.RigidBody.MotionControl.DYNAMICS, result );
      TuneBody( root, "dabi", 180.0f, agx.RigidBody.MotionControl.DYNAMICS, result );
      TuneBody( root, "xiaobi", 120.0f, agx.RigidBody.MotionControl.DYNAMICS, result );
      TuneBody( root, "watou", 70.0f, agx.RigidBody.MotionControl.DYNAMICS, result );

      TuneConstraint( rig.SwingHinge, "joint1", SwingTorqueLimit, SwingMinAngle, SwingMaxAngle, result );
      TuneConstraint( rig.BoomConstraint, "joint2", BoomTorqueLimit, BoomMinAngle, BoomMaxAngle, result );
      TuneConstraint( rig.StickConstraint, "joint3", StickTorqueLimit, StickMinAngle, StickMaxAngle, result );
      TuneConstraint( rig.BucketConstraint, "joint4", BucketTorqueLimit, BucketMinAngle, BucketMaxAngle, result );

      EditorUtility.SetDirty( rig );
    }

    private static void TuneBody( Transform root,
                                  string objectName,
                                  float massKg,
                                  agx.RigidBody.MotionControl motionControl,
                                  TuneResult result )
    {
      var transform = FindChildRecursive( root, objectName );
      var rigidBody = transform != null ? transform.GetComponent<RigidBody>() : null;
      if ( rigidBody == null ) {
        Append( ref result.warnings, $"Missing RigidBody on {objectName}" );
        return;
      }

      rigidBody.MotionControl = motionControl;
      rigidBody.MassProperties.Mass.UseDefault = false;
      rigidBody.MassProperties.Mass.UserValue = massKg;

      EditorUtility.SetDirty( rigidBody );
      result.bodies_tuned++;
      Append( ref result.body_settings, $"{GetHierarchyPath( transform.gameObject )}: mass={massKg:0.#}kg motion={motionControl}" );
    }

    private static void TuneConstraint( Constraint constraint,
                                        string name,
                                        float torqueLimit,
                                        float minAngle,
                                        float maxAngle,
                                        TuneResult result )
    {
      if ( constraint == null ) {
        Append( ref result.warnings, $"Missing constraint {name}" );
        return;
      }

      var range = constraint.GetController<RangeController>();
      if ( range != null ) {
        range.Enable = true;
        range.Range = new RangeReal( minAngle, maxAngle );
      }

      var targetSpeed = constraint.GetController<TargetSpeedController>();
      if ( targetSpeed != null ) {
        targetSpeed.Enable = true;
        targetSpeed.LockAtZeroSpeed = true;
        targetSpeed.ForceRange = new RangeReal( -torqueLimit, torqueLimit );
      }
      else {
        Append( ref result.warnings, $"Missing TargetSpeedController on {name}" );
      }

      var lockController = constraint.GetController<LockController>();
      if ( lockController != null ) {
        lockController.Enable = false;
        lockController.ForceRange = new RangeReal( -torqueLimit, torqueLimit );
      }

      EditorUtility.SetDirty( constraint );
      result.constraints_tuned++;
      Append( ref result.constraint_settings,
              $"{GetHierarchyPath( constraint.gameObject )}: range=[{minAngle:0.###},{maxAngle:0.###}]rad targetSpeedForce=+/-{torqueLimit:0.#}" );
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
                     $"Active scene '{activeScene.path}' has unsaved changes; YuLong tuning tool did not switch scenes.",
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
                                     "AGXUnity_Excavator_before_yulong_control_tuning_" +
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

    private static void WriteResult( bool success, string message, TuneResult result )
    {
      if ( result == null )
        result = new TuneResult();

      result.success = success;
      result.message = message;

      var outputDirectory = GetProjectRelativeAbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      File.WriteAllText( Path.Combine( outputDirectory, "result.json" ), JsonUtility.ToJson( result, true ) );
    }

    [Serializable]
    private sealed class TuneResult
    {
      public bool success;
      public string message;
      public string scene_backup_path;
      public bool prefab_tuned;
      public int scene_rigs_tuned;
      public int bodies_tuned;
      public int constraints_tuned;
      public string body_settings;
      public string constraint_settings;
      public string warnings;
    }
  }
}
#endif
