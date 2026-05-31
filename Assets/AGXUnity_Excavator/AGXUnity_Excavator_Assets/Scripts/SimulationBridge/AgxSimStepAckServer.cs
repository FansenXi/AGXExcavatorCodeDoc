using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using AGXUnity;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.Presentation;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.SimulationBridge
{
  public class AgxSimStepAckServer : MonoBehaviour
  {
    private sealed class PendingRequest
    {
      public AgxSimMessageType Type = 0;
      public AgxSimRequestPayload Payload = new AgxSimRequestPayload();
    }

    [SerializeField]
    private int m_port = 5057;

    [SerializeField]
    private bool m_listenOnEnable = true;

    [SerializeField]
    private bool m_disableEpisodeManagerWhileServing = true;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    [SerializeField]
    private SceneResetService m_sceneResetService = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private TrackedCameraWindow m_fpvCamera = null;

    [SerializeField]
    private bool m_enableDebugLogs = true;

    [SerializeField]
    [Min( 1 )]
    private int m_stepDebugLogInterval = 100;

    [SerializeField]
    private bool m_autoCreatePlannerVisualizer = true;

    [SerializeField]
    private PlannerDecisionVisualizer m_plannerDecisionVisualizer = null;

    [Header( "Reset Pose QPos" )]
    [SerializeField]
    private bool m_applyResetPoseQpos = false;

    [SerializeField]
    private float[] m_resetPoseQpos = Array.Empty<float>();

    [SerializeField]
    [Range( 0, 100 )]
    private int m_resetPoseQposBurnInSteps = 3;

    private readonly ConcurrentQueue<PendingRequest> m_pendingRequests = new ConcurrentQueue<PendingRequest>();
    private readonly ConcurrentQueue<byte[]> m_pendingResponses = new ConcurrentQueue<byte[]>();

    private Thread m_serverThread = null;
    private volatile bool m_stopRequested = false;
    private bool m_isListening = false;
    private bool m_restoreEpisodeManagerEnabled = false;
    private string m_lastRequestTypeName = "none";
    private long m_lastRequestStepId = -1;
    private bool m_lastResponseSuccess = true;
    private bool m_lastResetApplied = false;
    private int m_lastImageWidth = 0;
    private int m_lastImageHeight = 0;
    private int m_lastImagePayloadBytes = 0;
    private string m_lastWarningsSummary = "none";
    private string m_lastError = string.Empty;
    private PlannerDebugSnapshot m_lastPlannerDebug = PlannerDebugSnapshot.Empty();

    public bool IsListening => m_isListening;
    public string LastRequestTypeName => m_lastRequestTypeName;
    public long LastRequestStepId => m_lastRequestStepId;
    public bool LastResponseSuccess => m_lastResponseSuccess;
    public bool LastResetApplied => m_lastResetApplied;
    public int LastImageWidth => m_lastImageWidth;
    public int LastImageHeight => m_lastImageHeight;
    public int LastImagePayloadBytes => m_lastImagePayloadBytes;
    public string LastWarningsSummary => m_lastWarningsSummary;
    public string LastError => m_lastError;
    public PlannerDebugSnapshot LastPlannerDebug => m_lastPlannerDebug ?? PlannerDebugSnapshot.Empty();

    public void ConfigureResetPoseQpos( float[] qpos, int burnInSteps )
    {
      m_applyResetPoseQpos = qpos != null && qpos.Length >= 4;
      m_resetPoseQpos = m_applyResetPoseQpos ?
        new[] { qpos[ 0 ], qpos[ 1 ], qpos[ 2 ], qpos[ 3 ] } :
        Array.Empty<float>();
      m_resetPoseQposBurnInSteps = Mathf.Clamp( burnInSteps, 0, 100 );
    }

    private void Awake()
    {
      ResolveReferences();
      EnsurePlannerVisualizer();
    }

    private void OnEnable()
    {
      ResolveReferences();
      EnsurePlannerVisualizer();
      if ( m_disableEpisodeManagerWhileServing && m_episodeManager != null && m_episodeManager.enabled ) {
        m_restoreEpisodeManagerEnabled = true;
        m_episodeManager.enabled = false;
      }

      if ( m_listenOnEnable )
        StartServer();
    }

    private void OnDisable()
    {
      StopServer();
      if ( m_restoreEpisodeManagerEnabled && m_episodeManager != null )
        m_episodeManager.enabled = true;
      m_restoreEpisodeManagerEnabled = false;
    }

    private void OnDestroy()
    {
      StopServer();
    }

    private void Update()
    {
      ResolveReferences();
      EnsurePlannerVisualizer();
      ProcessPendingRequests();
    }

    public void StartServer()
    {
      if ( m_serverThread != null )
        return;

      m_stopRequested = false;
      m_serverThread = new Thread( ServerLoop )
      {
        IsBackground = true,
        Name = "AGX-Sim-StepAck-Server"
      };
      m_serverThread.Start();
    }

    public void StopServer()
    {
      m_stopRequested = true;
      m_isListening = false;

      if ( m_serverThread != null ) {
        if ( !m_serverThread.Join( 500 ) )
          m_serverThread.Interrupt();
        m_serverThread = null;
      }
    }

    private void ServerLoop()
    {
      TcpListener listener = null;
      TcpClient client = null;
      NetworkStream stream = null;

      try {
        listener = new TcpListener( IPAddress.Any, m_port );
        listener.Start();
        m_isListening = true;

        while ( !m_stopRequested ) {
          if ( client != null && !IsClientConnectionAlive( client ) ) {
            CloseClientConnection( ref stream, ref client );
            continue;
          }

          if ( client == null ) {
            if ( !listener.Pending() ) {
              Thread.Sleep( 5 );
              continue;
            }

            client = listener.AcceptTcpClient();
            client.NoDelay = true;
            stream = client.GetStream();
          }

          if ( client == null || !client.Connected || stream == null ) {
            CloseClientConnection( ref stream, ref client );
            continue;
          }

          if ( client.Client.Available > 0 ) {
            if ( !TryQueueIncomingRequest( stream ) ) {
              CloseClientConnection( ref stream, ref client );
              continue;
            }
          }

          if ( !TryFlushPendingResponses( stream ) ) {
            CloseClientConnection( ref stream, ref client );
            continue;
          }

          Thread.Sleep( 5 );
        }
      }
      catch ( ThreadInterruptedException ) {
      }
      catch ( System.Exception exception ) {
        Debug.LogError( $"AGX sim step-ack server stopped unexpectedly: {exception.Message}", this );
      }
      finally {
        m_isListening = false;
        CloseClientConnection( ref stream, ref client );

        try {
          listener?.Stop();
        }
        catch ( System.Exception ) {
        }
      }
    }

    private bool TryQueueIncomingRequest( NetworkStream stream )
    {
      if ( !AgxSimBinaryProtocol.TryReadFrame( stream, out var messageType, out var payloadBytes, out var error ) ) {
        Debug.LogWarning( $"AGX sim step-ack server dropped invalid frame: {error}", this );
        return false;
      }

      if ( messageType != AgxSimMessageType.GetInfoReq &&
           messageType != AgxSimMessageType.ResetReq &&
           messageType != AgxSimMessageType.StepReq &&
           messageType != AgxSimMessageType.RealignPoseReq ) {
        Debug.LogWarning( $"AGX sim step-ack server received unsupported msg_type: {messageType}", this );
        return false;
      }

      if ( !AgxSimBinaryProtocol.TryDeserializeRequest( messageType, payloadBytes, out var payload, out error ) ) {
        QueueResponse( CreateErrorResponse( GetResponseTypeForRequest( messageType ), error ) );
        return true;
      }

      m_pendingRequests.Enqueue( new PendingRequest
      {
        Type = messageType,
        Payload = payload ?? new AgxSimRequestPayload()
      } );

      return true;
    }

    private void ProcessPendingRequests()
    {
      while ( m_pendingRequests.TryDequeue( out var request ) ) {
        RecordIncomingRequestDebug( request );

        switch ( request.Type ) {
          case AgxSimMessageType.GetInfoReq:
            QueueResponse( CreateInfoResponse() );
            break;
          case AgxSimMessageType.ResetReq:
            QueueResponse( CreateResetResponse( request.Payload ) );
            break;
          case AgxSimMessageType.StepReq:
            QueueResponse( CreateStepResponse( request.Payload ) );
            break;
          case AgxSimMessageType.RealignPoseReq:
            QueueResponse( CreateRealignPoseResponse( request.Payload ) );
            break;
          default:
            Debug.LogWarning( $"AGX sim step-ack server encountered unsupported pending request: {request.Type}", this );
            break;
        }
      }
    }

    private byte[] CreateInfoResponse()
    {
      var payload = CreateBasePayload();
      payload.action_order = new[] { "swing_speed_cmd", "boom_speed_cmd", "stick_speed_cmd", "bucket_speed_cmd" };
      payload.qpos_order = new[] { "swing_position_norm", "boom_position_norm", "stick_position_norm", "bucket_position_norm" };
      payload.qvel_order = new[] { "swing_speed", "boom_speed", "stick_speed", "bucket_speed" };
      payload.env_state_order = new[]
      {
        "mass_in_bucket_kg",
        "excavated_mass_kg",
        "mass_in_target_box_kg",
        "deposited_mass_in_target_box_kg",
        "min_distance_to_target_m",
        "target_hard_collision_count",
        "target_contact_max_normal_force_n",
        "min_distance_to_dig_area_m",
        "bucket_depth_below_dig_area_plane_m",
        "target_horizontal_distance_m",
        "bucket_height_above_target_rim_m",
        "bucket_over_target_footprint_mask",
        "dump_clearance_ok_mask",
        "bucket_dump_area_relative_x_m",
        "bucket_dump_area_relative_z_m",
        "bucket_dump_area_footprint_outside_distance_m",
        "bucket_dig_area_cell_in_bounds_mask",
        "dig_area_long_axis",
        "dig_area_grid_long_count",
        "dig_area_grid_short_count",
        "bucket_dig_area_relative_x_m",
        "bucket_dig_area_relative_y_m",
        "bucket_dig_area_relative_z_m",
        "bucket_dig_area_long_norm",
	        "bucket_dig_area_short_norm",
	        "bucket_dig_area_long_index",
	        "bucket_dig_area_short_index",
	        "bucket_dig_area_cell_id",
	        "bucket_tip_dig_area_x_m",
	        "bucket_tip_dig_area_y_m",
	        "bucket_tip_dig_area_z_m",
	        "bucket_depth_below_local_surface_m",
	        "bucket_depth_below_target_surface_m",
	        "dig_area_surface_depth_m_r0_c0",
	        "dig_area_surface_depth_m_r0_c1",
	        "dig_area_surface_depth_m_r1_c0",
	        "dig_area_surface_depth_m_r1_c1",
	        "dig_area_surface_depth_m_r2_c0",
	        "dig_area_surface_depth_m_r2_c1",
	        "dig_area_removed_depth_m_r0_c0",
	        "dig_area_removed_depth_m_r0_c1",
	        "dig_area_removed_depth_m_r1_c0",
	        "dig_area_removed_depth_m_r1_c1",
	        "dig_area_removed_depth_m_r2_c0",
	        "dig_area_removed_depth_m_r2_c1",
	        "dig_area_target_depth_m_r0_c0",
	        "dig_area_target_depth_m_r0_c1",
	        "dig_area_target_depth_m_r1_c0",
	        "dig_area_target_depth_m_r1_c1",
	        "dig_area_target_depth_m_r2_c0",
	        "dig_area_target_depth_m_r2_c1",
	        "dig_area_cell_valid_mask_r0_c0",
	        "dig_area_cell_valid_mask_r0_c1",
	        "dig_area_cell_valid_mask_r1_c0",
	        "dig_area_cell_valid_mask_r1_c1",
	        "dig_area_cell_valid_mask_r2_c0",
	        "dig_area_cell_valid_mask_r2_c1",
	        "bucket_mass_delta_kg",
	        "deposited_mass_in_dump_area_kg",
	        "offtarget_deposited_mass_kg",
	        "target_geometry_available",
	        "bucket_dig_area_penetration_contact_mask",
	        "bucket_contact_dump_area_mask",
	        "hard_collision_count"
	      };
      payload.cameras = CreateCameraDescriptors();
      payload.camera_names = Array.ConvertAll( payload.cameras, camera => camera.name );
      payload.supports_images = payload.cameras.Length > 0;
      payload.supports_reset_pose = true;

      return AgxSimBinaryProtocol.SerializeResponse( AgxSimMessageType.GetInfoResp, payload );
    }

    private byte[] CreateResetResponse( AgxSimRequestPayload request )
    {
      var warnings = new List<string>();
      EnsureManualStepping( warnings );
      m_lastPlannerDebug = PlannerDebugSnapshot.Empty();

      var resetTerrain = request != null && request.reset_terrain;
      var resetPose = request != null && request.reset_pose;
      var shouldResetToInitialFrame = resetTerrain || resetPose;
      var resetApplied = false;

      if ( shouldResetToInitialFrame ) {
        if ( m_sceneResetService != null ) {
          m_sceneResetService.ResetScene( resetTerrain, resetPose );
          if ( resetPose )
            ApplyConfiguredResetPoseQpos( warnings );
          m_machineController?.StartEngine();
          resetApplied = true;
        }
        else if ( m_episodeManager != null && resetTerrain && resetPose ) {
          m_episodeManager.ResetEpisode( restartEpisode: true );
          ApplyConfiguredResetPoseQpos( warnings );
          resetApplied = true;
        }
        else {
          warnings.Add( "scene_reset_service_missing" );
        }
      }
      else {
        m_machineController?.StopMotion();
      }

      m_observationCollector?.ResetSampling();

      var payload = CreateBasePayload();
      payload.reset_applied = resetApplied;
      payload.warnings = warnings.ToArray();
      RecordResetResponseDebug( resetApplied, payload.success, payload.error, payload.warnings );

      return AgxSimBinaryProtocol.SerializeResponse( AgxSimMessageType.ResetResp, payload );
    }

    private void ApplyConfiguredResetPoseQpos( List<string> warnings )
    {
      if ( !m_applyResetPoseQpos )
        return;

      if ( m_resetPoseQpos == null || m_resetPoseQpos.Length < 4 ) {
        warnings?.Add( "reset_pose_qpos_missing" );
        return;
      }

      if ( m_machineController == null ) {
        warnings?.Add( "reset_pose_qpos_machine_controller_missing" );
        return;
      }

      if ( !m_machineController.TryRealignNormalizedPose( m_resetPoseQpos, out var realignWarning ) ) {
        warnings?.Add(
          string.IsNullOrWhiteSpace( realignWarning ) ?
            "reset_pose_qpos_realign_failed" :
            $"reset_pose_qpos_realign_failed:{realignWarning}" );
        return;
      }

      if ( !string.IsNullOrWhiteSpace( realignWarning ) )
        warnings?.Add( realignWarning );

      var burnInSteps = Mathf.Clamp( m_resetPoseQposBurnInSteps, 0, 100 );
      for ( var stepIndex = 0; stepIndex < burnInSteps; ++stepIndex ) {
        if ( Simulation.HasInstance )
          Simulation.Instance.DoStep();
        else {
          warnings?.Add( "simulation_instance_missing" );
          break;
        }
      }

      if ( !m_machineController.TryRealignNormalizedPose( m_resetPoseQpos, out var settleWarning ) ) {
        warnings?.Add(
          string.IsNullOrWhiteSpace( settleWarning ) ?
            "reset_pose_qpos_settle_failed" :
            $"reset_pose_qpos_settle_failed:{settleWarning}" );
        return;
      }

      if ( !string.IsNullOrWhiteSpace( settleWarning ) )
        warnings?.Add( settleWarning );

      warnings?.Add( $"reset_pose_qpos_applied:burn_in_steps={burnInSteps}" );
    }

    private byte[] CreateStepResponse( AgxSimRequestPayload request )
    {
      if ( request == null )
        return CreateErrorResponse( AgxSimMessageType.StepResp, "missing_payload" );

      if ( request.action == null || request.action.Length < 4 )
        return CreateErrorResponse( AgxSimMessageType.StepResp, "action_dim_must_be_4" );

      var warnings = new List<string>();
      EnsureManualStepping( warnings );
      UpdatePlannerDebug( request.planner_debug_json, warnings );

      m_machineController?.ApplyActuationCommand( new ExcavatorActuationCommand
      {
        Swing = request.action[ 0 ],
        Boom = request.action[ 1 ],
        Stick = request.action[ 2 ],
        Bucket = request.action[ 3 ]
      }.ClampAxes() );

      if ( Simulation.HasInstance )
        Simulation.Instance.DoStep();
      else
        warnings.Add( "simulation_instance_missing" );

      var payload = CreateObservationStepPayload( request.step_id, warnings );
      RecordStepResponseDebug( payload );

      return AgxSimBinaryProtocol.SerializeResponse( AgxSimMessageType.StepResp, payload );
    }

    private byte[] CreateRealignPoseResponse( AgxSimRequestPayload request )
    {
      if ( request == null )
        return CreateErrorResponse( AgxSimMessageType.RealignPoseResp, "missing_payload" );

      if ( request.qpos == null || request.qpos.Length < 4 )
        return CreateErrorResponse( AgxSimMessageType.RealignPoseResp, "qpos_dim_must_be_4" );

      var warnings = new List<string>();
      EnsureManualStepping( warnings );

      if ( m_machineController == null ) {
        return CreateErrorResponse( AgxSimMessageType.RealignPoseResp, "machine_controller_missing" );
      }

      if ( !m_machineController.TryRealignNormalizedPose( request.qpos, out var realignWarning ) ) {
        var error = string.IsNullOrWhiteSpace( realignWarning ) ?
                    "pose_realign_failed" :
                    $"pose_realign_failed:{realignWarning}";
        return CreateErrorResponse( AgxSimMessageType.RealignPoseResp, error );
      }

      if ( !string.IsNullOrWhiteSpace( realignWarning ) )
        warnings.Add( realignWarning );

      var burnInSteps = Mathf.Clamp( request.burn_in_steps, 0, 100 );
      for ( var stepIndex = 0; stepIndex < burnInSteps; ++stepIndex ) {
        if ( Simulation.HasInstance )
          Simulation.Instance.DoStep();
        else {
          warnings.Add( "simulation_instance_missing" );
          break;
        }
      }

      warnings.Add( $"pose_realign_applied:burn_in_steps={burnInSteps}" );
      var payload = CreateObservationStepPayload( request.step_id, warnings );
      RecordStepResponseDebug( payload );

      return AgxSimBinaryProtocol.SerializeResponse( AgxSimMessageType.RealignPoseResp, payload );
    }

    private AgxSimResponsePayload CreateObservationStepPayload( long stepId, List<string> warnings )
    {
      var observation = m_observationCollector != null ?
                        m_observationCollector.Collect( OperatorCommand.Zero ) :
                        new ActObservation();

      var payload = CreateBasePayload();
      payload.step_id = stepId;
      payload.qpos = new[]
      {
        observation.actuator_state != null ? observation.actuator_state.swing_position_norm : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.boom_position_norm : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.stick_position_norm : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.bucket_position_norm : 0.0f
      };
      payload.qvel = new[]
      {
        observation.actuator_state != null ? observation.actuator_state.swing_speed : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.boom_speed : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.stick_speed : 0.0f,
        observation.actuator_state != null ? observation.actuator_state.bucket_speed : 0.0f
      };
      payload.env_state = new[]
      {
        observation.task_state != null ? observation.task_state.mass_in_bucket_kg : 0.0f,
        observation.task_state != null ? observation.task_state.excavated_mass_kg : 0.0f,
        observation.task_state != null ? observation.task_state.mass_in_target_box_kg : 0.0f,
        observation.task_state != null ? observation.task_state.deposited_mass_in_target_box_kg : 0.0f,
        observation.task_state != null ? observation.task_state.min_distance_to_target_m : -1.0f,
        observation.task_state != null ? observation.task_state.target_hard_collision_count : 0.0f,
        observation.task_state != null ? observation.task_state.target_contact_max_normal_force_n : 0.0f,
        observation.task_state != null ? observation.task_state.min_distance_to_dig_area_m : -1.0f,
        observation.task_state != null ? observation.task_state.bucket_depth_below_dig_area_plane_m : 0.0f,
        observation.task_state != null ? observation.task_state.target_horizontal_distance_m : -1.0f,
        observation.task_state != null ? observation.task_state.bucket_height_above_target_rim_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_over_target_footprint_mask : 0.0f,
        observation.task_state != null ? observation.task_state.dump_clearance_ok_mask : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dump_area_relative_x_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dump_area_relative_z_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dump_area_footprint_outside_distance_m : -1.0f,
        observation.task_state != null ? observation.task_state.bucket_dig_area_cell_in_bounds_mask : 0.0f,
        observation.task_state != null ? observation.task_state.dig_area_long_axis : -1.0f,
        observation.task_state != null ? observation.task_state.dig_area_grid_long_count : 3.0f,
        observation.task_state != null ? observation.task_state.dig_area_grid_short_count : 2.0f,
        observation.task_state != null ? observation.task_state.bucket_dig_area_relative_x_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dig_area_relative_y_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dig_area_relative_z_m : 0.0f,
        observation.task_state != null ? observation.task_state.bucket_dig_area_long_norm : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_dig_area_short_norm : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_dig_area_long_index : -1.0f,
	        observation.task_state != null ? observation.task_state.bucket_dig_area_short_index : -1.0f,
	        observation.task_state != null ? observation.task_state.bucket_dig_area_cell_id : -1.0f,
	        observation.task_state != null ? observation.task_state.bucket_tip_dig_area_x_m : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_tip_dig_area_y_m : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_tip_dig_area_z_m : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_depth_below_local_surface_m : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_depth_below_target_surface_m : 0.0f,
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 0 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 1 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 2 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 3 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 4 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_surface_depth_m, 5 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 0 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 1 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 2 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 3 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 4 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_removed_depth_m, 5 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 0 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 1 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 2 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 3 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 4 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_target_depth_m, 5 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 0 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 1 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 2 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 3 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 4 ),
	        TaskStateArrayValue( observation.task_state, state => state.dig_area_cell_valid_mask, 5 ),
	        observation.task_state != null ? observation.task_state.bucket_mass_delta_kg : 0.0f,
	        observation.task_state != null ? observation.task_state.deposited_mass_in_dump_area_kg : 0.0f,
	        observation.task_state != null ? observation.task_state.offtarget_deposited_mass_kg : -1.0f,
	        observation.task_state != null ? observation.task_state.target_geometry_available : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_dig_area_penetration_contact_mask : 0.0f,
	        observation.task_state != null ? observation.task_state.bucket_contact_dump_area_mask : 0.0f,
	        observation.task_state != null ? observation.task_state.hard_collision_count : 0.0f
	      };
      payload.reward = observation.task_state != null ? observation.task_state.deposited_mass_in_target_box_kg : 0.0f;
      payload.sim_time_ns = observation != null ? (long)Math.Round( observation.sim_time_sec * 1000000000.0 ) : -1;
      payload.image_fpv = CaptureImageFrame( warnings );
      payload.warnings = warnings.ToArray();
      return payload;
    }

    private void EnsureManualStepping( List<string> warnings )
    {
      if ( !Simulation.HasInstance ) {
        warnings?.Add( "simulation_instance_missing" );
        return;
      }

      var simulation = Simulation.Instance;
      if ( simulation == null )
        return;

      if ( simulation.AutoSteppingMode != Simulation.AutoSteppingModes.Disabled ) {
        simulation.AutoSteppingMode = Simulation.AutoSteppingModes.Disabled;
        warnings?.Add( "auto_stepping_disabled_by_server" );
      }
    }

    private AgxSimResponsePayload CreateBasePayload()
    {
      var dt = GetDt();
      return new AgxSimResponsePayload
      {
        dt = dt,
        control_hz = dt > 1.0e-6f ? 1.0f / dt : 0.0f,
        action_semantics = AgxSimProtocolConstants.ActionSemantics
      };
    }

    private AgxSimCameraDescriptor[] CreateCameraDescriptors()
    {
      if ( m_fpvCamera == null )
        return Array.Empty<AgxSimCameraDescriptor>();

      return new[]
      {
        new AgxSimCameraDescriptor
        {
          name = "fpv",
          width = m_fpvCamera.TextureWidth,
          height = m_fpvCamera.TextureHeight,
          fps = GetDt() > 1.0e-6f ? 1.0f / GetDt() : 0.0f,
          pixel_format = AgxSimProtocolConstants.ImagePixelFormat,
          row_order = AgxSimProtocolConstants.ImageRowOrder
        }
      };
    }

	    private AgxSimImageFrame CaptureImageFrame( List<string> warnings )
	    {
      if ( m_fpvCamera == null )
        return null;

      if ( !m_fpvCamera.TryCaptureRgb24( out var rgb24, out var width, out var height ) ) {
        warnings?.Add( "fpv_capture_failed" );
        return null;
      }

      return new AgxSimImageFrame
      {
        name = "fpv",
        width = width,
        height = height,
        pixel_format = AgxSimProtocolConstants.ImagePixelFormat,
        row_order = AgxSimProtocolConstants.ImageRowOrder,
        data = rgb24 ?? Array.Empty<byte>()
	      };
	    }

	    private static float TaskStateArrayValue( ActTaskState state,
	                                             Func<ActTaskState, float[]> selector,
	                                             int index,
	                                             float defaultValue = 0.0f )
	    {
	      if ( state == null || selector == null )
	        return defaultValue;

	      var values = selector( state );
	      if ( values == null || index < 0 || index >= values.Length )
	        return defaultValue;

	      return values[ index ];
	    }
	
	    private float GetDt()
    {
      if ( Simulation.HasInstance && Simulation.Instance != null )
        return Simulation.Instance.TimeStep;

      return Time.fixedDeltaTime;
    }

    private byte[] CreateErrorResponse( AgxSimMessageType responseType, string error )
    {
      var warnings = new[] { error ?? string.Empty };
      RecordCommonResponseDebug( false, error, warnings );
      if ( m_enableDebugLogs )
        Debug.LogWarning( $"AGX sim step-ack server {responseType} error={error}", this );

      return AgxSimBinaryProtocol.SerializeResponse( responseType, new AgxSimResponsePayload
      {
        error = error ?? string.Empty,
        success = false,
        warnings = warnings
      } );
    }

    private void QueueResponse( byte[] response )
    {
      if ( response == null || response.Length == 0 )
        return;

      m_pendingResponses.Enqueue( response );
    }

    private bool TryFlushPendingResponses( NetworkStream stream )
    {
      if ( stream == null || !stream.CanWrite )
        return false;

      try {
        while ( true ) {
          if ( !m_pendingResponses.TryDequeue( out var response ) )
            break;

          stream.Write( response, 0, response.Length );
        }

        return true;
      }
      catch ( SocketException ) {
        return false;
      }
      catch ( ObjectDisposedException ) {
        return false;
      }
      catch ( System.IO.IOException ) {
        return false;
      }
    }

    private static bool IsClientConnectionAlive( TcpClient client )
    {
      if ( client == null )
        return false;

      try {
        var socket = client.Client;
        if ( socket == null || !socket.Connected )
          return false;

        return !(socket.Poll( 0, SelectMode.SelectRead ) && socket.Available == 0);
      }
      catch ( SocketException ) {
        return false;
      }
      catch ( ObjectDisposedException ) {
        return false;
      }
    }

    private static AgxSimMessageType GetResponseTypeForRequest( AgxSimMessageType requestType )
    {
      switch ( requestType ) {
        case AgxSimMessageType.GetInfoReq:
          return AgxSimMessageType.GetInfoResp;
        case AgxSimMessageType.ResetReq:
          return AgxSimMessageType.ResetResp;
        case AgxSimMessageType.RealignPoseReq:
          return AgxSimMessageType.RealignPoseResp;
        case AgxSimMessageType.StepReq:
        default:
          return AgxSimMessageType.StepResp;
      }
    }

    private static void CloseClientConnection( ref NetworkStream stream, ref TcpClient client )
    {
      try {
        stream?.Dispose();
      }
      catch ( System.Exception ) {
      }
      finally {
        stream = null;
      }

      try {
        client?.Close();
      }
      catch ( System.Exception ) {
      }
      finally {
        client = null;
      }
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
      m_sceneResetService = ExcavatorRigLocator.ResolveComponent( this, m_sceneResetService );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );
      m_fpvCamera = ExcavatorRigLocator.ResolveComponent( this, m_fpvCamera );
    }

    private void RecordIncomingRequestDebug( PendingRequest request )
    {
      if ( request == null ) {
        m_lastRequestTypeName = "none";
        m_lastRequestStepId = -1;
        return;
      }

      m_lastRequestTypeName = request.Type.ToString();
      m_lastRequestStepId = (request.Type == AgxSimMessageType.StepReq ||
                             request.Type == AgxSimMessageType.RealignPoseReq) &&
                            request.Payload != null ?
                            request.Payload.step_id :
                            -1;

      if ( !m_enableDebugLogs )
        return;

      if ( request.Type != AgxSimMessageType.StepReq &&
           request.Type != AgxSimMessageType.RealignPoseReq ) {
        Debug.Log( $"AGX sim step-ack server recv msg_type={request.Type}", this );
        return;
      }

      if ( ShouldLogStepDebug( m_lastRequestStepId, false ) )
        Debug.Log( $"AGX sim step-ack server recv msg_type={request.Type} step_id={m_lastRequestStepId}", this );
    }

    private void RecordResetResponseDebug( bool resetApplied, bool success, string error, string[] warnings )
    {
      m_lastResetApplied = resetApplied;
      m_lastImageWidth = 0;
      m_lastImageHeight = 0;
      m_lastImagePayloadBytes = 0;
      RecordCommonResponseDebug( success, error, warnings );

      if ( m_enableDebugLogs ) {
        Debug.Log(
          $"AGX sim step-ack server reset_applied={resetApplied} warnings={FormatWarnings( warnings )}",
          this );
      }
    }

    private void RecordStepResponseDebug( AgxSimResponsePayload payload )
    {
      var image = payload != null ? payload.image_fpv : null;
      m_lastResetApplied = false;
      m_lastImageWidth = image != null ? image.width : 0;
      m_lastImageHeight = image != null ? image.height : 0;
      m_lastImagePayloadBytes = image != null && image.data != null ? image.data.Length : 0;
      RecordCommonResponseDebug( payload != null && payload.success,
                                 payload != null ? payload.error : string.Empty,
                                 payload != null ? payload.warnings : null );

      if ( !m_enableDebugLogs || payload == null )
        return;

      if ( ShouldLogStepDebug( payload.step_id, payload.warnings != null && payload.warnings.Length > 0 ) ) {
        Debug.Log(
          $"AGX sim step-ack server step_id={payload.step_id} image={m_lastImageWidth}x{m_lastImageHeight} payload_bytes={m_lastImagePayloadBytes} warnings={FormatWarnings( payload.warnings )}",
          this );
      }
    }

    private void RecordCommonResponseDebug( bool success, string error, string[] warnings )
    {
      m_lastResponseSuccess = success;
      m_lastError = error ?? string.Empty;
      m_lastWarningsSummary = FormatWarnings( warnings );
    }

    private void UpdatePlannerDebug( string plannerDebugJson, List<string> warnings )
    {
      if ( string.IsNullOrWhiteSpace( plannerDebugJson ) ) {
        m_lastPlannerDebug = PlannerDebugSnapshot.Empty();
        return;
      }

      try {
        var snapshot = JsonUtility.FromJson<PlannerDebugSnapshot>( plannerDebugJson );
        if ( snapshot == null ) {
          m_lastPlannerDebug = PlannerDebugSnapshot.ParseWarning( "planner_debug_json_parse_null" );
          warnings?.Add( "planner_debug_json_parse_null" );
          return;
        }

        snapshot.Normalize();
        m_lastPlannerDebug = snapshot;
      }
      catch ( System.Exception exception ) {
        var warning = $"planner_debug_json_parse_failed:{exception.Message}";
        m_lastPlannerDebug = PlannerDebugSnapshot.ParseWarning( warning );
        warnings?.Add( warning );
      }
    }

    private void EnsurePlannerVisualizer()
    {
      if ( !m_autoCreatePlannerVisualizer )
        return;
      if ( m_plannerDecisionVisualizer == null )
        m_plannerDecisionVisualizer = GetComponent<PlannerDecisionVisualizer>();
      if ( m_plannerDecisionVisualizer == null )
        m_plannerDecisionVisualizer = gameObject.AddComponent<PlannerDecisionVisualizer>();
      m_plannerDecisionVisualizer.Configure( this );
    }

    private bool ShouldLogStepDebug( long stepId, bool hasWarnings )
    {
      if ( hasWarnings )
        return true;

      if ( stepId <= 0 )
        return true;

      return m_stepDebugLogInterval > 0 && stepId % m_stepDebugLogInterval == 0;
    }

    private static string FormatWarnings( string[] warnings )
    {
      if ( warnings == null || warnings.Length == 0 )
        return "none";

      return string.Join( ", ", warnings );
    }
  }
}
