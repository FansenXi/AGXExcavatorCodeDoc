using System;
using System.Globalization;
using System.IO;
using System.Text;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class TeleopEpisodeExporter : MonoBehaviour
  {
    [Serializable]
    private sealed class TeleopEpisodeMetadata
    {
      public string exporter_version = "teleop-export/v0";
      public int episode_index = 0;
      public string source_name = string.Empty;
      public string scene_name = string.Empty;
      public string control_layout = string.Empty;
      public string record_loop = "Update";
      public float fixed_dt_sec = 0.02f;
      public string action_semantics = "actuator_speed_cmd";
      public string[] action_order = Array.Empty<string>();
      public string[] qpos_order = Array.Empty<string>();
      public string[] qvel_order = Array.Empty<string>();
      public string[] env_state_order = Array.Empty<string>();
      public string[] camera_names = Array.Empty<string>();
      public string image_format = "raw_rgb";
      public string image_row_order = "top_to_bottom";
      public bool capture_fpv_frames = true;
      public int frame_stride = 1;
      public string started_at_local = string.Empty;
      public string stop_reason = string.Empty;
      public int step_count = 0;
      public int captured_fpv_frame_count = 0;
    }

    [Serializable]
    private sealed class TeleopImageFrameRecord
    {
      public string name = "fpv";
      public string relative_path = string.Empty;
      public int width = 0;
      public int height = 0;
      public int byte_count = 0;
      public string pixel_format = "raw_rgb";
      public string row_order = "top_to_bottom";
    }

    [Serializable]
    private sealed class TeleopActuationCommandRecord
    {
      public float boom = 0.0f;
      public float bucket = 0.0f;
      public float stick = 0.0f;
      public float swing = 0.0f;
      public float drive = 0.0f;
      public float steer = 0.0f;
      public float throttle = 0.0f;
    }

    [Serializable]
    private sealed class TeleopStepRecord
    {
      public int step_index = 0;
      public float wall_time_sec = 0.0f;
      public float sim_time_sec = 0.0f;
      public float fixed_dt_sec = 0.02f;
      public float[] action = Array.Empty<float>();
      public float[] qpos = Array.Empty<float>();
      public float[] qvel = Array.Empty<float>();
      public float[] env_state = Array.Empty<float>();
      public float reward = 0.0f;
      public ActWireOperatorCommand raw_operator_command = new ActWireOperatorCommand();
      public ActWireOperatorCommand simulated_operator_command = new ActWireOperatorCommand();
      public TeleopActuationCommandRecord actuation_command = new TeleopActuationCommandRecord();
      public bool act_backend_ready = false;
      public bool act_timeout_fallback = false;
      public int act_response_seq = -1;
      public float act_inference_time_ms = 0.0f;
      public string act_session_id = string.Empty;
      public string act_status = string.Empty;
      public TeleopImageFrameRecord fpv_image = null;
    }

    [SerializeField]
    private string m_exportDirectory = "TeleopExports";

    [SerializeField]
    private bool m_captureFpvFrames = true;

    [SerializeField]
    [Min( 1 )]
    private int m_frameStride = 1;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    [SerializeField]
    private TrackedCameraWindow m_fpvCamera = null;

    private StreamWriter m_stepWriter = null;
    private TeleopEpisodeMetadata m_metadata = null;
    private string m_episodeDirectory = string.Empty;
    private string m_frameDirectory = string.Empty;
    private int m_stepIndex = 0;
    private int m_capturedFrameCount = 0;

    public bool IsExporting => m_stepWriter != null;
    public string LastExportDirectory { get; private set; } = string.Empty;

    private void Awake()
    {
      ResolveReferences();
    }

    private void OnDisable()
    {
      AbortExportIfNeeded( "component_disabled" );
    }

    private void OnDestroy()
    {
      AbortExportIfNeeded( "component_destroyed" );
    }

    public void BeginEpisode( int episodeIndex, string sourceName, string controlLayout )
    {
      ResolveReferences();
      AbortExportIfNeeded( "restart_without_close" );

      var exportRoot = Path.GetFullPath( Path.Combine( Application.dataPath, "..", m_exportDirectory ) );
      Directory.CreateDirectory( exportRoot );

      var safeSourceName = SanitizeFileName( sourceName );
      var timestamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture );
      m_episodeDirectory = Path.Combine(
        exportRoot,
        string.Format(
          CultureInfo.InvariantCulture,
          "episode_{0:000}_{1}_{2}",
          episodeIndex,
          timestamp,
          safeSourceName ) );

      Directory.CreateDirectory( m_episodeDirectory );
      m_frameDirectory = Path.Combine( m_episodeDirectory, "frames", "fpv" );
      if ( m_captureFpvFrames )
        Directory.CreateDirectory( m_frameDirectory );

      m_stepWriter = new StreamWriter(
        Path.Combine( m_episodeDirectory, "steps.jsonl" ),
        false,
        new UTF8Encoding( false ) )
      {
        AutoFlush = true
      };

      m_stepIndex = 0;
      m_capturedFrameCount = 0;
      LastExportDirectory = m_episodeDirectory;

      m_metadata = new TeleopEpisodeMetadata
      {
        episode_index = episodeIndex,
        source_name = sourceName ?? string.Empty,
        scene_name = SceneManager.GetActiveScene().name,
        control_layout = controlLayout ?? string.Empty,
        fixed_dt_sec = Time.fixedDeltaTime,
        action_order = new[] { "swing_speed_cmd", "boom_speed_cmd", "stick_speed_cmd", "bucket_speed_cmd" },
        qpos_order = new[] { "swing_position_norm", "boom_position_norm", "stick_position_norm", "bucket_position_norm" },
        qvel_order = new[] { "swing_speed", "boom_speed", "stick_speed", "bucket_speed" },
        env_state_order = new[] { "mass_in_bucket_kg" },
        camera_names = m_captureFpvFrames ? new[] { "fpv" } : Array.Empty<string>(),
        capture_fpv_frames = m_captureFpvFrames,
        frame_stride = Mathf.Max( 1, m_frameStride ),
        started_at_local = DateTime.Now.ToString( "o", CultureInfo.InvariantCulture ),
        stop_reason = "running"
      };

      WriteMetadata();
    }

    public void RecordStep( float wallTimeSeconds,
                            OperatorCommand rawCommand,
                            OperatorCommand simulatedCommand,
                            ExcavatorActuationCommand actuationCommand,
                            IActCommandDiagnostics actDiagnostics )
    {
      if ( m_stepWriter == null )
        return;

      ResolveReferences();

      var observation = m_observationCollector != null ?
                        m_observationCollector.Collect( simulatedCommand.WithoutEpisodeSignals() ) :
                        new ActObservation();

      var stepRecord = new TeleopStepRecord
      {
        step_index = m_stepIndex,
        wall_time_sec = wallTimeSeconds,
        sim_time_sec = observation != null ? observation.sim_time_sec : wallTimeSeconds,
        fixed_dt_sec = observation != null ? observation.fixed_dt_sec : Time.fixedDeltaTime,
        action = new[]
        {
          actuationCommand.Swing,
          actuationCommand.Boom,
          actuationCommand.Stick,
          actuationCommand.Bucket
        },
        qpos = new[]
        {
          observation != null && observation.actuator_state != null ? observation.actuator_state.swing_position_norm : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.boom_position_norm : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.stick_position_norm : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.bucket_position_norm : 0.0f
        },
        qvel = new[]
        {
          observation != null && observation.actuator_state != null ? observation.actuator_state.swing_speed : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.boom_speed : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.stick_speed : 0.0f,
          observation != null && observation.actuator_state != null ? observation.actuator_state.bucket_speed : 0.0f
        },
        env_state = new[]
        {
          observation != null && observation.task_state != null ? observation.task_state.mass_in_bucket_kg : 0.0f
        },
        reward = 0.0f,
        raw_operator_command = ActWireOperatorCommand.FromOperatorCommand( rawCommand.WithoutEpisodeSignals() ),
        simulated_operator_command = ActWireOperatorCommand.FromOperatorCommand( simulatedCommand.WithoutEpisodeSignals() ),
        actuation_command = new TeleopActuationCommandRecord
        {
          boom = actuationCommand.Boom,
          bucket = actuationCommand.Bucket,
          stick = actuationCommand.Stick,
          swing = actuationCommand.Swing,
          drive = actuationCommand.Drive,
          steer = actuationCommand.Steer,
          throttle = actuationCommand.Throttle
        },
        act_backend_ready = actDiagnostics != null && actDiagnostics.IsBackendReady,
        act_timeout_fallback = actDiagnostics != null && actDiagnostics.IsCommandTimedOut,
        act_response_seq = actDiagnostics != null ? actDiagnostics.LastResponseSequence : -1,
        act_inference_time_ms = actDiagnostics != null ? actDiagnostics.LastInferenceTimeMs : 0.0f,
        act_session_id = actDiagnostics != null ? actDiagnostics.CurrentSessionId : string.Empty,
        act_status = actDiagnostics != null ? actDiagnostics.LastBackendStatus : string.Empty
      };

      if ( m_captureFpvFrames && m_fpvCamera != null && m_stepIndex % Mathf.Max( 1, m_frameStride ) == 0 ) {
        if ( m_fpvCamera.TryCaptureRgb24( out var rgb24, out var width, out var height ) ) {
          var frameFileName = string.Format( CultureInfo.InvariantCulture, "frame_{0:000000}.rgb24", m_stepIndex );
          var framePath = Path.Combine( m_frameDirectory, frameFileName );
          File.WriteAllBytes( framePath, rgb24 ?? Array.Empty<byte>() );

          stepRecord.fpv_image = new TeleopImageFrameRecord
          {
            relative_path = Path.Combine( "frames", "fpv", frameFileName ).Replace( '\\', '/' ),
            width = width,
            height = height,
            byte_count = rgb24 != null ? rgb24.Length : 0
          };
          ++m_capturedFrameCount;
        }
      }

      m_stepWriter.WriteLine( JsonUtility.ToJson( stepRecord ) );
      ++m_stepIndex;
    }

    public string EndEpisode( string reason )
    {
      if ( m_stepWriter == null )
        return LastExportDirectory;

      if ( m_metadata != null ) {
        m_metadata.stop_reason = string.IsNullOrWhiteSpace( reason ) ? "episode_end" : reason;
        m_metadata.step_count = m_stepIndex;
        m_metadata.captured_fpv_frame_count = m_capturedFrameCount;
        WriteMetadata();
      }

      CloseWriter();
      return LastExportDirectory;
    }

    private void ResolveReferences()
    {
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
      m_fpvCamera = ExcavatorRigLocator.ResolveComponent( this, m_fpvCamera );
    }

    private void WriteMetadata()
    {
      if ( string.IsNullOrWhiteSpace( m_episodeDirectory ) || m_metadata == null )
        return;

      var metadataPath = Path.Combine( m_episodeDirectory, "metadata.json" );
      using ( var writer = new StreamWriter( metadataPath, false, new UTF8Encoding( false ) ) ) {
        writer.Write( JsonUtility.ToJson( m_metadata, true ) );
      }
    }

    private void AbortExportIfNeeded( string reason )
    {
      if ( m_stepWriter == null )
        return;

      try {
        EndEpisode( reason );
      }
      catch ( Exception exception ) {
        Debug.LogWarning( $"Teleop exporter abort failed: {exception.Message}", this );
        CloseWriter();
      }
    }

    private void CloseWriter()
    {
      try {
        m_stepWriter?.Dispose();
      }
      catch ( Exception ) {
      }
      finally {
        m_stepWriter = null;
        m_metadata = null;
        m_episodeDirectory = string.Empty;
        m_frameDirectory = string.Empty;
        m_stepIndex = 0;
        m_capturedFrameCount = 0;
      }
    }

    private static string SanitizeFileName( string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return "none";

      foreach ( var invalidChar in Path.GetInvalidFileNameChars() )
        value = value.Replace( invalidChar, '_' );

      return value.Replace( ' ', '_' );
    }
  }
}
