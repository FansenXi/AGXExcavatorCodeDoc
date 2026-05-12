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
  public static class CodexExcavatorPlaySmokeTestUtility
  {
    private const string ScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";
    private const string RequestPath = "Temp/CodexExcavatorPlaySmokeTest.request";
    private const string OutputDirectory = "Temp/CodexExcavatorPlaySmokeTest";
    private const string SessionRunning = "CodexExcavatorPlaySmokeTest.Running";
    private const string SessionStartTime = "CodexExcavatorPlaySmokeTest.StartTime";
    private const string SessionBeforeRoot = "CodexExcavatorPlaySmokeTest.BeforeRoot";
    private const string SessionBeforeBounds = "CodexExcavatorPlaySmokeTest.BeforeBounds";
    private const float TestDurationSeconds = 3.0f;
    private const float MaxReasonableBoundsSize = 10.0f;
    private const float MaxReasonableDisplacement = 2.0f;

    private static double s_nextPollTime;
    private static bool s_isStarting;

    static CodexExcavatorPlaySmokeTestUtility()
    {
      EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
      if ( SessionState.GetBool( SessionRunning, false ) ) {
        ContinueRunningTest();
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

      try {
        File.Delete( requestPath );
      }
      catch ( System.Exception exception ) {
        WriteResult( new SmokeResult { success = false, message = "Could not delete request file: " + exception.Message } );
        return;
      }

      StartSmokeTest();
    }

    private static void StartSmokeTest()
    {
      s_isStarting = true;

      try {
        var scene = EnsureTargetSceneIsOpen();
        if ( !scene.IsValid() ) {
          WriteResult( new SmokeResult { success = false, message = "Target scene could not be opened." } );
          return;
        }

        var root = ResolveExcavatorRoot();
        if ( root == null ) {
          WriteResult( new SmokeResult { success = false, message = "Excavator root was not found." } );
          return;
        }

        var beforeBounds = CalculateBounds( CollectMeasuredRenderers( root ) );
        SessionState.SetBool( SessionRunning, true );
        SessionState.SetFloat( SessionStartTime, -1.0f );
        SessionState.SetString( SessionBeforeRoot, FormatVector( root.transform.position ) );
        SessionState.SetString( SessionBeforeBounds, beforeBounds.HasValue ? FormatBounds( beforeBounds.Value ) : string.Empty );

        EditorApplication.isPlaying = true;
      }
      catch ( System.Exception exception ) {
        WriteResult( new SmokeResult { success = false, message = exception.ToString() } );
      }
      finally {
        s_isStarting = false;
      }
    }

    private static void ContinueRunningTest()
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

      var elapsed = (float)EditorApplication.timeSinceStartup - startTime;
      if ( elapsed < TestDurationSeconds )
        return;

      var result = FinishSmokeTest( elapsed );
      WriteResult( result );
      SessionState.SetBool( SessionRunning, false );
      SessionState.EraseFloat( SessionStartTime );
      EditorApplication.isPlaying = false;
    }

    private static SmokeResult FinishSmokeTest( float elapsed )
    {
      var result = new SmokeResult {
        success = true,
        elapsed_seconds = FormatFloat( elapsed ),
        before_root_position_m = SessionState.GetString( SessionBeforeRoot, string.Empty ),
        before_bounds = SessionState.GetString( SessionBeforeBounds, string.Empty )
      };

      var root = ResolveExcavatorRoot();
      if ( root == null ) {
        result.success = false;
        result.message = "Excavator root disappeared during play mode.";
        return result;
      }

      result.excavator_root = GetHierarchyPath( root );
      result.after_root_position_m = FormatVector( root.transform.position );

      var renderers = CollectMeasuredRenderers( root );
      var bounds = CalculateBounds( renderers );
      if ( bounds.HasValue )
        result.after_bounds = FormatBounds( bounds.Value );

      var invalidRigidBodyTransforms = 0;
      foreach ( var rigidBody in root.GetComponentsInChildren<RigidBody>( true ) ) {
        if ( rigidBody == null )
          continue;

        if ( !IsFinite( rigidBody.transform.position ) ||
             !IsFinite( rigidBody.transform.rotation ) ||
             rigidBody.transform.position.magnitude > 100.0f )
          invalidRigidBodyTransforms++;
      }

      result.rigid_body_count = root.GetComponentsInChildren<RigidBody>( true ).Length;
      result.invalid_rigid_body_transforms = invalidRigidBodyTransforms;

      var beforeRoot = TryParseVector( result.before_root_position_m );
      var displacement = beforeRoot.HasValue ? Vector3.Distance( beforeRoot.Value, root.transform.position ) : 0.0f;
      result.root_displacement_m = FormatFloat( displacement );

      var boundsLooksReasonable = !bounds.HasValue ||
                                  ( IsFinite( bounds.Value.center ) &&
                                    IsFinite( bounds.Value.size ) &&
                                    bounds.Value.size.x < MaxReasonableBoundsSize &&
                                    bounds.Value.size.y < MaxReasonableBoundsSize &&
                                    bounds.Value.size.z < MaxReasonableBoundsSize &&
                                    bounds.Value.center.y > -2.0f &&
                                    bounds.Value.center.y < 6.0f );

      if ( invalidRigidBodyTransforms > 0 || !boundsLooksReasonable || displacement > MaxReasonableDisplacement ) {
        result.success = false;
        result.message = "Play smoke test detected unstable transforms or unreasonable bounds.";
      }
      else {
        result.message = "Play smoke test completed without obvious AGX startup instability.";
      }

      return result;
    }

    private static Scene EnsureTargetSceneIsOpen()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.path == ScenePath )
        return activeScene;

      if ( activeScene.IsValid() && activeScene.isDirty )
        return default;

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
        if ( prefabRoot != null && prefabRoot.scene.IsValid() )
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

    private static bool IsFinite( Vector3 value )
    {
      return float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
    }

    private static bool IsFinite( Quaternion value )
    {
      return float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z ) && float.IsFinite( value.w );
    }

    private static Vector3? TryParseVector( string value )
    {
      if ( string.IsNullOrEmpty( value ) )
        return null;

      var trimmed = value.Trim( '(', ')' );
      var parts = trimmed.Split( ',' );
      if ( parts.Length != 3 )
        return null;

      if ( float.TryParse( parts[ 0 ], NumberStyles.Float, CultureInfo.InvariantCulture, out var x ) &&
           float.TryParse( parts[ 1 ], NumberStyles.Float, CultureInfo.InvariantCulture, out var y ) &&
           float.TryParse( parts[ 2 ], NumberStyles.Float, CultureInfo.InvariantCulture, out var z ) )
        return new Vector3( x, y, z );

      return null;
    }

    private static string FormatBounds( Bounds bounds )
    {
      return "min " + FormatVector( bounds.min ) + ", max " + FormatVector( bounds.max ) + ", size " + FormatVector( bounds.size );
    }

    private static string FormatVector( Vector3 value )
    {
      return string.Format( CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", value.x, value.y, value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.###", CultureInfo.InvariantCulture );
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

    private static void WriteResult( SmokeResult result )
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
    private sealed class SmokeResult
    {
      public bool success;
      public string message;
      public string elapsed_seconds;
      public string excavator_root;
      public string before_root_position_m;
      public string after_root_position_m;
      public string root_displacement_m;
      public string before_bounds;
      public string after_bounds;
      public int rigid_body_count;
      public int invalid_rigid_body_transforms;
    }
  }
}
#endif
