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
  public static class CodexExcavatorScaleOnlyUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorScaleOnly.request";
    private const string OutputDirectory = "Temp/CodexExcavatorScaleOnly";
    private const float RequestedScale = 0.8f;
    private const float AlreadyScaledTrackLengthThreshold = 2.4f;
    private const float TargetTrackMinY = 0.05f;

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorScaleOnlyUtility()
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
        WriteResult( new ScaleOnlyResult { success = false, message = "Could not delete request file: " + exception.Message } );
        return;
      }

      RunScaleOnly();
    }

    private static void RunScaleOnly()
    {
      s_isRunning = true;
      var result = new ScaleOnlyResult();

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

        var beforeRenderers = CollectMeasuredRenderers( root );
        FillBounds( CalculateBounds( beforeRenderers ), result.before_overall );
        var beforeTracks = CalculateTrackBounds( beforeRenderers );
        FillBounds( beforeTracks, result.before_tracks );
        FillBounds( CalculateBucketBounds( beforeRenderers ), result.before_bucket );

        if ( beforeTracks.HasValue && beforeTracks.Value.size.x < AlreadyScaledTrackLengthThreshold ) {
          result.scaling_skipped = true;
        }
        else {
          ApplyBakedScale( root, RequestedScale, result );
          result.applied_scale = FormatFloat( RequestedScale );
        }

        AlignTrackBottom( root, result );
        root.transform.localScale = Vector3.one;
        EditorUtility.SetDirty( root.transform );

        var afterRenderers = CollectMeasuredRenderers( root );
        FillBounds( CalculateBounds( afterRenderers ), result.after_overall );
        FillBounds( CalculateTrackBounds( afterRenderers ), result.after_tracks );
        FillBounds( CalculateBucketBounds( afterRenderers ), result.after_bucket );
        result.root_position_m = FormatVector( root.transform.position );
        result.root_lossy_scale = FormatVector( root.transform.lossyScale );

        EditorSceneManager.MarkSceneDirty( scene );
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene( scene );

        result.success = true;
        result.message = result.scaling_skipped ? "Excavator already appeared scaled; only track alignment was applied." :
                                                  "Excavator was scale-baked to 80% without pose changes.";
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

    private static void ApplyBakedScale( GameObject root, float scale, ScaleOnlyResult result )
    {
      foreach ( var transform in root.GetComponentsInChildren<Transform>( true ) ) {
        if ( transform == root.transform )
          continue;

        Undo.RecordObject( transform, "Scale-bake excavator transform" );
        transform.localPosition *= scale;
        if ( ShouldScaleTransformLocalScale( transform ) )
          transform.localScale *= scale;
        EditorUtility.SetDirty( transform );
        result.scaled_transform_positions++;
      }

      foreach ( var shape in root.GetComponentsInChildren<Shape>( true ) ) {
        if ( ScaleShapeDimensions( shape, scale ) ) {
          ScalePrimitiveRenderDataVisual( shape, scale );
          EditorUtility.SetDirty( shape );
          result.scaled_shapes++;
        }
      }

      foreach ( var track in root.GetComponentsInChildren<Track>( true ) ) {
        Undo.RecordObject( track, "Scale-bake excavator track" );
        track.Width *= scale;
        track.Thickness *= scale;
        track.InitialTensionDistance *= scale;
        EditorUtility.SetDirty( track );
        result.scaled_tracks++;
      }

      foreach ( var wheel in root.GetComponentsInChildren<TrackWheel>( true ) ) {
        Undo.RecordObject( wheel, "Scale-bake excavator track wheel" );
        wheel.Radius *= scale;
        EditorUtility.SetDirty( wheel );
        result.scaled_track_wheels++;
      }

      var massScale = scale * scale * scale;
      var inertiaScale = massScale * scale * scale;
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        ScaleMassProperties( rigidBody, scale, massScale, inertiaScale );
        EditorUtility.SetDirty( rigidBody );
        result.scaled_rigid_bodies++;
      }

      foreach ( var component in root.GetComponentsInChildren<Component>( true ) ) {
        if ( component == null || component is Transform )
          continue;

        result.scaled_serialized_local_positions += ScaleSerializedLocalPositionVectors( component, scale );
      }

      foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) )
        ScaleConstraintControllers( constraint, scale, result );
    }

    private static bool ShouldScaleTransformLocalScale( Transform transform )
    {
      if ( transform.GetComponent<AGXUnity.Collide.Mesh>() != null )
        return true;
      if ( transform.GetComponent<Renderer>() == null )
        return false;
      return transform.GetComponentInParent<Shape>() == null;
    }

    private static bool ScaleShapeDimensions( Shape shape, float scale )
    {
      if ( shape == null )
        return false;

      Undo.RecordObject( shape, "Scale-bake excavator shape" );
      if ( shape is Box box ) {
        box.HalfExtents *= scale;
        return true;
      }
      if ( shape is Sphere sphere ) {
        sphere.Radius *= scale;
        return true;
      }
      if ( shape is Cylinder cylinder ) {
        cylinder.Radius *= scale;
        cylinder.Height *= scale;
        return true;
      }
      if ( shape is Capsule capsule ) {
        capsule.Radius *= scale;
        capsule.Height *= scale;
        return true;
      }
      if ( shape is Cone cone ) {
        cone.TopRadius *= scale;
        cone.BottomRadius *= scale;
        cone.Height *= scale;
        return true;
      }
      if ( shape is HollowCylinder hollowCylinder ) {
        hollowCylinder.Thickness *= scale;
        hollowCylinder.Radius *= scale;
        hollowCylinder.Height *= scale;
        return true;
      }
      if ( shape is HollowCone hollowCone ) {
        hollowCone.TopRadius *= scale;
        hollowCone.BottomRadius *= scale;
        hollowCone.Thickness *= scale;
        hollowCone.Height *= scale;
        return true;
      }
      return false;
    }

    private static void ScalePrimitiveRenderDataVisual( Shape shape, float scale )
    {
      if ( shape == null || shape is AGXUnity.Collide.Mesh )
        return;

      foreach ( var visual in shape.GetComponentsInChildren<AGXUnity.Rendering.ShapeVisualRenderData>( true ) ) {
        if ( visual == null || visual.Shape != shape )
          continue;

        Undo.RecordObject( visual.transform, "Scale primitive render data visual" );
        visual.transform.localScale *= scale;
        EditorUtility.SetDirty( visual.transform );
      }
    }

    private static void ScaleMassProperties( RigidBody rigidBody, float lengthScale, float massScale, float inertiaScale )
    {
      if ( rigidBody == null || rigidBody.MassProperties == null )
        return;

      var properties = rigidBody.MassProperties;
      properties.Mass.DefaultValue *= massScale;
      properties.Mass.UserValue *= massScale;
      properties.InertiaDiagonal.DefaultValue *= inertiaScale;
      properties.InertiaDiagonal.UserValue *= inertiaScale;
      properties.InertiaOffDiagonal.DefaultValue *= inertiaScale;
      properties.InertiaOffDiagonal.UserValue *= inertiaScale;
      properties.CenterOfMassOffset.DefaultValue *= lengthScale;
      properties.CenterOfMassOffset.UserValue *= lengthScale;
    }

    private static int ScaleSerializedLocalPositionVectors( Component component, float scale )
    {
      var serializedObject = new SerializedObject( component );
      var iterator = serializedObject.GetIterator();
      var changed = 0;

      while ( iterator.Next( true ) ) {
        if ( iterator.propertyType != SerializedPropertyType.Vector3 )
          continue;
        if ( iterator.propertyPath.IndexOf( "m_localPosition", StringComparison.OrdinalIgnoreCase ) < 0 )
          continue;

        iterator.vector3Value *= scale;
        changed++;
      }

      if ( changed > 0 ) {
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty( component );
      }
      return changed;
    }

    private static void ScaleConstraintControllers( Constraint constraint, float scale, ScaleOnlyResult result )
    {
      if ( constraint == null )
        return;

      var isLinearPrimaryConstraint = constraint.Type == ConstraintType.Prismatic || constraint.Type == ConstraintType.DistanceJoint;
      foreach ( var controller in constraint.GetElementaryConstraintControllers() ) {
        if ( controller == null )
          continue;

        var isTranslationalController = isLinearPrimaryConstraint ||
                                        controller.GetControllerType() == Constraint.ControllerType.Translational;
        if ( !isTranslationalController )
          continue;

        Undo.RecordObject( constraint, "Scale excavator constraint controller" );
        if ( controller is LockController lockController ) {
          lockController.Position = ScaleFinite( lockController.Position, scale );
          result.scaled_constraint_controllers++;
        }
        else if ( controller is RangeController rangeController ) {
          rangeController.Range = new RangeReal( ScaleFinite( rangeController.Range.Min, scale ),
                                                 ScaleFinite( rangeController.Range.Max, scale ) );
          result.scaled_constraint_controllers++;
        }
        else if ( controller is TargetSpeedController speedController ) {
          speedController.Speed = ScaleFinite( speedController.Speed, scale );
          result.scaled_constraint_controllers++;
        }
      }
      EditorUtility.SetDirty( constraint );
    }

    private static void AlignTrackBottom( GameObject root, ScaleOnlyResult result )
    {
      var trackBounds = CalculateTrackBounds( CollectMeasuredRenderers( root ) );
      if ( !trackBounds.HasValue )
        return;

      var deltaY = TargetTrackMinY - trackBounds.Value.min.y;
      if ( Mathf.Abs( deltaY ) < 0.001f )
        return;

      Undo.RecordObject( root.transform, "Align excavator track bottom" );
      root.transform.position += new Vector3( 0.0f, deltaY, 0.0f );
      EditorUtility.SetDirty( root.transform );
      result.track_alignment_delta_y_m = FormatFloat( deltaY );
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
                                     "AGXUnity_Excavator_before_e85_scale_only_" +
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

    private static Bounds? CalculateTrackBounds( List<Renderer> renderers )
    {
      var tracks = new List<Renderer>();
      foreach ( var renderer in renderers ) {
        var path = GetHierarchyPath( renderer.gameObject );
        if ( path.IndexOf( "track", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "undercarriage", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "roller", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "sprocket", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             path.IndexOf( "idler", StringComparison.OrdinalIgnoreCase ) >= 0 )
          tracks.Add( renderer );
      }
      return CalculateBounds( tracks );
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

    private static float ScaleFinite( float value, float scale )
    {
      return float.IsNaN( value ) || float.IsInfinity( value ) ? value : value * scale;
    }

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( ScaleOnlyResult result )
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
    private sealed class ScaleOnlyResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public string scene_backup_path;
      public string applied_scale;
      public bool scaling_skipped;
      public string root_position_m;
      public string root_lossy_scale;
      public string track_alignment_delta_y_m;
      public int scaled_transform_positions;
      public int scaled_shapes;
      public int scaled_tracks;
      public int scaled_track_wheels;
      public int scaled_rigid_bodies;
      public int scaled_serialized_local_positions;
      public int scaled_constraint_controllers;
      public BoundsSample before_overall = new BoundsSample();
      public BoundsSample before_tracks = new BoundsSample();
      public BoundsSample before_bucket = new BoundsSample();
      public BoundsSample after_overall = new BoundsSample();
      public BoundsSample after_tracks = new BoundsSample();
      public BoundsSample after_bucket = new BoundsSample();
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
