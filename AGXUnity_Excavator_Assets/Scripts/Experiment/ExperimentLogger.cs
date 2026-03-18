using System;
using System.Globalization;
using System.IO;
using System.Text;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  [Serializable]
  internal class ExperimentHardwareDiagnosticsRecord
  {
    public bool available = false;
    public bool device_connected = false;
    public string device_name = string.Empty;
    public string profile_name = string.Empty;
    public string binding_status = string.Empty;
    public string raw_input_summary = string.Empty;
    public HardwareInputSnapshot raw_input_snapshot = HardwareInputSnapshot.Zero;
  }

  [Serializable]
  internal class ExperimentActDiagnosticsRecord
  {
    public bool available = false;
    public bool backend_ready = false;
    public bool command_timed_out = false;
    public int last_response_sequence = -1;
    public float inference_time_ms = 0.0f;
    public string session_id = string.Empty;
    public string backend_status = string.Empty;
  }

  [Serializable]
  internal class ExperimentTrajectoryStepRecord
  {
    public string dataset_format = "excavator_il/v1";
    public string observation_schema = "act-operator/v1";
    public int episode_index = 0;
    public int step_index = 0;
    public string source_name = string.Empty;
    public string source_object_name = string.Empty;
    public string source_type = string.Empty;
    public string control_layout = string.Empty;
    public float fixed_time_sec = 0.0f;
    public float sim_time_sec = 0.0f;
    public float realtime_since_startup_sec = 0.0f;
    public int frame_count = 0;
    public OperatorCommand raw_operator_command = OperatorCommand.Zero;
    public OperatorCommand action_target = OperatorCommand.Zero;
    public OperatorCommand simulated_operator_command = OperatorCommand.Zero;
    public ExcavatorActuationCommand actuation_command = ExcavatorActuationCommand.Zero;
    public ActObservation observation = new ActObservation();
    public ExperimentHardwareDiagnosticsRecord hardware = new ExperimentHardwareDiagnosticsRecord();
    public ExperimentActDiagnosticsRecord act = new ExperimentActDiagnosticsRecord();
  }

  [Serializable]
  internal class ExperimentEpisodeSummaryRecord
  {
    public string dataset_format = "excavator_il/v1";
    public string observation_schema = "act-operator/v1";
    public int episode_index = 0;
    public string source_name = string.Empty;
    public string source_object_name = string.Empty;
    public string source_type = string.Empty;
    public string control_layout = string.Empty;
    public string episode_start_wall_time_iso8601 = string.Empty;
    public string episode_end_wall_time_iso8601 = string.Empty;
    public string end_reason = string.Empty;
    public int step_count = 0;
    public float start_sim_time_sec = 0.0f;
    public float end_sim_time_sec = 0.0f;
    public float duration_sec = 0.0f;
    public float start_realtime_since_startup_sec = 0.0f;
    public float end_realtime_since_startup_sec = 0.0f;
    public float fixed_dt_sec = 0.0f;
    public ActTaskState final_task_state = new ActTaskState();
    public string trajectory_jsonl_path = string.Empty;
    public string trajectory_csv_path = string.Empty;
  }

  public class ExperimentLogger : MonoBehaviour
  {
    private const string CsvHeader =
      "dataset_format,observation_schema,episode_index,step_index,source_name,source_object_name,source_type,control_layout," +
      "fixed_time_sec,sim_time_sec,realtime_since_startup_sec,frame_count,hardware_available,act_available,device_connected,device_name,profile_name,binding_status," +
      "hardware_left_x,hardware_left_y,hardware_right_x,hardware_right_y,hardware_drive,hardware_steer,hardware_reset_button,hardware_start_button,hardware_stop_button,hardware_input_summary," +
      "raw_left_x,raw_left_y,raw_right_x,raw_right_y,raw_drive,raw_steer,raw_reset_requested,raw_start_episode_requested,raw_stop_episode_requested," +
      "action_left_x,action_left_y,action_right_x,action_right_y,action_drive,action_steer," +
      "sim_left_x,sim_left_y,sim_right_x,sim_right_y,sim_drive,sim_steer," +
      "boom,bucket,stick,swing,drive,steer,throttle," +
      "base_pos_x,base_pos_y,base_pos_z,base_rot_x,base_rot_y,base_rot_z,base_rot_w," +
      "base_lin_vel_x,base_lin_vel_y,base_lin_vel_z,base_ang_vel_x,base_ang_vel_y,base_ang_vel_z," +
      "bucket_pos_x,bucket_pos_y,bucket_pos_z,bucket_rot_x,bucket_rot_y,bucket_rot_z,bucket_rot_w," +
      "boom_position_norm,boom_speed,stick_position_norm,stick_speed,bucket_position_norm,bucket_speed,swing_speed," +
      "mass_in_bucket_kg," +
      "prev_action_left_x,prev_action_left_y,prev_action_right_x,prev_action_right_y,prev_action_drive,prev_action_steer," +
      "act_backend_ready,act_command_timed_out,act_last_response_sequence,act_inference_time_ms,act_session_id,act_backend_status";

    [SerializeField]
    private string m_logDirectory = "ExperimentLogs";

    [SerializeField]
    private bool m_writeCsv = true;

    [SerializeField]
    private bool m_writeJsonLines = true;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    private StreamWriter m_csvWriter = null;
    private StreamWriter m_jsonlWriter = null;
    private string m_sourceName = "Unknown";
    private string m_sourceObjectName = string.Empty;
    private string m_sourceType = string.Empty;
    private string m_controlLayout = string.Empty;
    private string m_currentCsvPath = string.Empty;
    private string m_currentJsonlPath = string.Empty;
    private string m_currentSummaryPath = string.Empty;
    private string m_episodeStartWallTimeIso8601 = string.Empty;
    private int m_episodeIndex = 0;
    private int m_stepIndex = 0;
    private float m_episodeStartSimTimeSec = 0.0f;
    private float m_episodeStartRealtimeSec = 0.0f;
    private OperatorCommand m_previousLoggedOperatorCommand = OperatorCommand.Zero;
    private ActObservation m_lastObservation = null;

    public bool IsRecording { get; private set; }
    public string LastSavedPath { get; private set; } = string.Empty;

    private void Awake()
    {
      ResolveReferences();
    }

    private void OnDisable()
    {
      if ( IsRecording )
        EndEpisode( "logger_disabled" );
      else
        DisposeWriters();
    }

    public void BeginEpisode( int episodeIndex,
                              OperatorCommandSourceBehaviour commandSource,
                              string controlLayout )
    {
      DisposeWriters();
      ResolveReferences();

      var timestamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture );
      var rootDirectory = Path.GetFullPath( Path.Combine( Application.dataPath, "..", m_logDirectory ) );
      Directory.CreateDirectory( rootDirectory );

      m_episodeIndex = episodeIndex;
      m_stepIndex = 0;
      m_sourceName = commandSource != null ? commandSource.SourceName : "Unknown";
      m_sourceObjectName = commandSource != null ? commandSource.gameObject.name : string.Empty;
      m_sourceType = commandSource != null ? commandSource.GetType().Name : string.Empty;
      m_controlLayout = string.IsNullOrWhiteSpace( controlLayout ) ? string.Empty : controlLayout;
      m_episodeStartSimTimeSec = Time.time;
      m_episodeStartRealtimeSec = Time.realtimeSinceStartup;
      m_episodeStartWallTimeIso8601 = DateTimeOffset.Now.ToString( "O", CultureInfo.InvariantCulture );
      m_previousLoggedOperatorCommand = OperatorCommand.Zero;
      m_lastObservation = null;
      LastSavedPath = string.Empty;
      IsRecording = true;

      var fileStem = string.Format(
        CultureInfo.InvariantCulture,
        "episode_{0:000}_{1}_{2}_{3}",
        m_episodeIndex,
        timestamp,
        SanitizeFileName( m_sourceName ),
        string.IsNullOrWhiteSpace( m_sourceObjectName ) ? "source" : SanitizeFileName( m_sourceObjectName ) );

      if ( m_writeCsv ) {
        m_currentCsvPath = Path.Combine( rootDirectory, $"{fileStem}_trajectory.csv" );
        m_csvWriter = CreateWriter( m_currentCsvPath );
        m_csvWriter.WriteLine( CsvHeader );
      }
      else
        m_currentCsvPath = string.Empty;

      if ( m_writeJsonLines ) {
        m_currentJsonlPath = Path.Combine( rootDirectory, $"{fileStem}_trajectory.jsonl" );
        m_jsonlWriter = CreateWriter( m_currentJsonlPath );
      }
      else
        m_currentJsonlPath = string.Empty;

      m_currentSummaryPath = Path.Combine( rootDirectory, $"{fileStem}_summary.json" );

      m_observationCollector?.ResetSampling();
    }

    public void RecordFrame( OperatorCommandSourceBehaviour commandSource,
                             OperatorCommand rawCommand,
                             OperatorCommand simulatedCommand,
                             ExcavatorActuationCommand actuationCommand )
    {
      if ( !IsRecording )
        return;

      ResolveReferences();

      var observation = m_observationCollector != null ?
                        m_observationCollector.CollectForLogging( m_previousLoggedOperatorCommand ) :
                        CreateFallbackObservation( m_previousLoggedOperatorCommand );

      var rawCommandClamped = rawCommand.ClampAxes();
      var actionTarget = rawCommandClamped.WithoutEpisodeSignals().ClampAxes();
      var simulatedCommandClamped = simulatedCommand.WithoutEpisodeSignals().ClampAxes();
      var actuationCommandClamped = actuationCommand.ClampAxes();
      var hardwareRecord = CreateHardwareRecord( commandSource as IHardwareCommandDiagnostics );
      var actRecord = CreateActRecord( commandSource as IActCommandDiagnostics );
      var stepRecord = new ExperimentTrajectoryStepRecord
      {
        episode_index = m_episodeIndex,
        step_index = m_stepIndex,
        source_name = m_sourceName,
        source_object_name = m_sourceObjectName,
        source_type = m_sourceType,
        control_layout = m_controlLayout,
        fixed_time_sec = Time.fixedTime,
        sim_time_sec = observation.sim_time_sec,
        realtime_since_startup_sec = Time.realtimeSinceStartup,
        frame_count = Time.frameCount,
        raw_operator_command = rawCommandClamped,
        action_target = actionTarget,
        simulated_operator_command = simulatedCommandClamped,
        actuation_command = actuationCommandClamped,
        observation = observation,
        hardware = hardwareRecord,
        act = actRecord
      };

      if ( m_jsonlWriter != null )
        m_jsonlWriter.WriteLine( JsonUtility.ToJson( stepRecord ) );

      if ( m_csvWriter != null )
        m_csvWriter.WriteLine( BuildCsvRow( stepRecord ) );

      m_previousLoggedOperatorCommand = actionTarget;
      m_lastObservation = observation;
      ++m_stepIndex;
    }

    public string EndEpisode( string reason )
    {
      if ( !IsRecording && m_csvWriter == null && m_jsonlWriter == null )
        return LastSavedPath;

      var endReason = string.IsNullOrWhiteSpace( reason ) ? "episode_end" : reason;
      var endSimTimeSec = Time.time;
      var endRealtimeSec = Time.realtimeSinceStartup;
      var summaryRecord = new ExperimentEpisodeSummaryRecord
      {
        episode_index = m_episodeIndex,
        source_name = m_sourceName,
        source_object_name = m_sourceObjectName,
        source_type = m_sourceType,
        control_layout = m_controlLayout,
        episode_start_wall_time_iso8601 = m_episodeStartWallTimeIso8601,
        episode_end_wall_time_iso8601 = DateTimeOffset.Now.ToString( "O", CultureInfo.InvariantCulture ),
        end_reason = endReason,
        step_count = m_stepIndex,
        start_sim_time_sec = m_episodeStartSimTimeSec,
        end_sim_time_sec = endSimTimeSec,
        duration_sec = Mathf.Max( 0.0f, endSimTimeSec - m_episodeStartSimTimeSec ),
        start_realtime_since_startup_sec = m_episodeStartRealtimeSec,
        end_realtime_since_startup_sec = endRealtimeSec,
        fixed_dt_sec = Time.fixedDeltaTime,
        final_task_state = CreateTaskStateCopy( m_lastObservation != null ? m_lastObservation.task_state : null ),
        trajectory_jsonl_path = m_currentJsonlPath,
        trajectory_csv_path = m_currentCsvPath
      };

      Directory.CreateDirectory( Path.GetDirectoryName( m_currentSummaryPath ) ?? string.Empty );
      File.WriteAllText( m_currentSummaryPath, JsonUtility.ToJson( summaryRecord, true ), new UTF8Encoding( false ) );

      DisposeWriters();

      IsRecording = false;
      LastSavedPath = !string.IsNullOrWhiteSpace( m_currentJsonlPath ) ? m_currentJsonlPath :
                      !string.IsNullOrWhiteSpace( m_currentCsvPath ) ? m_currentCsvPath :
                      m_currentSummaryPath;
      return LastSavedPath;
    }

    private void ResolveReferences()
    {
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
    }

    private void DisposeWriters()
    {
      m_csvWriter?.Dispose();
      m_jsonlWriter?.Dispose();
      m_csvWriter = null;
      m_jsonlWriter = null;
    }

    private static StreamWriter CreateWriter( string path )
    {
      var writer = new StreamWriter( path, false, new UTF8Encoding( false ) )
      {
        AutoFlush = true
      };
      return writer;
    }

    private static ExperimentHardwareDiagnosticsRecord CreateHardwareRecord( IHardwareCommandDiagnostics diagnostics )
    {
      if ( diagnostics == null )
        return new ExperimentHardwareDiagnosticsRecord();

      return new ExperimentHardwareDiagnosticsRecord
      {
        available = true,
        device_connected = diagnostics.DeviceConnected,
        device_name = diagnostics.DeviceDisplayName ?? string.Empty,
        profile_name = diagnostics.ProfileName ?? string.Empty,
        binding_status = diagnostics.BindingStatus ?? string.Empty,
        raw_input_summary = diagnostics.LastRawInputSummary ?? string.Empty,
        raw_input_snapshot = diagnostics.LastRawInputSnapshot
      };
    }

    private static ExperimentActDiagnosticsRecord CreateActRecord( IActCommandDiagnostics diagnostics )
    {
      if ( diagnostics == null )
        return new ExperimentActDiagnosticsRecord();

      return new ExperimentActDiagnosticsRecord
      {
        available = true,
        backend_ready = diagnostics.IsBackendReady,
        command_timed_out = diagnostics.IsCommandTimedOut,
        last_response_sequence = diagnostics.LastResponseSequence,
        inference_time_ms = diagnostics.LastInferenceTimeMs,
        session_id = diagnostics.CurrentSessionId ?? string.Empty,
        backend_status = diagnostics.LastBackendStatus ?? string.Empty
      };
    }

    private static ActObservation CreateFallbackObservation( OperatorCommand previousOperatorCommand )
    {
      return new ActObservation
      {
        sim_time_sec = Time.time,
        fixed_dt_sec = Time.fixedDeltaTime,
        previous_operator_command = ActWireOperatorCommand.FromOperatorCommand( previousOperatorCommand.WithoutEpisodeSignals() )
      };
    }

    private static ActTaskState CreateTaskStateCopy( ActTaskState taskState )
    {
      if ( taskState == null )
        return new ActTaskState();

      return new ActTaskState
      {
        mass_in_bucket_kg = taskState.mass_in_bucket_kg
      };
    }

    private static string BuildCsvRow( ExperimentTrajectoryStepRecord record )
    {
      var observation = record.observation ?? new ActObservation();
      var hardware = record.hardware ?? new ExperimentHardwareDiagnosticsRecord();
      var act = record.act ?? new ExperimentActDiagnosticsRecord();
      var actuatorState = observation.actuator_state ?? new ActActuatorState();
      var taskState = observation.task_state ?? new ActTaskState();
      var previousCommand = observation.previous_operator_command ?? new ActWireOperatorCommand();

      return string.Join(
        ",",
        Csv( record.dataset_format ),
        Csv( record.observation_schema ),
        record.episode_index.ToString( CultureInfo.InvariantCulture ),
        record.step_index.ToString( CultureInfo.InvariantCulture ),
        Csv( record.source_name ),
        Csv( record.source_object_name ),
        Csv( record.source_type ),
        Csv( record.control_layout ),
        F( record.fixed_time_sec ),
        F( record.sim_time_sec ),
        F( record.realtime_since_startup_sec ),
        record.frame_count.ToString( CultureInfo.InvariantCulture ),
        B( hardware.available ),
        B( act.available ),
        B( hardware.device_connected ),
        Csv( hardware.device_name ),
        Csv( hardware.profile_name ),
        Csv( hardware.binding_status ),
        F( hardware.raw_input_snapshot.LeftStickX ),
        F( hardware.raw_input_snapshot.LeftStickY ),
        F( hardware.raw_input_snapshot.RightStickX ),
        F( hardware.raw_input_snapshot.RightStickY ),
        F( hardware.raw_input_snapshot.Drive ),
        F( hardware.raw_input_snapshot.Steer ),
        F( hardware.raw_input_snapshot.ResetButton ),
        F( hardware.raw_input_snapshot.StartEpisodeButton ),
        F( hardware.raw_input_snapshot.StopEpisodeButton ),
        Csv( hardware.raw_input_summary ),
        F( record.raw_operator_command.LeftStickX ),
        F( record.raw_operator_command.LeftStickY ),
        F( record.raw_operator_command.RightStickX ),
        F( record.raw_operator_command.RightStickY ),
        F( record.raw_operator_command.Drive ),
        F( record.raw_operator_command.Steer ),
        B( record.raw_operator_command.ResetRequested ),
        B( record.raw_operator_command.StartEpisodeRequested ),
        B( record.raw_operator_command.StopEpisodeRequested ),
        F( record.action_target.LeftStickX ),
        F( record.action_target.LeftStickY ),
        F( record.action_target.RightStickX ),
        F( record.action_target.RightStickY ),
        F( record.action_target.Drive ),
        F( record.action_target.Steer ),
        F( record.simulated_operator_command.LeftStickX ),
        F( record.simulated_operator_command.LeftStickY ),
        F( record.simulated_operator_command.RightStickX ),
        F( record.simulated_operator_command.RightStickY ),
        F( record.simulated_operator_command.Drive ),
        F( record.simulated_operator_command.Steer ),
        F( record.actuation_command.Boom ),
        F( record.actuation_command.Bucket ),
        F( record.actuation_command.Stick ),
        F( record.actuation_command.Swing ),
        F( record.actuation_command.Drive ),
        F( record.actuation_command.Steer ),
        F( record.actuation_command.Throttle ),
        F( A( observation.base_pose_world?.position, 0 ) ),
        F( A( observation.base_pose_world?.position, 1 ) ),
        F( A( observation.base_pose_world?.position, 2 ) ),
        F( A( observation.base_pose_world?.rotation_xyzw, 0 ) ),
        F( A( observation.base_pose_world?.rotation_xyzw, 1 ) ),
        F( A( observation.base_pose_world?.rotation_xyzw, 2 ) ),
        F( A( observation.base_pose_world?.rotation_xyzw, 3 ) ),
        F( A( observation.base_velocity_local?.linear, 0 ) ),
        F( A( observation.base_velocity_local?.linear, 1 ) ),
        F( A( observation.base_velocity_local?.linear, 2 ) ),
        F( A( observation.base_velocity_local?.angular, 0 ) ),
        F( A( observation.base_velocity_local?.angular, 1 ) ),
        F( A( observation.base_velocity_local?.angular, 2 ) ),
        F( A( observation.bucket_pose_world?.position, 0 ) ),
        F( A( observation.bucket_pose_world?.position, 1 ) ),
        F( A( observation.bucket_pose_world?.position, 2 ) ),
        F( A( observation.bucket_pose_world?.rotation_xyzw, 0 ) ),
        F( A( observation.bucket_pose_world?.rotation_xyzw, 1 ) ),
        F( A( observation.bucket_pose_world?.rotation_xyzw, 2 ) ),
        F( A( observation.bucket_pose_world?.rotation_xyzw, 3 ) ),
        F( actuatorState.boom_position_norm ),
        F( actuatorState.boom_speed ),
        F( actuatorState.stick_position_norm ),
        F( actuatorState.stick_speed ),
        F( actuatorState.bucket_position_norm ),
        F( actuatorState.bucket_speed ),
        F( actuatorState.swing_speed ),
        F( taskState.mass_in_bucket_kg ),
        F( previousCommand.left_stick_x ),
        F( previousCommand.left_stick_y ),
        F( previousCommand.right_stick_x ),
        F( previousCommand.right_stick_y ),
        F( previousCommand.drive ),
        F( previousCommand.steer ),
        B( act.backend_ready ),
        B( act.command_timed_out ),
        act.last_response_sequence.ToString( CultureInfo.InvariantCulture ),
        F( act.inference_time_ms ),
        Csv( act.session_id ),
        Csv( act.backend_status ) );
    }

    private static float A( float[] values, int index )
    {
      return values != null && index >= 0 && index < values.Length ? values[ index ] : 0.0f;
    }

    private static string B( bool value )
    {
      return value ? "1" : "0";
    }

    private static string F( float value )
    {
      return value.ToString( "0.######", CultureInfo.InvariantCulture );
    }

    private static string Csv( string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return "none";

      return value.Replace( ',', ';' )
                  .Replace( '\r', ' ' )
                  .Replace( '\n', ' ' );
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
