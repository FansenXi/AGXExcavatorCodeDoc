#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexExcavatorDimensionUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorDimensions.request";
    private const string OutputDirectory = "Temp/CodexExcavatorDimensions";

    private static double s_nextPollTime;
    private static bool s_isRunning;

    static CodexExcavatorDimensionUtility()
    {
      EditorApplication.update += PollForRequest;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Measure Excavator Dimensions" )]
    public static void MeasureExcavatorDimensionsFromMenu()
    {
      MeasureExcavatorDimensions( "menu" );
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

      MeasureExcavatorDimensions( "request-file" );
    }

    private static void MeasureExcavatorDimensions( string source )
    {
      s_isRunning = true;
      var result = new ExcavatorDimensionResult();

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( false, "Target scene is not open and could not be opened safely.", result );
          return;
        }

        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          WriteResult( false, "Excavator root was not found.", result );
          return;
        }

        result.excavator_root = GetHierarchyPath( root );
        result.root_position_m = FormatVector( root.transform.position );
        result.root_euler_deg = FormatVector( root.transform.eulerAngles );
        result.root_lossy_scale = FormatVector( root.transform.lossyScale );

        var renderers = CollectMeasuredRenderers( root );
        result.renderer_count = renderers.Count;

        var overallBounds = CalculateBounds( renderers );
        if ( overallBounds.HasValue ) {
          FillBoundsResult( overallBounds.Value, result.overall );
          result.message = $"Measured {root.name} from {source}. Overall visual bounds: {result.overall.size_m}.";
        }

        var trackRenderers = CollectNamedTrackRenderers( renderers );
        result.named_track_renderer_count = trackRenderers.Count;
        var trackBounds = CalculateBounds( trackRenderers );
        if ( !trackBounds.HasValue && overallBounds.HasValue ) {
          trackRenderers = CollectLowerUndercarriageRenderers( renderers, overallBounds.Value );
          result.lower_undercarriage_renderer_count = trackRenderers.Count;
          trackBounds = CalculateBounds( trackRenderers );
        }

        if ( trackBounds.HasValue ) {
          FillBoundsResult( trackBounds.Value, result.track_assembly );
          FillSplitTrackBounds( trackRenderers, trackBounds.Value.center.z, result );
        }

        FillLargestRendererSamples( renderers, result );
        WriteResult( true, result.message, result );
      }
      catch ( Exception exception ) {
        result.message = exception.ToString();
        WriteResult( false, result.message, result );
      }
      finally {
        s_isRunning = false;
      }
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty ) {
        WriteResult( false,
                     $"Active scene '{activeScene.path}' has unsaved changes; dimension utility did not switch scenes.",
                     null );
        return default;
      }

      return EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static GameObject ResolveExcavatorRoot()
    {
      var components = Resources.FindObjectsOfTypeAll<Component>();
      foreach ( var component in components ) {
        if ( component == null || component.gameObject == null || !component.gameObject.scene.IsValid() )
          continue;

        if ( component.GetType().FullName != "AGXUnity.Excavator" )
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

      return FindSceneObject( "Excavator_BobcatE85 Variant" ) ??
             FindSceneObject( "Excavator_BobcatE85" ) ??
             FindSceneObject( "Excavator" );
    }

    private static List<Renderer> CollectMeasuredRenderers( GameObject root )
    {
      var result = new List<Renderer>();
      var renderers = root.GetComponentsInChildren<Renderer>( true );
      foreach ( var renderer in renderers ) {
        if ( renderer == null || renderer is ParticleSystemRenderer )
          continue;

        if ( !renderer.enabled || !renderer.gameObject.activeInHierarchy )
          continue;

        result.Add( renderer );
      }

      return result;
    }

    private static List<Renderer> CollectNamedTrackRenderers( List<Renderer> renderers )
    {
      var result = new List<Renderer>();
      foreach ( var renderer in renderers ) {
        var path = GetHierarchyPath( renderer.gameObject );
        if ( ContainsTrackKeyword( path ) )
          result.Add( renderer );
      }

      return result;
    }

    private static List<Renderer> CollectLowerUndercarriageRenderers( List<Renderer> renderers, Bounds overallBounds )
    {
      var result = new List<Renderer>();
      var cutoff = overallBounds.min.y + Mathf.Min( 0.85f, Mathf.Max( 0.35f, overallBounds.size.y * 0.32f ) );
      foreach ( var renderer in renderers ) {
        var bounds = renderer.bounds;
        if ( bounds.center.y <= cutoff && bounds.min.y <= cutoff )
          result.Add( renderer );
      }

      return result;
    }

    private static bool ContainsTrackKeyword( string value )
    {
      if ( string.IsNullOrEmpty( value ) )
        return false;

      return value.IndexOf( "track", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "crawler", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "tread", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "undercarriage", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "roller", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "sprocket", StringComparison.OrdinalIgnoreCase ) >= 0 ||
             value.IndexOf( "idler", StringComparison.OrdinalIgnoreCase ) >= 0;
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

    private static void FillSplitTrackBounds( List<Renderer> trackRenderers, float centerZ, ExcavatorDimensionResult result )
    {
      var left = new List<Renderer>();
      var right = new List<Renderer>();
      foreach ( var renderer in trackRenderers ) {
        if ( renderer.bounds.center.z < centerZ )
          left.Add( renderer );
        else
          right.Add( renderer );
      }

      var leftBounds = CalculateBounds( left );
      var rightBounds = CalculateBounds( right );
      if ( leftBounds.HasValue )
        FillBoundsResult( leftBounds.Value, result.track_negative_z_side );
      if ( rightBounds.HasValue )
        FillBoundsResult( rightBounds.Value, result.track_positive_z_side );
    }

    private static void FillBoundsResult( Bounds bounds, BoundsResult target )
    {
      target.min_m = FormatVector( bounds.min );
      target.max_m = FormatVector( bounds.max );
      target.center_m = FormatVector( bounds.center );
      target.size_m = FormatVector( bounds.size );
      target.length_x_m = FormatFloat( bounds.size.x );
      target.height_y_m = FormatFloat( bounds.size.y );
      target.width_z_m = FormatFloat( bounds.size.z );
      target.length_x_mm = FormatFloat( bounds.size.x * 1000.0f );
      target.height_y_mm = FormatFloat( bounds.size.y * 1000.0f );
      target.width_z_mm = FormatFloat( bounds.size.z * 1000.0f );
    }

    private static void FillLargestRendererSamples( List<Renderer> renderers, ExcavatorDimensionResult result )
    {
      renderers.Sort( ( left, right ) => right.bounds.size.sqrMagnitude.CompareTo( left.bounds.size.sqrMagnitude ) );
      var count = Mathf.Min( 12, renderers.Count );
      for ( var index = 0; index < count; ++index ) {
        var renderer = renderers[index];
        result.largest_renderers.Add( new RendererSample {
          path = GetHierarchyPath( renderer.gameObject ),
          size_m = FormatVector( renderer.bounds.size ),
          center_m = FormatVector( renderer.bounds.center )
        } );
      }
    }

    private static GameObject FindSceneObject( string name )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var gameObject in objects ) {
        if ( gameObject != null && gameObject.scene.IsValid() && gameObject.name == name )
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
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    private static void WriteResult( bool success, string message, ExcavatorDimensionResult result )
    {
      Directory.CreateDirectory( GetProjectRelativeAbsolutePath( OutputDirectory ) );
      if ( result == null )
        result = new ExcavatorDimensionResult();

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
    private sealed class ExcavatorDimensionResult
    {
      public bool success;
      public string message;
      public string excavator_root;
      public string root_position_m;
      public string root_euler_deg;
      public string root_lossy_scale;
      public int renderer_count;
      public int named_track_renderer_count;
      public int lower_undercarriage_renderer_count;
      public BoundsResult overall = new BoundsResult();
      public BoundsResult track_assembly = new BoundsResult();
      public BoundsResult track_negative_z_side = new BoundsResult();
      public BoundsResult track_positive_z_side = new BoundsResult();
      public List<RendererSample> largest_renderers = new List<RendererSample>();
    }

    [Serializable]
    private sealed class BoundsResult
    {
      public string min_m;
      public string max_m;
      public string center_m;
      public string size_m;
      public string length_x_m;
      public string width_z_m;
      public string height_y_m;
      public string length_x_mm;
      public string width_z_mm;
      public string height_y_mm;
    }

    [Serializable]
    private sealed class RendererSample
    {
      public string path;
      public string size_m;
      public string center_m;
    }
  }
}
#endif
