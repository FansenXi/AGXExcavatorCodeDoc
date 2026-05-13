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
  public static class CodexExcavatorPoseSnapshotUtility
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexExcavatorPoseSnapshot.request";
    private const string OutputDirectory = "Temp/CodexExcavatorPoseSnapshot";
    private const int MaxKeyTransforms = 260;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorPoseSnapshotUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Capture Excavator Pose Snapshot" )]
    public static void CapturePoseSnapshotFromMenu()
    {
      CapturePoseSnapshot( "menu" );
    }

    private static void PollForRequest()
    {
      if ( s_isRunning || EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 0.5;

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = GetProjectRelativeAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new PoseSnapshotResult {
          success = false,
          message = "Could not delete request file: " + exception.Message
        } );
        return;
      }

      CapturePoseSnapshot( "request-file" );
    }

    private static void CapturePoseSnapshot( string source )
    {
      s_isRunning = true;
      var result = new PoseSnapshotResult {
        source = source,
        unity_is_playing = EditorApplication.isPlaying,
        realtime_since_startup_s = FormatFloat( Time.realtimeSinceStartup )
      };

      try {
        var scene = EnsureTargetSceneIsAvailable( result );
        if ( !scene.IsValid() ) {
          WriteResult( result );
          return;
        }

        result.scene_path = scene.path;
        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          result.message = "Excavator root was not found.";
          WriteResult( result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        result.root_transform = DescribeTransform( root.transform );

        var renderers = CollectMeasuredRenderers( root );
        FillBounds( CalculateBounds( renderers ), result.overall_bounds );
        FillBounds( CalculateBucketBounds( renderers ), result.bucket_bounds );

        foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) )
          result.rigid_bodies.Add( DescribeRigidBody( rigidBody ) );

        foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) )
          result.constraints.Add( DescribeConstraint( constraint ) );

        foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) ) {
          if ( result.key_transforms.Count >= MaxKeyTransforms )
            break;

          if ( IsKeyTransform( transform ) )
            result.key_transforms.Add( DescribeTransform( transform ) );
        }

        result.rigid_body_count = result.rigid_bodies.Count;
        result.constraint_count = result.constraints.Count;
        result.key_transform_count = result.key_transforms.Count;
        result.success = true;
        result.message = $"Captured excavator pose snapshot with {result.rigid_body_count} rigid bodies and {result.constraint_count} constraints.";
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

    private static Scene EnsureTargetSceneIsAvailable( PoseSnapshotResult result )
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( EditorApplication.isPlayingOrWillChangePlaymode ) {
        result.message = $"Active scene is '{activeScene.path}', and the editor is in play mode; pose snapshot did not switch scenes.";
        return default;
      }

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        result.message = $"Active scene '{activeScene.path}' has unsaved changes; pose snapshot did not switch scenes.";
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var preferredBobcat = FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
                            FindSceneObject( "Excavator_BobcatE85" );
      if ( preferredBobcat != null )
        return preferredBobcat;

      foreach ( var component in Resources.FindObjectsOfTypeAll<Component>() ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        var type = component.GetType();
        var typeName = type.FullName ?? type.Name;
        if ( typeName != "AGXUnity.Excavator" && type.Name != "ExcavatorE85" )
          continue;

        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot( component.gameObject );
        if ( prefabRoot != null && prefabRoot.scene.IsValid() &&
             prefabRoot.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          return prefabRoot;

        var transform = component.transform;
        while ( transform.parent != null &&
                transform.parent.name.IndexOf( "ExperimentRig", StringComparison.OrdinalIgnoreCase ) < 0 )
          transform = transform.parent;

        return transform.gameObject;
      }

      return FindSceneObject( "Excavator CAT 365 Tracked" ) ??
             FindSceneObject( "Excavator" );
    }

    private static RigidBodySample DescribeRigidBody( RigidBody rigidBody )
    {
      return new RigidBodySample {
        name = rigidBody.name,
        path = GetHierarchyPath( rigidBody.gameObject ),
        transform = DescribeTransform( rigidBody.transform ),
        motion_control = rigidBody.MotionControl.ToString()
      };
    }

    private static ConstraintSample DescribeConstraint( Constraint constraint )
    {
      var sample = new ConstraintSample {
        name = constraint.name,
        path = GetHierarchyPath( constraint.gameObject ),
        type = constraint.Type.ToString(),
        transform = DescribeTransform( constraint.transform )
      };

      var pair = constraint.AttachmentPair;
      if ( pair == null )
        return sample;

      sample.synchronized = pair.Synchronized;
      sample.reference_object = pair.ReferenceObject != null ? GetHierarchyPath( pair.ReferenceObject ) : "<world>";
      sample.connected_object = pair.ConnectedObject != null ? GetHierarchyPath( pair.ConnectedObject ) : "<world>";
      sample.reference_body = pair.ReferenceBody != null ? pair.ReferenceBody.name : "<none>";
      sample.connected_body = pair.ConnectedBody != null ? pair.ConnectedBody.name : "<world>";
      sample.reference_frame = DescribeFrame( pair.ReferenceFrame );
      sample.connected_frame = DescribeFrame( pair.ConnectedFrame );
      sample.frame_position_distance_m = FormatFloat( Vector3.Distance( pair.ReferenceFrame.Position, pair.ConnectedFrame.Position ) );
      sample.frame_axis_angle_deg = FormatFloat( Vector3.Angle( pair.ReferenceFrame.Rotation * Vector3.forward,
                                                                pair.ConnectedFrame.Rotation * Vector3.forward ) );

      try {
        sample.current_angle = FormatFloat( constraint.GetCurrentAngle() );
      }
      catch ( System.Exception exception ) {
        sample.current_angle = "unavailable: " + exception.GetType().Name;
      }

      try {
        sample.current_speed = FormatFloat( constraint.GetCurrentSpeed() );
      }
      catch ( System.Exception exception ) {
        sample.current_speed = "unavailable: " + exception.GetType().Name;
      }

      foreach ( var controller in constraint.GetElementaryConstraintControllers() ) {
        if ( controller != null )
          sample.controllers.Add( DescribeController( controller ) );
      }

      return sample;
    }

    private static ConstraintControllerSample DescribeController( ElementaryConstraintController controller )
    {
      var sample = new ConstraintControllerSample {
        type = controller.GetType().Name,
        controller_type = controller.GetControllerType().ToString()
      };

      if ( controller is LockController lockController )
        sample.value = "position=" + FormatFloat( lockController.Position );
      else if ( controller is RangeController rangeController )
        sample.value = "range=(" + FormatFloat( rangeController.Range.Min ) + ", " + FormatFloat( rangeController.Range.Max ) + ")";
      else if ( controller is TargetSpeedController speedController )
        sample.value = "speed=" + FormatFloat( speedController.Speed );
      else if ( controller is ElectricMotorController motorController )
        sample.value = "voltage=" + FormatFloat( motorController.Voltage ) +
                       "; armature_resistance=" + FormatFloat( motorController.ArmatureResistance ) +
                       "; torque_constant=" + FormatFloat( motorController.TorqueConstant );
      else if ( controller is FrictionController frictionController )
        sample.value = "friction_coefficient=" + FormatFloat( frictionController.FrictionCoefficient ) +
                       "; non_linear_direct_solve_enabled=" + FormatBool( frictionController.NonLinearDirectSolveEnabled ) +
                       "; minimum_static_friction_force_range=(" +
                       FormatFloat( frictionController.MinimumStaticFrictionForceRange.Min ) + ", " +
                       FormatFloat( frictionController.MinimumStaticFrictionForceRange.Max ) + ")";
      else if ( controller is ScrewController screwController )
        sample.value = "lead=" + FormatFloat( screwController.Lead );

      return sample;
    }

    private static ConstraintFrameSample DescribeFrame( ConstraintFrame frame )
    {
      if ( frame == null )
        return null;

      return new ConstraintFrameSample {
        local_position = FormatVector( frame.LocalPosition ),
        local_euler = FormatVector( frame.LocalRotation.eulerAngles ),
        world_position = FormatVector( frame.Position ),
        world_euler = FormatVector( frame.Rotation.eulerAngles )
      };
    }

    private static TransformSample DescribeTransform( Transform transform )
    {
      return new TransformSample {
        name = transform.name,
        path = GetHierarchyPath( transform.gameObject ),
        world_position = FormatVector( transform.position ),
        world_euler = FormatVector( transform.eulerAngles ),
        local_position = FormatVector( transform.localPosition ),
        local_euler = FormatVector( transform.localEulerAngles ),
        local_scale = FormatVector( transform.localScale ),
        lossy_scale = FormatVector( transform.lossyScale )
      };
    }

    private static bool IsKeyTransform( Transform transform )
    {
      var path = GetHierarchyPath( transform.gameObject );
      return path.IndexOf( "Chassie", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Cabin", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "UnderCarriage", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Boom", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Arm", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Stick", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Bucket", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Track", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Hinge", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "Prismatic", StringComparison.OrdinalIgnoreCase ) >= 0;
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

    private static void FillBounds( Bounds? source, BoundsSample target )
    {
      if ( !source.HasValue )
        return;

      target.center = FormatVector( source.Value.center );
      target.size = FormatVector( source.Value.size );
      target.min = FormatVector( source.Value.min );
      target.max = FormatVector( source.Value.max );
    }

    private static GameObject FindSceneObject( string objectName )
    {
      foreach ( var gameObject in Resources.FindObjectsOfTypeAll<GameObject>() ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == objectName )
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
      return string.Format( CultureInfo.InvariantCulture,
                            "({0:0.###}, {1:0.###}, {2:0.###})",
                            value.x,
                            value.y,
                            value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static string FormatBool( bool value )
    {
      return value ? "true" : "false";
    }

    private static void WriteResult( PoseSnapshotResult result )
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
    private sealed class PoseSnapshotResult
    {
      public bool success;
      public string message;
      public string source;
      public bool unity_is_playing;
      public string realtime_since_startup_s;
      public string scene_path;
      public string excavator_root;
      public TransformSample root_transform;
      public BoundsSample overall_bounds = new BoundsSample();
      public BoundsSample bucket_bounds = new BoundsSample();
      public int rigid_body_count;
      public int constraint_count;
      public int key_transform_count;
      public List<RigidBodySample> rigid_bodies = new List<RigidBodySample>();
      public List<ConstraintSample> constraints = new List<ConstraintSample>();
      public List<TransformSample> key_transforms = new List<TransformSample>();
    }

    [Serializable]
    private sealed class TransformSample
    {
      public string name;
      public string path;
      public string world_position;
      public string world_euler;
      public string local_position;
      public string local_euler;
      public string local_scale;
      public string lossy_scale;
    }

    [Serializable]
    private sealed class RigidBodySample
    {
      public string name;
      public string path;
      public string motion_control;
      public TransformSample transform;
    }

    [Serializable]
    private sealed class ConstraintSample
    {
      public string name;
      public string path;
      public string type;
      public bool synchronized;
      public string reference_object;
      public string connected_object;
      public string reference_body;
      public string connected_body;
      public string frame_position_distance_m;
      public string frame_axis_angle_deg;
      public string current_angle;
      public string current_speed;
      public TransformSample transform;
      public ConstraintFrameSample reference_frame;
      public ConstraintFrameSample connected_frame;
      public List<ConstraintControllerSample> controllers = new List<ConstraintControllerSample>();
    }

    [Serializable]
    private sealed class ConstraintFrameSample
    {
      public string local_position;
      public string local_euler;
      public string world_position;
      public string world_euler;
    }

    [Serializable]
    private sealed class ConstraintControllerSample
    {
      public string type;
      public string controller_type;
      public string value;
    }

    [Serializable]
    private sealed class BoundsSample
    {
      public string center;
      public string size;
      public string min;
      public string max;
    }
  }
}
#endif
