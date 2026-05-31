#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity.Collide;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexYuLongTaskBindingUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexYuLongTaskBindings.request";
    private const string OutputDirectory = "Temp/CodexYuLongTaskBindings";
    private const string ExcavatorCenterlineName = "Excavator_Centerline_Z6p375";
    private const string ExcavatorReferenceSphereName = "Excavator_BoomBase_Target_X4p0_Z6p375";

    private static readonly string[] TargetNameOrder = { "DumpArea", "Dump", "DumpBox", "ContainerBox" };

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexYuLongTaskBindingUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Repair YuLong Task Bindings" )]
    public static void RepairFromMenu()
    {
      RepairTaskBindings( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = AbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( false, $"Could not delete request file: {exception.Message}", null );
        return;
      }

      RepairTaskBindings( "request-file" );
    }

    private static void RepairTaskBindings( string source )
    {
      s_isRunning = true;
      var result = new TaskBindingResult { source = source };

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        result.scene_backup_path = SaveCurrentSceneBackup( scene );

        PreserveManualDigAreaReference( result );
        AlignDumpTargetToFootprint( result );
        ConfigureTargetSensorDefault( result );
        CalibrateMachineInitialPose( result );

        EditorSceneManager.MarkSceneDirty( scene );
        EditorSceneManager.SaveScene( scene );
        AssetDatabase.SaveAssets();

        WriteResult( true, "YuLong task bindings repaired.", result );
      }
      catch ( System.Exception exception ) {
        result.message = exception.ToString();
        WriteResult( false, result.message, result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static void PreserveManualDigAreaReference( TaskBindingResult result )
    {
      var digMeasurement = FindObject<global::DigAreaMeasurement>();
      var digBox = digMeasurement != null ? GetSerializedObjectReference<Box>( digMeasurement, "m_digAreaBox" ) : null;
      var digRoot = digBox != null ? digBox.transform.parent : FindSceneTransform( "AGXUnity.RigidBody.DigArea" );

      if ( digRoot == null || digBox == null ) {
        Append( ref result.warnings, "Could not inspect manual DigArea reference: missing DigArea root or assigned Box." );
        return;
      }

      result.dig_area =
        $"Manual DigArea preserved: root={FormatVector( digRoot.position )}; " +
        $"box={FormatVector( digBox.transform.position )}; halfExtents={FormatVector( digBox.HalfExtents )}";
    }

    private static void AlignDumpTargetToFootprint( TaskBindingResult result )
    {
      var dumpFootprint = FindFootprintTransform( "Dump_Footprint", "CodexDumpAreaBoards" );
      var dumpSensor = FindTargetSensorByName( "DumpArea" );
      var dumpBox = dumpSensor != null ? dumpSensor.GetComponent<Box>() : null;
      var dumpTransform = dumpSensor != null ? dumpSensor.transform : FindSceneTransform( "SubmergedBox" );
      if ( dumpFootprint == null || dumpTransform == null ) {
        Append( ref result.warnings, "Could not fully align DumpArea: missing Dump_Footprint or DumpArea target." );
        return;
      }

      var previousPosition = dumpTransform.position;
      var targetPosition = dumpFootprint.position;
      if ( dumpBox != null )
        targetPosition.y += Mathf.Max( 0.0f, dumpBox.HalfExtents.y - 0.015f );
      else
        targetPosition.y = previousPosition.y;

      dumpTransform.position = targetPosition;
      EditorUtility.SetDirty( dumpTransform );
      if ( dumpSensor != null )
        EditorUtility.SetDirty( dumpSensor );

      result.dump_target = $"DumpArea {FormatVector( previousPosition )} -> {FormatVector( dumpTransform.position )}";
    }

    private static void ConfigureTargetSensorDefault( TaskBindingResult result )
    {
      var switchable = FindObject<global::SwitchableTargetMassSensor>();
      if ( switchable == null ) {
        Append( ref result.warnings, "SwitchableTargetMassSensor not found." );
        return;
      }

      var sensors = new List<global::TargetMassSensorBase>();
      var dumpSensor = FindTargetSensorByName( "DumpArea" );
      if ( dumpSensor != null )
        sensors.Add( dumpSensor );

      foreach ( var sensor in UnityEngine.Object.FindObjectsByType<global::TargetMassSensorBase>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( sensor != null && !sensors.Contains( sensor ) )
          sensors.Add( sensor );
      }

      var serialized = new SerializedObject( switchable );
      var targetSensorsProperty = serialized.FindProperty( "m_targetSensors" );
      if ( targetSensorsProperty != null ) {
        targetSensorsProperty.arraySize = sensors.Count;
        for ( var index = 0; index < sensors.Count; index++ )
          targetSensorsProperty.GetArrayElementAtIndex( index ).objectReferenceValue = sensors[index];
      }

      var defaultIndexProperty = serialized.FindProperty( "m_defaultTargetIndex" );
      if ( defaultIndexProperty != null )
        defaultIndexProperty.intValue = 0;

      var preferredNamesProperty = serialized.FindProperty( "m_preferredDefaultTargetNames" );
      if ( preferredNamesProperty != null ) {
        preferredNamesProperty.arraySize = TargetNameOrder.Length;
        for ( var index = 0; index < TargetNameOrder.Length; index++ )
          preferredNamesProperty.GetArrayElementAtIndex( index ).stringValue = TargetNameOrder[index];
      }

      serialized.ApplyModifiedPropertiesWithoutUndo();
      EditorUtility.SetDirty( switchable );
      result.target_sensor = $"Default target is DumpArea; target order={string.Join( ",", TargetNameOrder )}";
    }

    private static void CalibrateMachineInitialPose( TaskBindingResult result )
    {
      var controller = FindObject<ExcavatorMachineController>();
      var centerline = FindSceneTransform( ExcavatorCenterlineName );
      var referenceSphere = FindSceneTransform( ExcavatorReferenceSphereName );

      if ( controller == null || controller.MachineRoot == null || centerline == null || referenceSphere == null ) {
        Append( ref result.warnings, "Skipped YuLong initial pose calibration: missing controller/root/centerline/reference sphere." );
        return;
      }

      var anchor = FindMachineAnchor( controller.MachineRoot );
      var anchorPosition = anchor.position;
      var targetAnchorPosition = anchorPosition;
      targetAnchorPosition.x = referenceSphere.position.x;
      targetAnchorPosition.z = centerline.position.z;

      var delta = targetAnchorPosition - anchorPosition;
      delta.y = 0.0f;
      result.initial_bucket = $"anchor={anchor.name} position={FormatVector( anchorPosition )}; referenceSphere={FormatVector( referenceSphere.position )}; centerlineZ={centerline.position.z:0.###}";
      result.suggested_root_delta = $"rootDeltaXZ=({delta.x:0.###}, {delta.z:0.###})";

      var previousPosition = controller.MachineRoot.position;
      controller.MachineRoot.position += delta;
      EditorUtility.SetDirty( controller.MachineRoot );
      result.initial_pose_adjustment = $"Machine root moved {FormatVector( previousPosition )} -> {FormatVector( controller.MachineRoot.position )}";
    }

    private static Vector3 EstimateBucketReferenceCenter( Transform bucketReference )
    {
      if ( bucketReference == null )
        return Vector3.zero;

      var renderers = bucketReference.GetComponentsInChildren<Renderer>( true );
      if ( renderers == null || renderers.Length == 0 )
        return bucketReference.position;

      var hasBounds = false;
      var bounds = new Bounds( bucketReference.position, Vector3.zero );
      foreach ( var renderer in renderers ) {
        if ( renderer == null )
          continue;

        if ( !hasBounds ) {
          bounds = renderer.bounds;
          hasBounds = true;
        }
        else {
          bounds.Encapsulate( renderer.bounds );
        }
      }

      return hasBounds ? bounds.center : bucketReference.position;
    }

    private static T FindObject<T>() where T : UnityEngine.Object
    {
      var objects = UnityEngine.Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      return objects != null && objects.Length > 0 ? objects[0] : null;
    }

    private static global::TargetMassSensorBase FindTargetSensorByName( string targetName )
    {
      foreach ( var sensor in UnityEngine.Object.FindObjectsByType<global::TargetMassSensorBase>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( sensor != null && string.Equals( sensor.TargetName, targetName, StringComparison.OrdinalIgnoreCase ) )
          return sensor;
      }

      return null;
    }

    private static Transform FindSceneTransform( string objectName )
    {
      foreach ( var transform in UnityEngine.Object.FindObjectsByType<Transform>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( transform != null && transform.name == objectName )
          return transform;
      }

      return null;
    }

    private static Transform FindFootprintTransform( string objectName, string ancestorName )
    {
      foreach ( var transform in UnityEngine.Object.FindObjectsByType<Transform>( FindObjectsInactive.Include, FindObjectsSortMode.None ) ) {
        if ( transform != null && transform.name == objectName && HasAncestorNamed( transform, ancestorName ) )
          return transform;
      }

      return FindSceneTransform( objectName );
    }

    private static bool HasAncestorNamed( Transform transform, string ancestorName )
    {
      var current = transform != null ? transform.parent : null;
      while ( current != null ) {
        if ( current.name == ancestorName )
          return true;
        current = current.parent;
      }

      return false;
    }

    private static Transform FindMachineAnchor( Transform machineRoot )
    {
      var baseLink = FindChildRecursive( machineRoot, "base_link" );
      if ( baseLink != null ) {
        var controller = FindDirectChild( baseLink, "controller" ) ?? FindChildRecursive( baseLink, "controller" );
        if ( controller != null )
          return controller;
      }

      return FindChildRecursive( machineRoot, "controller" ) ?? machineRoot;
    }

    private static Transform FindDirectChild( Transform parent, string childName )
    {
      if ( parent == null )
        return null;

      for ( var index = 0; index < parent.childCount; index++ ) {
        var child = parent.GetChild( index );
        if ( child != null && child.name == childName )
          return child;
      }

      return null;
    }

    private static Transform FindChildRecursive( Transform root, string childName )
    {
      if ( root == null )
        return null;

      if ( root.name == childName )
        return root;

      for ( var index = 0; index < root.childCount; index++ ) {
        var found = FindChildRecursive( root.GetChild( index ), childName );
        if ( found != null )
          return found;
      }

      return null;
    }

    private static T GetSerializedObjectReference<T>( UnityEngine.Object target, string propertyName ) where T : UnityEngine.Object
    {
      var serialized = new SerializedObject( target );
      var property = serialized.FindProperty( propertyName );
      return property != null ? property.objectReferenceValue as T : null;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; YuLong task binding tool did not switch scenes.",
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

      var sceneAbsolutePath = AbsolutePath( ScenePath );
      if ( !File.Exists( sceneAbsolutePath ) )
        return string.Empty;

      var backupDirectory = AbsolutePath( "CodexSceneBackups" );
      Directory.CreateDirectory( backupDirectory );
      var backupPath = Path.Combine( backupDirectory,
                                     "AGXUnity_Excavator_before_yulong_task_bindings_" +
                                     DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture ) +
                                     ".unity" );
      File.Copy( sceneAbsolutePath, backupPath, overwrite: false );
      return backupPath;
    }

    private static string AbsolutePath( string projectRelativePath )
    {
      var projectRoot = Directory.GetParent( Application.dataPath )?.FullName ?? Directory.GetCurrentDirectory();
      return Path.Combine( projectRoot, projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private static string FormatVector( Vector3 value )
    {
      return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }

    private static void Append( ref string text, string value )
    {
      if ( string.IsNullOrEmpty( text ) )
        text = value;
      else
        text += "; " + value;
    }

    private static void WriteResult( bool success, string message, TaskBindingResult result )
    {
      if ( result == null )
        result = new TaskBindingResult();

      result.success = success;
      result.message = message;

      var outputDirectory = AbsolutePath( OutputDirectory );
      Directory.CreateDirectory( outputDirectory );
      File.WriteAllText( Path.Combine( outputDirectory, "result.json" ), JsonUtility.ToJson( result, true ) );
    }

    [Serializable]
    private sealed class TaskBindingResult
    {
      public bool success;
      public string source;
      public string message;
      public string scene_backup_path;
      public string dig_area;
      public string dump_target;
      public string target_sensor;
      public string initial_bucket;
      public string suggested_root_delta;
      public string initial_pose_adjustment;
      public string warnings;
    }
  }
}
#endif
