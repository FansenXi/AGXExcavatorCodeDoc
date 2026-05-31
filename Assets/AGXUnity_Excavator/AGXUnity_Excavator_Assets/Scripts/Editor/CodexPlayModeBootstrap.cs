#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexPlayModeBootstrap
  {
    private const string ScenePath = CodexSceneScaleConfig.MainScenePath;
    private const string RequestPath = "Temp/CodexPlayModeBootstrap.request";
    private const string DefaultStatusPath = "Temp/CodexPlayModeBootstrap/status.json";
    private const string RunningKey = "CodexPlayModeBootstrap.running";
    private const string PhaseKey = "CodexPlayModeBootstrap.phase";
    private const string StartedAtKey = "CodexPlayModeBootstrap.started_at";
    private const string TimeoutSecKey = "CodexPlayModeBootstrap.timeout_sec";
    private const string StatusPathKey = "CodexPlayModeBootstrap.status_path";

    private const double DefaultTimeoutSeconds = 300.0;
    private static double s_nextRequestPollTime;

    static CodexPlayModeBootstrap()
    {
      EditorApplication.update -= Tick;
      EditorApplication.update += Tick;
    }

    [MenuItem( "Tools/AGX Excavator/Codex/Restart Play Mode For Step-Ack Smoke" )]
    public static void RunFromMenu()
    {
      StartRun( new BootstrapRequest
      {
        status_path = DefaultStatusPath,
        timeout_sec = DefaultTimeoutSeconds
      }, "menu" );
    }

    public static void RunFromCommandLine()
    {
      StartRun( ParseCommandLineRequest(), "execute-method" );
    }

    private static void Tick()
    {
      if ( IsRunning() ) {
        AdvanceRun();
        return;
      }

      PollForRequestFile();
    }

    private static void PollForRequestFile()
    {
      if ( EditorApplication.timeSinceStartup < s_nextRequestPollTime )
        return;

      s_nextRequestPollTime = EditorApplication.timeSinceStartup + 0.5;

      var requestPath = ProjectRelativeToAbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      BootstrapRequest request;
      try {
        var raw = File.ReadAllText( requestPath );
        request = string.IsNullOrWhiteSpace( raw ) ?
                  new BootstrapRequest() :
                  JsonUtility.FromJson<BootstrapRequest>( raw );
        File.Delete( requestPath );
      }
      catch ( Exception exception ) {
        WriteStatus( DefaultStatusPath,
                     false,
                     "request",
                     $"Could not read request file: {exception.Message}",
                     0.0,
                     true );
        return;
      }

      StartRun( request ?? new BootstrapRequest(), "request-file" );
    }

    private static void StartRun( BootstrapRequest request, string source )
    {
      var now = EditorApplication.timeSinceStartup;
      var statusPath = string.IsNullOrWhiteSpace( request.status_path ) ?
                       DefaultStatusPath :
                       request.status_path;
      var timeoutSec = request.timeout_sec > 0.0 ? request.timeout_sec : DefaultTimeoutSeconds;

      SetString( StatusPathKey, statusPath );
      SetString( TimeoutSecKey, timeoutSec.ToString( "R", CultureInfo.InvariantCulture ) );
      SetString( StartedAtKey, now.ToString( "R", CultureInfo.InvariantCulture ) );
      SetBool( RunningKey, true );
      SetPhase( "start" );

      WriteStatus( statusPath,
                   false,
                   "start",
                   $"Unity Play Mode bootstrap requested from {source}.",
                   0.0,
                   false );
      AdvanceRun();
    }

    private static void AdvanceRun()
    {
      var statusPath = GetString( StatusPathKey, DefaultStatusPath );
      var startedAt = GetDouble( StartedAtKey, EditorApplication.timeSinceStartup );
      var elapsed = EditorApplication.timeSinceStartup - startedAt;
      var timeoutSec = GetDouble( TimeoutSecKey, DefaultTimeoutSeconds );
      var phase = GetString( PhaseKey, "start" );

      if ( elapsed > timeoutSec ) {
        Finish( false,
                phase,
                $"Timed out after {elapsed:0.0}s while waiting for phase '{phase}'.",
                elapsed );
        return;
      }

      try {
        switch ( phase ) {
          case "start":
            AssetDatabase.Refresh();
            if ( EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ) {
              EditorApplication.isPlaying = false;
              SetPhase( "wait_edit_mode" );
              WriteStatus( statusPath, false, "wait_edit_mode", "Exiting Play Mode.", elapsed, false );
              return;
            }
            SetPhase( "wait_compile" );
            return;

          case "wait_edit_mode":
            if ( EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode )
              return;
            SetPhase( "wait_compile" );
            return;

          case "wait_compile":
            if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
              return;
            SaveOpenScenes();
            SetPhase( "open_scene" );
            return;

          case "open_scene":
            OpenMainScene();
            SaveOpenScenes();
            SetPhase( "enter_play_mode" );
            return;

          case "enter_play_mode":
            if ( !EditorApplication.isPlaying )
              EditorApplication.isPlaying = true;
            SetPhase( "wait_play_mode" );
            WriteStatus( statusPath, false, "wait_play_mode", "Entering Play Mode.", elapsed, false );
            return;

          case "wait_play_mode":
            if ( !EditorApplication.isPlaying )
              return;
            SetPhase( "wait_step_ack_server" );
            return;

          case "wait_step_ack_server":
            if ( TryFindListeningServer( out _ ) ) {
              Finish( true,
                      "done",
                      "Unity is in Play Mode and AgxSimStepAckServer is listening.",
                      elapsed );
            }
            return;

          default:
            Finish( false, phase, $"Unknown bootstrap phase '{phase}'.", elapsed );
            return;
        }
      }
      catch ( Exception exception ) {
        Finish( false, phase, exception.ToString(), elapsed );
      }
    }

    private static void Finish( bool success, string phase, string message, double elapsed )
    {
      WriteStatus( GetString( StatusPathKey, DefaultStatusPath ),
                   success,
                   phase,
                   message,
                   elapsed,
                   true );
      SetBool( RunningKey, false );
      SetString( PhaseKey, string.Empty );
    }

    private static void SaveOpenScenes()
    {
      EditorSceneManager.SaveOpenScenes();
      AssetDatabase.SaveAssets();
    }

    private static void OpenMainScene()
    {
      var activeScene = SceneManager.GetActiveScene();
      if ( activeScene.IsValid() && activeScene.path == ScenePath )
        return;

      EditorSceneManager.OpenScene( ScenePath, OpenSceneMode.Single );
    }

    private static bool TryFindListeningServer( out AgxSimStepAckServer server )
    {
      server = null;
      var servers = UnityEngine.Object.FindObjectsByType<AgxSimStepAckServer>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );
      if ( servers == null )
        return false;

      foreach ( var candidate in servers ) {
        if ( candidate == null )
          continue;
        if ( candidate.IsListening ) {
          server = candidate;
          return true;
        }
      }

      return false;
    }

    private static BootstrapRequest ParseCommandLineRequest()
    {
      var request = new BootstrapRequest
      {
        status_path = DefaultStatusPath,
        timeout_sec = DefaultTimeoutSeconds
      };
      var args = Environment.GetCommandLineArgs();
      for ( var index = 0; index < args.Length; ++index ) {
        var arg = args[ index ];
        if ( arg == "-codexStatusPath" && index + 1 < args.Length ) {
          request.status_path = args[ ++index ];
        }
        else if ( arg == "-codexTimeoutSec" && index + 1 < args.Length ) {
          if ( double.TryParse( args[ ++index ],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out var timeoutSec ) )
            request.timeout_sec = timeoutSec;
        }
      }
      return request;
    }

    private static void WriteStatus( string statusPath,
                                     bool success,
                                     string phase,
                                     string message,
                                     double elapsedSec,
                                     bool complete )
    {
      var status = new BootstrapStatus
      {
        success = success,
        phase = phase ?? string.Empty,
        message = message ?? string.Empty,
        complete = complete,
        elapsed_sec = elapsedSec,
        is_playing = EditorApplication.isPlaying,
        is_compiling = EditorApplication.isCompiling,
        active_scene = SceneManager.GetActiveScene().path ?? string.Empty,
        step_ack_listening = TryFindListeningServer( out _ )
      };

      var absolutePath = ProjectRelativeToAbsolutePath( statusPath );
      var parent = Path.GetDirectoryName( absolutePath );
      if ( !string.IsNullOrEmpty( parent ) )
        Directory.CreateDirectory( parent );
      File.WriteAllText( absolutePath, JsonUtility.ToJson( status, true ) );
    }

    private static string ProjectRelativeToAbsolutePath( string path )
    {
      if ( string.IsNullOrWhiteSpace( path ) )
        path = DefaultStatusPath;
      if ( Path.IsPathRooted( path ) )
        return path;
      return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), path ) );
    }

    private static bool IsRunning()
    {
      return SessionState.GetBool( RunningKey, false );
    }

    private static void SetPhase( string phase )
    {
      SetString( PhaseKey, phase );
    }

    private static string GetString( string key, string defaultValue )
    {
      var value = SessionState.GetString( key, defaultValue );
      return string.IsNullOrEmpty( value ) ? defaultValue : value;
    }

    private static void SetString( string key, string value )
    {
      SessionState.SetString( key, value ?? string.Empty );
    }

    private static void SetBool( string key, bool value )
    {
      SessionState.SetBool( key, value );
    }

    private static double GetDouble( string key, double defaultValue )
    {
      var value = SessionState.GetString( key, string.Empty );
      return double.TryParse( value,
                              NumberStyles.Float,
                              CultureInfo.InvariantCulture,
                              out var parsed ) ?
             parsed :
             defaultValue;
    }

    [Serializable]
    private sealed class BootstrapRequest
    {
      public string status_path = DefaultStatusPath;
      public double timeout_sec = DefaultTimeoutSeconds;
    }

    [Serializable]
    private sealed class BootstrapStatus
    {
      public bool success;
      public string phase = string.Empty;
      public string message = string.Empty;
      public bool complete;
      public double elapsed_sec;
      public bool is_playing;
      public bool is_compiling;
      public string active_scene = string.Empty;
      public bool step_ack_listening;
    }
  }
}
#endif
