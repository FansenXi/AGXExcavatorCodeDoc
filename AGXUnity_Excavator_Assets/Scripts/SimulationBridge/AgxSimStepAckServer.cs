using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
      public string Type = string.Empty;
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
    private global::MassVolumeCounter m_massVolumeCounter = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private TrackedCameraWindow m_fpvCamera = null;

    private readonly ConcurrentQueue<PendingRequest> m_pendingRequests = new ConcurrentQueue<PendingRequest>();
    private readonly ConcurrentQueue<string> m_pendingResponses = new ConcurrentQueue<string>();

    private Thread m_serverThread = null;
    private volatile bool m_stopRequested = false;
    private bool m_isListening = false;
    private bool m_restoreEpisodeManagerEnabled = false;

    public bool IsListening => m_isListening;

    private void Awake()
    {
      ResolveReferences();
    }

    private void OnEnable()
    {
      ResolveReferences();
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
      StreamReader reader = null;
      StreamWriter writer = null;

      try {
        listener = new TcpListener( IPAddress.Any, m_port );
        listener.Start();
        m_isListening = true;

        while ( !m_stopRequested ) {
          if ( client == null ) {
            if ( !listener.Pending() ) {
              FlushPendingResponses( writer );
              Thread.Sleep( 5 );
              continue;
            }

            client = listener.AcceptTcpClient();
            client.NoDelay = true;
            var stream = client.GetStream();
            reader = new StreamReader( stream, Encoding.UTF8 );
            writer = new StreamWriter( stream, new UTF8Encoding( false ) )
            {
              AutoFlush = true
            };
          }

          if ( client.Connected && client.Client.Available > 0 ) {
            var line = reader.ReadLine();
            if ( string.IsNullOrWhiteSpace( line ) )
              continue;

            TryQueueIncomingRequest( line );
          }

          FlushPendingResponses( writer );
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

        try {
          reader?.Dispose();
        }
        catch ( System.Exception ) {
        }

        try {
          writer?.Dispose();
        }
        catch ( System.Exception ) {
        }

        try {
          client?.Close();
        }
        catch ( System.Exception ) {
        }

        try {
          listener?.Stop();
        }
        catch ( System.Exception ) {
        }
      }
    }

    private void TryQueueIncomingRequest( string line )
    {
      AgxSimRequestEnvelope request = null;
      try {
        request = JsonUtility.FromJson<AgxSimRequestEnvelope>( line );
      }
      catch ( System.Exception exception ) {
        QueueResponse( CreateErrorResponse( "error_resp", $"invalid_json:{exception.Message}" ) );
        return;
      }

      if ( request == null || string.IsNullOrWhiteSpace( request.type ) ) {
        QueueResponse( CreateErrorResponse( "error_resp", "invalid_request" ) );
        return;
      }

      m_pendingRequests.Enqueue( new PendingRequest
      {
        Type = request.type,
        Payload = request.payload ?? new AgxSimRequestPayload()
      } );
    }

    private void ProcessPendingRequests()
    {
      while ( m_pendingRequests.TryDequeue( out var request ) ) {
        switch ( request.Type ) {
          case "get_info_req":
            QueueResponse( CreateInfoResponse() );
            break;
          case "reset_req":
            QueueResponse( CreateResetResponse( request.Payload ) );
            break;
          case "step_req":
            QueueResponse( CreateStepResponse( request.Payload ) );
            break;
          default:
            QueueResponse( CreateErrorResponse( "error_resp", $"unsupported_type:{request.Type}" ) );
            break;
        }
      }
    }

    private string CreateInfoResponse()
    {
      var payload = CreateBasePayload();
      payload.action_order = new[] { "swing_speed_cmd", "boom_speed_cmd", "stick_speed_cmd", "bucket_speed_cmd" };
      payload.qpos_order = new[] { "boom_position_norm", "stick_position_norm", "bucket_position_norm" };
      payload.qvel_order = new[] { "swing_speed", "boom_speed", "stick_speed", "bucket_speed" };
      payload.env_state_order = new[] { "mass_in_bucket_kg" };
      payload.camera_names = m_fpvCamera != null ? new[] { "fpv" } : Array.Empty<string>();
      payload.supports_images = false;
      payload.supports_reset_pose = false;

      return JsonUtility.ToJson( new AgxSimResponseEnvelope
      {
        type = "get_info_resp",
        payload = payload
      } );
    }

    private string CreateResetResponse( AgxSimRequestPayload request )
    {
      var warnings = new List<string>();
      EnsureManualStepping( warnings );

      m_machineController?.StopMotion();
      m_observationCollector?.ResetSampling();

      if ( request != null && request.reset_terrain )
        m_sceneResetService?.ResetScene();

      if ( request != null && request.reset_pose )
        warnings.Add( "reset_pose_not_implemented_yet" );

      var payload = CreateBasePayload();
      payload.warnings = warnings.ToArray();

      return JsonUtility.ToJson( new AgxSimResponseEnvelope
      {
        type = "reset_resp",
        payload = payload
      } );
    }

    private string CreateStepResponse( AgxSimRequestPayload request )
    {
      if ( request == null )
        return CreateErrorResponse( "step_resp", "missing_payload" );

      if ( request.action == null || request.action.Length < 4 )
        return CreateErrorResponse( "step_resp", "action_dim_must_be_4" );

      var warnings = new List<string>();
      EnsureManualStepping( warnings );

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

      var observation = m_observationCollector != null ?
                        m_observationCollector.Collect( OperatorCommand.Zero ) :
                        new ActObservation();

      var payload = CreateBasePayload();
      payload.step_id = request.step_id;
      payload.qpos = new[]
      {
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
        observation.task_state != null ? observation.task_state.mass_in_bucket_kg : 0.0f
      };
      payload.reward = 0.0f;
      payload.sim_time_sec = observation.sim_time_sec;
      payload.warnings = warnings.ToArray();

      return JsonUtility.ToJson( new AgxSimResponseEnvelope
      {
        type = "step_resp",
        payload = payload
      } );
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

    private float GetDt()
    {
      if ( Simulation.HasInstance && Simulation.Instance != null )
        return Simulation.Instance.TimeStep;

      return Time.fixedDeltaTime;
    }

    private static string CreateErrorResponse( string type, string error )
    {
      return JsonUtility.ToJson( new AgxSimResponseEnvelope
      {
        type = type,
        status = "error",
        error = error ?? string.Empty,
        payload = new AgxSimResponsePayload
        {
          warnings = new[] { error ?? string.Empty }
        }
      } );
    }

    private void QueueResponse( string response )
    {
      if ( string.IsNullOrWhiteSpace( response ) )
        return;

      m_pendingResponses.Enqueue( response );
    }

    private void FlushPendingResponses( StreamWriter writer )
    {
      if ( writer == null )
        return;

      while ( true ) {
        if ( !m_pendingResponses.TryDequeue( out var response ) )
          break;

        writer.WriteLine( response );
      }
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
      m_sceneResetService = ExcavatorRigLocator.ResolveComponent( this, m_sceneResetService );
      m_massVolumeCounter = ExcavatorRigLocator.ResolveComponent( this, m_massVolumeCounter );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );
      m_fpvCamera = ExcavatorRigLocator.ResolveComponent( this, m_fpvCamera );
    }
  }
}
