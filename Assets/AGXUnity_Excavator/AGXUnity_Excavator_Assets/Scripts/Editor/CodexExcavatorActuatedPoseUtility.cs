#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AGXUnity;
using AGXUnity.Model;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexExcavatorActuatedPoseUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorActuatedPose.request";
    private const string OutputDirectory = "Temp/CodexExcavatorActuatedPose";
    private const string ResultPath = OutputDirectory + "/result.json";
    private const string PosePath = OutputDirectory + "/pose.json";
    private const string SessionRunning = "CodexExcavatorActuatedPose.Running";
    private const string SessionApply = "CodexExcavatorActuatedPose.Apply";
    private const string SessionStartTime = "CodexExcavatorActuatedPose.StartTime";
    private const string SessionDuration = "CodexExcavatorActuatedPose.Duration";
    private const string SessionBoom = "CodexExcavatorActuatedPose.Boom";
    private const string SessionBucket = "CodexExcavatorActuatedPose.Bucket";
    private const string SessionStick = "CodexExcavatorActuatedPose.Stick";
    private const string SessionSwing = "CodexExcavatorActuatedPose.Swing";
    private const string SessionThrottle = "CodexExcavatorActuatedPose.Throttle";
    private const string SessionDirect = "CodexExcavatorActuatedPose.Direct";
    private const string SessionAwaitingApply = "CodexExcavatorActuatedPose.AwaitingApply";

    private static double s_nextPollTime;
    private static bool s_isStarting;

    static CodexExcavatorActuatedPoseUtility()
    {
      EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
      if ( SessionState.GetBool( SessionRunning, false ) ) {
        ContinuePlayRun();
        return;
      }

      if ( SessionState.GetBool( SessionAwaitingApply, false ) && !EditorApplication.isPlayingOrWillChangePlaymode ) {
        SessionState.SetBool( SessionAwaitingApply, false );
        if ( SessionState.GetBool( SessionApply, false ) )
          ApplyCapturedPose();
        return;
      }

      if ( s_isStarting || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;
      if ( EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      var request = File.ReadAllText( requestPath );
      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new ActuatedPoseResult { success = false, message = "Could not delete request file: " + exception.Message } );
        return;
      }

      StartPlayRun( ParseRequest( request ) );
    }

    private static PoseRequest ParseRequest( string text )
    {
      var request = new PoseRequest {
        duration = 2.0f,
        throttle = 1.0f,
        apply = false
      };

      if ( string.IsNullOrWhiteSpace( text ) )
        return request;

      var separators = new[] { '\r', '\n', ';', ',' };
      foreach ( var rawToken in text.Split( separators, StringSplitOptions.RemoveEmptyEntries ) ) {
        var token = rawToken.Trim();
        var separator = token.IndexOf( '=' );
        if ( separator < 0 )
          continue;

        var key = token.Substring( 0, separator ).Trim().ToLowerInvariant();
        var value = token.Substring( separator + 1 ).Trim();
        if ( key == "apply" ) {
          request.apply = value.Equals( "true", StringComparison.OrdinalIgnoreCase ) || value == "1";
          continue;
        }

        if ( !float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) )
          continue;

        if ( key == "duration" )
          request.duration = Mathf.Clamp( number, 0.1f, 12.0f );
        else if ( key == "boom" )
          request.boom = Mathf.Clamp( number, -1.0f, 1.0f );
        else if ( key == "bucket" )
          request.bucket = Mathf.Clamp( number, -1.0f, 1.0f );
        else if ( key == "stick" )
          request.stick = Mathf.Clamp( number, -1.0f, 1.0f );
        else if ( key == "swing" )
          request.swing = Mathf.Clamp( number, -1.0f, 1.0f );
        else if ( key == "throttle" )
          request.throttle = Mathf.Clamp01( number );
        else if ( key == "direct" )
          request.direct = number > 0.5f;
      }

      return request;
    }

    private static void StartPlayRun( PoseRequest request )
    {
      s_isStarting = true;
      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( new ActuatedPoseResult { success = false, message = "Target scene could not be opened." } );
          return;
        }

        EditorSceneManager.SaveScene( scene );
        SessionState.SetBool( SessionRunning, true );
        SessionState.SetBool( SessionApply, request.apply );
        SessionState.SetFloat( SessionStartTime, -1.0f );
        SessionState.SetFloat( SessionDuration, request.duration );
        SessionState.SetFloat( SessionBoom, request.boom );
        SessionState.SetFloat( SessionBucket, request.bucket );
        SessionState.SetFloat( SessionStick, request.stick );
        SessionState.SetFloat( SessionSwing, request.swing );
        SessionState.SetFloat( SessionThrottle, request.throttle );
        SessionState.SetBool( SessionDirect, request.direct );
        EditorApplication.isPlaying = true;
      }
      catch ( System.Exception exception ) {
        WriteResult( new ActuatedPoseResult { success = false, message = exception.ToString() } );
      }
      finally {
        s_isStarting = false;
      }
    }

    private static void ContinuePlayRun()
    {
      if ( !EditorApplication.isPlaying ) {
        if ( !EditorApplication.isPlayingOrWillChangePlaymode )
          SessionState.SetBool( SessionRunning, false );
        return;
      }

      var startTime = SessionState.GetFloat( SessionStartTime, -1.0f );
      if ( startTime < 0.0f ) {
        SessionState.SetFloat( SessionStartTime, (float)EditorApplication.timeSinceStartup );
        return;
      }

      var command = new ExcavatorActuationCommand {
        Boom = SessionState.GetFloat( SessionBoom, 0.0f ),
        Bucket = SessionState.GetFloat( SessionBucket, 0.0f ),
        Stick = SessionState.GetFloat( SessionStick, 0.0f ),
        Swing = SessionState.GetFloat( SessionSwing, 0.0f ),
        Throttle = SessionState.GetFloat( SessionThrottle, 1.0f )
      };

      var controller = FindMachineController();
      var direct = SessionState.GetBool( SessionDirect, false );
      if ( direct )
        ApplyDirectConstraintSpeeds( command );
      else if ( controller != null )
        controller.ApplyActuationCommand( command );

      var elapsed = (float)EditorApplication.timeSinceStartup - startTime;
      if ( elapsed < SessionState.GetFloat( SessionDuration, 2.0f ) )
        return;

      if ( controller != null )
        controller.StopMotion();

      var result = CapturePoseResult( elapsed, command, controller != null );
      WritePose( result.pose );
      result.pose = null;
      WriteResult( result );

      SessionState.SetBool( SessionRunning, false );
      SessionState.SetBool( SessionAwaitingApply, true );
      EditorApplication.isPlaying = false;
    }

    private static ActuatedPoseResult CapturePoseResult( float elapsed, ExcavatorActuationCommand command, bool usedController )
    {
      var result = new ActuatedPoseResult {
        success = true,
        message = "Actuated pose probe completed.",
        elapsed_seconds = FormatFloat( elapsed ),
        command = command.ToCompactString(),
        used_machine_controller = usedController,
        pose = new PoseCapture()
      };

      var root = ResolveExcavatorRoot();
      if ( root == null ) {
        result.success = false;
        result.message = "Excavator root was not found in play mode.";
        return result;
      }

      result.excavator_root = GetHierarchyPath( root );
      FillBounds( CalculateBounds( CollectMeasuredRenderers( root ) ), result.overall_bounds );
      FillBounds( CalculateBucketBounds( CollectMeasuredRenderers( root ) ), result.bucket_bounds );
      FillShovelMinimums( root, result );
      CaptureRigidBodyTransforms( root, result.pose );
      return result;
    }

    private static void ApplyDirectConstraintSpeeds( ExcavatorActuationCommand command )
    {
      var excavator = FindExcavator();
      if ( excavator == null )
        return;

      ApplySpeed( excavator.BoomPrismatics, command.Boom );
      ApplySpeed( excavator.StickPrismatic, command.Stick );
      ApplySpeed( excavator.BucketPrismatic, command.Bucket );
      ApplySpeed( excavator.SwingHinge, command.Swing );
    }

    private static Excavator FindExcavator()
    {
      foreach ( var excavator in Resources.FindObjectsOfTypeAll<Excavator>() ) {
        if ( excavator != null && excavator.gameObject.scene.IsValid() && excavator.gameObject.activeInHierarchy )
          return excavator;
      }
      return null;
    }

    private static void ApplySpeed( Constraint[] constraints, float speed )
    {
      if ( constraints == null )
        return;
      foreach ( var constraint in constraints )
        ApplySpeed( constraint, speed );
    }

    private static void ApplySpeed( Constraint constraint, float speed )
    {
      if ( constraint == null )
        return;

      var speedController = constraint.GetController<TargetSpeedController>();
      if ( speedController == null )
        return;

      var lockController = constraint.GetController<LockController>();
      if ( lockController != null )
        lockController.Enable = false;

      speedController.LockAtZeroSpeed = false;
      speedController.Enable = true;
      speedController.Speed = speed;
    }

    private static void CaptureRigidBodyTransforms( GameObject root, PoseCapture pose )
    {
      pose.root_path = GetHierarchyPath( root );
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        if ( rigidBody == null )
          continue;

        pose.transforms.Add( new TransformSample {
          path = GetHierarchyPath( rigidBody.gameObject ),
          position = rigidBody.transform.position,
          rotation = rigidBody.transform.rotation
        } );
      }
    }

    private static void ApplyCapturedPose()
    {
      var pose = ReadPose();
      if ( pose == null ) {
        WriteResult( new ActuatedPoseResult { success = false, message = "No captured pose was found to apply." } );
        return;
      }

      var scene = EnsureTargetSceneIsOpen();
      var root = ResolveExcavatorRoot();
      if ( !scene.IsValid() || root == null ) {
        WriteResult( new ActuatedPoseResult { success = false, message = "Could not reopen target scene or resolve excavator root." } );
        return;
      }

      var pathToTransform = new Dictionary<string, Transform>();
      foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) )
        pathToTransform[ GetHierarchyPath( transform.gameObject ) ] = transform;

      var applied = 0;
      foreach ( var sample in pose.transforms ) {
        if ( !pathToTransform.TryGetValue( sample.path, out var transform ) )
          continue;

        Undo.RecordObject( transform, "Apply actuated excavator pose" );
        transform.SetPositionAndRotation( sample.position, sample.rotation );
        EditorUtility.SetDirty( transform );
        applied++;
      }

      SynchronizeConstraintFlagsForCurrentTransforms( root );

      EditorSceneManager.MarkSceneDirty( scene );
      AssetDatabase.SaveAssets();
      EditorSceneManager.SaveScene( scene );

      var result = new ActuatedPoseResult {
        success = true,
        message = "Captured play-mode pose applied to edit scene.",
        excavator_root = GetHierarchyPath( root ),
        applied_transform_count = applied
      };
      FillBounds( CalculateBounds( CollectMeasuredRenderers( root ) ), result.overall_bounds );
      FillBounds( CalculateBucketBounds( CollectMeasuredRenderers( root ) ), result.bucket_bounds );
      FillShovelMinimums( root, result );
      WriteResult( result );
    }

    private static void SynchronizeConstraintFlagsForCurrentTransforms( GameObject root )
    {
      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
        if ( constraint == null || constraint.AttachmentPair == null )
          continue;

        // Keep the original non-synchronized editing mode, but refresh the connected
        // frame to the solved play-mode body pose where the mechanism is coherent.
        var wasSynchronized = constraint.AttachmentPair.Synchronized;
        constraint.AttachmentPair.Synchronized = true;
        constraint.AttachmentPair.Synchronize();
        constraint.AttachmentPair.Synchronized = wasSynchronized;
        EditorUtility.SetDirty( constraint );
      }
    }

    private static ExcavatorMachineController FindMachineController()
    {
      foreach ( var controller in Resources.FindObjectsOfTypeAll<ExcavatorMachineController>() ) {
        if ( controller != null && controller.gameObject.scene.IsValid() && controller.gameObject.activeInHierarchy )
          return controller;
      }
      return null;
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

    private static List<Renderer> CollectMeasuredRenderers( GameObject root )
    {
      var result = new List<Renderer>();
      foreach ( var renderer in root.GetComponentsInChildren<Renderer>( true ) ) {
        if ( renderer == null || renderer is ParticleSystemRenderer )
          continue;
        if ( renderer.enabled && renderer.gameObject.activeInHierarchy )
          result.Add( renderer );
      }
      return result;
    }

    private static Bounds? CalculateBucketBounds( List<Renderer> renderers )
    {
      var bucketRenderers = new List<Renderer>();
      foreach ( var renderer in renderers ) {
        var path = GetHierarchyPath( renderer.gameObject );
        if ( path.IndexOf( "/Bucket/", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.EndsWith( "/Bucket", StringComparison.OrdinalIgnoreCase ) )
          bucketRenderers.Add( renderer );
      }
      return CalculateBounds( bucketRenderers );
    }

    private static Bounds? CalculateBounds( List<Renderer> renderers )
    {
      var hasBounds = false;
      var result = new Bounds();
      foreach ( var renderer in renderers ) {
        if ( !hasBounds ) {
          result = renderer.bounds;
          hasBounds = true;
        }
        else {
          result.Encapsulate( renderer.bounds );
        }
      }
      return hasBounds ? result : (Bounds?)null;
    }

    private static void FillBounds( Bounds? bounds, BoundsSample target )
    {
      if ( !bounds.HasValue )
        return;
      target.min_m = FormatVector( bounds.Value.min );
      target.max_m = FormatVector( bounds.Value.max );
      target.center_m = FormatVector( bounds.Value.center );
      target.size_m = FormatVector( bounds.Value.size );
    }

    private static void FillShovelMinimums( GameObject root, ActuatedPoseResult result )
    {
      foreach ( var shovel in root.GetComponentsInChildren<DeformableTerrainShovel>( true ) ) {
        var minY = float.PositiveInfinity;
        AddLineMinY( shovel.TopEdge, ref minY );
        AddLineMinY( shovel.CuttingEdge, ref minY );
        AddLineMinY( shovel.ToothDirection, ref minY );
        if ( shovel.name.IndexOf( "Bucket", StringComparison.OrdinalIgnoreCase ) >= 0 )
          result.bucket_shovel_min_y_m = float.IsPositiveInfinity( minY ) ? "" : FormatFloat( minY );
        if ( shovel.name.IndexOf( "Blade", StringComparison.OrdinalIgnoreCase ) >= 0 )
          result.blade_shovel_min_y_m = float.IsPositiveInfinity( minY ) ? "" : FormatFloat( minY );
      }
    }

    private static void AddLineMinY( Line line, ref float minY )
    {
      if ( line == null )
        return;
      if ( line.Start != null )
        minY = Mathf.Min( minY, line.Start.Position.y );
      if ( line.End != null )
        minY = Mathf.Min( minY, line.End.Position.y );
    }

    private static void WritePose( PoseCapture pose )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( GetProjectRelativeAbsolutePath( PosePath ), JsonUtility.ToJson( pose, true ) );
    }

    private static PoseCapture ReadPose()
    {
      var path = GetProjectRelativeAbsolutePath( PosePath );
      return File.Exists( path ) ? JsonUtility.FromJson<PoseCapture>( File.ReadAllText( path ) ) : null;
    }

    private static void WriteResult( ActuatedPoseResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      File.WriteAllText( GetProjectRelativeAbsolutePath( ResultPath ), JsonUtility.ToJson( result, true ) );
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

    private static string GetProjectRelativeAbsolutePath( string projectRelativePath )
    {
      return Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath.Replace( '/', Path.DirectorySeparatorChar ) );
    }

    private sealed class PoseRequest
    {
      public float duration;
      public float boom;
      public float bucket;
      public float stick;
      public float swing;
      public float throttle;
      public bool apply;
      public bool direct;
    }

    [Serializable]
    private sealed class ActuatedPoseResult
    {
      public bool success;
      public string message;
      public string elapsed_seconds;
      public string command;
      public bool used_machine_controller;
      public string excavator_root;
      public int applied_transform_count;
      public string bucket_shovel_min_y_m;
      public string blade_shovel_min_y_m;
      public BoundsSample overall_bounds = new BoundsSample();
      public BoundsSample bucket_bounds = new BoundsSample();
      public PoseCapture pose;
    }

    [Serializable]
    private sealed class PoseCapture
    {
      public string root_path;
      public List<TransformSample> transforms = new List<TransformSample>();
    }

    [Serializable]
    private sealed class TransformSample
    {
      public string path;
      public Vector3 position;
      public Quaternion rotation;
    }

    [Serializable]
    private sealed class BoundsSample
    {
      public string min_m;
      public string max_m;
      public string center_m;
      public string size_m;
    }
  }
}
#endif
