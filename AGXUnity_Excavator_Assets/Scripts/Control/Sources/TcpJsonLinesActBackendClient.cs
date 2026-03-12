using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  public class TcpJsonLinesActBackendClient : ActBackendClientBehaviour
  {
    [SerializeField]
    private string m_host = "127.0.0.1";

    [SerializeField]
    private int m_port = 5055;

    [SerializeField]
    private bool m_connectOnEnable = true;

    [SerializeField]
    private bool m_autoReconnect = true;

    [SerializeField]
    private int m_connectTimeoutMs = 1000;

    [SerializeField]
    private int m_reconnectDelayMs = 1000;

    private readonly object m_queueLock = new object();
    private readonly object m_responseLock = new object();
    private readonly Queue<string> m_outboundMessages = new Queue<string>();

    private Thread m_workerThread = null;
    private volatile bool m_stopRequested = false;
    private TcpClient m_client = null;
    private StreamReader m_reader = null;
    private StreamWriter m_writer = null;
    private bool m_isReady = false;
    private bool m_hasFreshResponse = false;
    private bool m_helloQueued = false;
    private ActStepResponse m_latestResponse;

    public override bool IsReady => m_isReady;

    private void OnEnable()
    {
      if ( m_connectOnEnable )
        StartWorker();
    }

    private void OnDisable()
    {
      StopWorker();
    }

    private void OnDestroy()
    {
      StopWorker();
    }

    public override void BeginEpisode( ActEpisodeConfig config, string sessionId )
    {
      StartWorker();

      var message = new ActResetMessage
      {
        session_id = sessionId,
        seq = 0,
        payload = new ActResetPayload
        {
          task_name = config != null ? config.task_name : "excavator_dig_v1",
          seed = config != null ? config.seed : 0,
          fixed_dt_sec = config != null ? config.fixed_dt_sec : Time.fixedDeltaTime,
          observation_rate_hz = config != null ? config.observation_rate_hz : 20.0f,
          command_timeout_ms = config != null ? config.command_timeout_ms : 200
        }
      };

      EnqueueMessage( JsonUtility.ToJson( message ) );
    }

    public override void EndEpisode( string reason, string sessionId, int seq )
    {
      if ( string.IsNullOrEmpty( sessionId ) )
        return;

      var message = new ActCloseMessage
      {
        session_id = sessionId,
        seq = Mathf.Max( 0, seq ),
        payload = new ActClosePayload
        {
          reason = string.IsNullOrWhiteSpace( reason ) ? "episode_end" : reason
        }
      };

      EnqueueMessage( JsonUtility.ToJson( message ) );
    }

    public override void SubmitObservation( ActStepRequest request )
    {
      StartWorker();

      var message = new ActStepMessage
      {
        session_id = request.SessionId,
        seq = request.Seq,
        payload = new ActStepPayload
        {
          sim_time_sec = request.Observation != null ? request.Observation.sim_time_sec : Time.time,
          fixed_dt_sec = request.Observation != null ? request.Observation.fixed_dt_sec : Time.fixedDeltaTime,
          observation = request.Observation ?? new ActObservation()
        }
      };

      EnqueueMessage( JsonUtility.ToJson( message ) );
    }

    public override bool TryGetLatestResult( out ActStepResponse response )
    {
      lock ( m_responseLock ) {
        if ( !m_hasFreshResponse ) {
          response = new ActStepResponse();
          return false;
        }

        response = m_latestResponse;
        m_hasFreshResponse = false;
        return true;
      }
    }

    private void StartWorker()
    {
      if ( m_workerThread != null )
        return;

      m_stopRequested = false;
      m_workerThread = new Thread( WorkerLoop )
      {
        IsBackground = true,
        Name = "ACT-TCP-Client"
      };
      m_workerThread.Start();
    }

    private void StopWorker()
    {
      m_stopRequested = true;

      if ( m_workerThread != null ) {
        if ( !m_workerThread.Join( 500 ) )
          m_workerThread.Interrupt();

        m_workerThread = null;
      }

      Disconnect();
    }

    private void WorkerLoop()
    {
      while ( !m_stopRequested ) {
        try {
          EnsureConnected();
          FlushPendingMessages();
          ReadIncomingMessages();
          Thread.Sleep( 5 );
        }
        catch ( ThreadInterruptedException ) {
          break;
        }
        catch ( Exception ) {
          Disconnect();
          if ( !m_autoReconnect )
            break;

          Thread.Sleep( Mathf.Max( 100, m_reconnectDelayMs ) );
        }
      }

      Disconnect();
    }

    private void EnsureConnected()
    {
      if ( m_client != null && m_client.Connected )
        return;

      Disconnect();

      var client = new TcpClient
      {
        NoDelay = true
      };

      var connectResult = client.BeginConnect( m_host, m_port, null, null );
      if ( !connectResult.AsyncWaitHandle.WaitOne( Mathf.Max( 100, m_connectTimeoutMs ) ) ) {
        client.Close();
        throw new TimeoutException( $"Timed out connecting to {m_host}:{m_port}." );
      }

      client.EndConnect( connectResult );
      var stream = client.GetStream();
      m_client = client;
      m_reader = new StreamReader( stream, Encoding.UTF8 );
      m_writer = new StreamWriter( stream, new UTF8Encoding( false ) )
      {
        AutoFlush = true
      };
      m_isReady = true;

      if ( !m_helloQueued ) {
        m_writer.WriteLine( JsonUtility.ToJson( new ActHelloMessage() ) );
        m_helloQueued = true;
      }
    }

    private void FlushPendingMessages()
    {
      if ( m_writer == null )
        return;

      while ( true ) {
        string line = null;
        lock ( m_queueLock ) {
          if ( m_outboundMessages.Count > 0 )
            line = m_outboundMessages.Dequeue();
        }

        if ( string.IsNullOrEmpty( line ) )
          break;

        m_writer.WriteLine( line );
      }
    }

    private void ReadIncomingMessages()
    {
      if ( m_client == null || m_reader == null )
        return;

      var socket = m_client.Client;
      while ( socket != null && socket.Available > 0 ) {
        var line = m_reader.ReadLine();
        if ( string.IsNullOrWhiteSpace( line ) )
          return;

        HandleIncomingLine( line );
      }
    }

    private void HandleIncomingLine( string line )
    {
      var envelope = JsonUtility.FromJson<ActWireEnvelopeBase>( line );
      if ( envelope == null || envelope.type != "step_result" )
        return;

      var message = JsonUtility.FromJson<ActStepResultMessage>( line );
      if ( message == null || message.payload == null )
        return;

      lock ( m_responseLock ) {
        m_latestResponse = new ActStepResponse
        {
          SessionId = message.session_id,
          Seq = message.seq,
          Status = message.payload.status,
          OperatorCommand = message.payload.operator_command != null ?
                            message.payload.operator_command.ToOperatorCommand() :
                            Control.Core.OperatorCommand.Zero,
          InferenceTimeMs = message.payload.inference_time_ms,
          ModelTimeSec = message.payload.model_time_sec,
          HasValue = true
        };
        m_hasFreshResponse = true;
      }
    }

    private void EnqueueMessage( string message )
    {
      if ( string.IsNullOrWhiteSpace( message ) )
        return;

      lock ( m_queueLock ) {
        m_outboundMessages.Enqueue( message );
      }
    }

    private void Disconnect()
    {
      m_isReady = false;
      m_helloQueued = false;

      try {
        m_reader?.Dispose();
      }
      catch ( Exception ) {
      }

      try {
        m_writer?.Dispose();
      }
      catch ( Exception ) {
      }

      try {
        m_client?.Close();
      }
      catch ( Exception ) {
      }

      m_reader = null;
      m_writer = null;
      m_client = null;
    }
  }
}
