#if UNITY_EDITOR
using System;
using System.IO;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEditor;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [InitializeOnLoad]
  public static class CodexPlayModeBootstrap
  {
    private const string RequestPath = "Temp/CodexPlayModeBootstrap.request";
    private const string DefaultStatusPath = "Temp/CodexPlayModeBootstrap/status.json";
    private const string SessionPending = "CodexPlayModeBootstrap.Pending";
    private const string SessionStatusPath = "CodexPlayModeBootstrap.StatusPath";
    private const string SessionTimeoutSec = "CodexPlayModeBootstrap.TimeoutSec";
    private const string SessionStartTime = "CodexPlayModeBootstrap.StartTime";
    private const string SessionRequestedExit = "CodexPlayModeBootstrap.RequestedExit";

    private static double s_nextPollTime;

    static CodexPlayModeBootstrap()
    {
      EditorApplication.update += OnEditorUpdate;
    }

    public static void RunFromCommandLine()
    {
      StartRequest( ParseCommandLineRequest(), "execute-method" );
    }

    private static void OnEditorUpdate()
    {
      if ( SessionState.GetBool( SessionPending, false ) ) {
        ContinueRequest();
        return;
      }

      if ( EditorApplication.timeSinceStartup < s_nextPollTime )
        return;

      s_nextPollTime = EditorApplication.timeSinceStartup + 1.0;

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating )
        return;

      var requestPath = AbsolutePath( RequestPath );
      if ( !File.Exists( requestPath ) )
        return;

      BootstrapRequest request;
      try {
        request = JsonUtility.FromJson<BootstrapRequest>( File.ReadAllText( requestPath ) ) ??
                  new BootstrapRequest();
        File.Delete( requestPath );
      }
      catch ( Exception exception ) {
        WriteStatus( AbsolutePath( DefaultStatusPath ),
                     false,
                     true,
                     "request_error",
                     "Could not read bootstrap request: " + exception.Message );
        return;
      }

      StartRequest( request, "request-file" );
    }

    private static void StartRequest( BootstrapRequest request, string source )
    {
      request = request ?? new BootstrapRequest();
      var statusPath = string.IsNullOrWhiteSpace( request.status_path ) ?
                       AbsolutePath( DefaultStatusPath ) :
                       request.status_path;
      var timeoutSec = request.timeout_sec > 0.0f ? request.timeout_sec : 120.0f;

      SessionState.SetBool( SessionPending, true );
      SessionState.SetString( SessionStatusPath, statusPath );
      SessionState.SetFloat( SessionTimeoutSec, timeoutSec );
      SessionState.SetFloat( SessionStartTime, (float)EditorApplication.timeSinceStartup );
      SessionState.SetBool( SessionRequestedExit, false );
      WriteStatus( statusPath, true, false, "received", source );
      AssetDatabase.Refresh();
      ContinueRequest();
    }

    private static void ContinueRequest()
    {
      var statusPath = SessionState.GetString( SessionStatusPath, AbsolutePath( DefaultStatusPath ) );
      var timeoutSec = SessionState.GetFloat( SessionTimeoutSec, 120.0f );
      var startTime = SessionState.GetFloat( SessionStartTime, (float)EditorApplication.timeSinceStartup );
      var elapsed = (float)EditorApplication.timeSinceStartup - startTime;

      if ( elapsed > timeoutSec ) {
        Finish( statusPath, false, "timeout", $"Timed out after {elapsed:0.0}s." );
        return;
      }

      if ( EditorApplication.isCompiling || EditorApplication.isUpdating ) {
        WriteStatus( statusPath, true, false, "compiling", "Waiting for Unity compilation/import." );
        return;
      }

      if ( EditorApplication.isPlaying && !SessionState.GetBool( SessionRequestedExit, false ) ) {
        SessionState.SetBool( SessionRequestedExit, true );
        WriteStatus( statusPath, true, false, "exiting_play_mode", "Leaving Play Mode before restart." );
        EditorApplication.isPlaying = false;
        return;
      }

      if ( !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode ) {
        WriteStatus( statusPath, true, false, "entering_play_mode", "Entering Play Mode." );
        EditorApplication.isPlaying = true;
        return;
      }

      if ( !EditorApplication.isPlaying ) {
        WriteStatus( statusPath, true, false, "play_mode_transition", "Waiting for Play Mode transition." );
        return;
      }

      var server = UnityEngine.Object.FindObjectOfType<AgxSimStepAckServer>( true );
      if ( server != null && server.IsListening ) {
        Finish( statusPath, true, "ready", "Play Mode is running and step-ack server is listening." );
        return;
      }

      WriteStatus( statusPath, true, false, "waiting_for_server", "Waiting for step-ack server." );
    }

    private static BootstrapRequest ParseCommandLineRequest()
    {
      var request = new BootstrapRequest();
      var args = Environment.GetCommandLineArgs();
      for ( var index = 0; index < args.Length; ++index ) {
        if ( args[ index ] == "-codexStatusPath" && index + 1 < args.Length )
          request.status_path = args[ ++index ];
        else if ( args[ index ] == "-codexTimeoutSec" && index + 1 < args.Length &&
                  float.TryParse( args[ ++index ], out var timeout ) )
          request.timeout_sec = timeout;
      }

      return request;
    }

    private static void Finish( string statusPath, bool success, string phase, string message )
    {
      SessionState.SetBool( SessionPending, false );
      SessionState.EraseString( SessionStatusPath );
      SessionState.EraseFloat( SessionTimeoutSec );
      SessionState.EraseFloat( SessionStartTime );
      SessionState.EraseBool( SessionRequestedExit );
      WriteStatus( statusPath, success, true, phase, message );
    }

    private static void WriteStatus( string statusPath,
                                     bool success,
                                     bool complete,
                                     string phase,
                                     string message )
    {
      if ( string.IsNullOrWhiteSpace( statusPath ) )
        return;

      try {
        var status = new BootstrapStatus
        {
          success = success,
          complete = complete,
          phase = phase ?? string.Empty,
          message = message ?? string.Empty,
          elapsed_sec = SessionState.GetBool( SessionPending, false ) ?
                        Mathf.Max( 0.0f,
                                   (float)EditorApplication.timeSinceStartup -
                                   SessionState.GetFloat( SessionStartTime,
                                                          (float)EditorApplication.timeSinceStartup ) ) :
                        0.0f
        };
        var directory = Path.GetDirectoryName( statusPath );
        if ( !string.IsNullOrEmpty( directory ) )
          Directory.CreateDirectory( directory );
        File.WriteAllText( statusPath, JsonUtility.ToJson( status, true ) );
      }
      catch ( Exception exception ) {
        Debug.LogWarning( "Could not write Codex play-mode bootstrap status: " + exception.Message );
      }
    }

    private static string AbsolutePath( string projectRelativePath )
    {
      return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), projectRelativePath ) );
    }

    [Serializable]
    private sealed class BootstrapRequest
    {
      public string status_path = string.Empty;
      public float timeout_sec = 120.0f;
    }

    [Serializable]
    private sealed class BootstrapStatus
    {
      public bool success = false;
      public bool complete = false;
      public string phase = string.Empty;
      public string message = string.Empty;
      public float elapsed_sec = 0.0f;
    }
  }
}
#endif
