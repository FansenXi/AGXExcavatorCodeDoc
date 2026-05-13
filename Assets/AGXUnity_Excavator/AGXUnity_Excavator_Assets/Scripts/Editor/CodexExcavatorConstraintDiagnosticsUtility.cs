#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
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
  public static class CodexExcavatorConstraintDiagnosticsUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorConstraintDiagnostics.request";
    private const string OutputDirectory = "Temp/CodexExcavatorConstraintDiagnostics";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorConstraintDiagnosticsUtility()
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
        WriteResult( new DiagnosticsResult { success = false, message = "Could not delete request file: " + exception.Message } );
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

        result.success = true;
        result.excavator_root = GetHierarchyPath( root );
        result.root_position_m = FormatVector( root.transform.position );
        result.root_lossy_scale = FormatVector( root.transform.lossyScale );

        var renderers = CollectMeasuredRenderers( root );
        FillBounds( CalculateBounds( renderers ), result.overall_bounds );
        FillBounds( CalculateBucketBounds( renderers ), result.bucket_render_bounds );

        foreach ( var shovel in root.GetComponentsInChildren<DeformableTerrainShovel>( true ) )
          result.shovels.Add( CreateShovelSample( shovel ) );

        foreach ( var constraint in root.GetComponentsInChildren<Constraint>( true ) ) {
          var sample = CreateConstraintSample( constraint );
          result.constraints.Add( sample );
          if ( sample.frame_position_distance_m > result.max_frame_position_error_m )
            result.max_frame_position_error_m = sample.frame_position_distance_m;
          if ( sample.frame_axis_angle_deg > result.max_frame_axis_error_deg )
            result.max_frame_axis_error_deg = sample.frame_axis_angle_deg;
        }

        result.constraint_count = result.constraints.Count;
        result.message = "Constraint diagnostics completed.";
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

    private static ConstraintSample CreateConstraintSample( Constraint constraint )
    {
      var pair = constraint.AttachmentPair;
      var referencePosition = pair.ReferenceFrame.Position;
      var connectedPosition = pair.ConnectedFrame.Position;
      var referenceRotation = pair.ReferenceFrame.Rotation;
      var connectedRotation = pair.ConnectedFrame.Rotation;
      var referenceForward = referenceRotation * Vector3.forward;
      var connectedForward = connectedRotation * Vector3.forward;

      var referenceBody = pair.ReferenceBody;
      var connectedBody = pair.ConnectedBody;

      return new ConstraintSample {
        name = constraint.name,
        path = GetHierarchyPath( constraint.gameObject ),
        type = constraint.Type.ToString(),
        synchronized = pair.Synchronized,
        reference_object = pair.ReferenceObject != null ? GetHierarchyPath( pair.ReferenceObject ) : "<world>",
        connected_object = pair.ConnectedObject != null ? GetHierarchyPath( pair.ConnectedObject ) : "<world>",
        reference_body = referenceBody != null ? referenceBody.name : "<none>",
        connected_body = connectedBody != null ? connectedBody.name : "<world>",
        reference_position_m = FormatVector( referencePosition ),
        connected_position_m = FormatVector( connectedPosition ),
        reference_local_position_m = FormatVector( pair.ReferenceFrame.LocalPosition ),
        connected_local_position_m = FormatVector( pair.ConnectedFrame.LocalPosition ),
        reference_axis = FormatVector( referenceForward ),
        connected_axis = FormatVector( connectedForward ),
        frame_position_distance_m = Vector3.Distance( referencePosition, connectedPosition ),
        frame_axis_angle_deg = Vector3.Angle( referenceForward, connectedForward )
      };
    }

    private static ShovelSample CreateShovelSample( DeformableTerrainShovel shovel )
    {
      var points = new List<Vector3>();
      AddLinePoints( shovel.TopEdge, points );
      AddLinePoints( shovel.CuttingEdge, points );
      AddLinePoints( shovel.ToothDirection, points );

      var minY = float.PositiveInfinity;
      var maxY = float.NegativeInfinity;
      foreach ( var point in points ) {
        minY = Mathf.Min( minY, point.y );
        maxY = Mathf.Max( maxY, point.y );
      }

      return new ShovelSample {
        name = shovel.name,
        path = GetHierarchyPath( shovel.gameObject ),
        point_count = points.Count,
        min_y_m = float.IsPositiveInfinity( minY ) ? "" : FormatFloat( minY ),
        max_y_m = float.IsNegativeInfinity( maxY ) ? "" : FormatFloat( maxY ),
        points_m = FormatPointList( points )
      };
    }

    private static void AddLinePoints( Line line, List<Vector3> points )
    {
      if ( line == null )
        return;

      if ( line.Start != null )
        points.Add( line.Start.Position );
      if ( line.End != null )
        points.Add( line.End.Position );
    }

    private static string FormatPointList( List<Vector3> points )
    {
      var values = new List<string>();
      foreach ( var point in points )
        values.Add( FormatVector( point ) );
      return string.Join( "; ", values );
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

    private static void FillBounds( Bounds? bounds, BoundsSample sample )
    {
      if ( !bounds.HasValue )
        return;
      sample.min_m = FormatVector( bounds.Value.min );
      sample.max_m = FormatVector( bounds.Value.max );
      sample.center_m = FormatVector( bounds.Value.center );
      sample.size_m = FormatVector( bounds.Value.size );
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

    private static void WriteResult( DiagnosticsResult result )
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
    private sealed class DiagnosticsResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public string root_position_m;
      public string root_lossy_scale;
      public int constraint_count;
      public float max_frame_position_error_m;
      public float max_frame_axis_error_deg;
      public BoundsSample overall_bounds = new BoundsSample();
      public BoundsSample bucket_render_bounds = new BoundsSample();
      public List<ShovelSample> shovels = new List<ShovelSample>();
      public List<ConstraintSample> constraints = new List<ConstraintSample>();
    }

    [Serializable]
    private sealed class BoundsSample
    {
      public string min_m;
      public string max_m;
      public string center_m;
      public string size_m;
    }

    [Serializable]
    private sealed class ShovelSample
    {
      public string name;
      public string path;
      public int point_count;
      public string min_y_m;
      public string max_y_m;
      public string points_m;
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
      public string reference_position_m;
      public string connected_position_m;
      public string reference_local_position_m;
      public string connected_local_position_m;
      public string reference_axis;
      public string connected_axis;
      public float frame_position_distance_m;
      public float frame_axis_angle_deg;
    }
  }
}
#endif
