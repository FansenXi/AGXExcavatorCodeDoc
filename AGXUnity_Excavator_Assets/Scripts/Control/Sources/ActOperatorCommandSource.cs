using System.Globalization;
using AGXUnity_Excavator.Scripts.Control.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Sources
{
  [RequireComponent( typeof( ActObservationCollector ) )]
  public class ActOperatorCommandSource : OperatorCommandSourceBehaviour, IEpisodeLifecycleAware, IActCommandDiagnostics
  {
    [SerializeField]
    private string m_taskName = "excavator_dig_v1";

    [SerializeField]
    [Min( 1.0f )]
    private float m_observationRateHz = 20.0f;

    [SerializeField]
    [Min( 1 )]
    private int m_commandTimeoutMs = 200;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    [SerializeField]
    private ActBackendClientBehaviour m_backendClient = null;

    [SerializeField]
    private bool m_logInvalidResponses = true;

    private bool m_episodeActive = false;
    private string m_sessionId = string.Empty;
    private int m_nextSequence = 1;
    private float m_nextObservationTime = 0.0f;
    private float m_lastValidResponseTime = float.NegativeInfinity;
    private OperatorCommand m_latestValidCommand = OperatorCommand.Zero;
    private string m_lastBackendStatus = "idle";
    private int m_lastResponseSequence = -1;
    private float m_lastInferenceTimeMs = 0.0f;

    public override string SourceName => "ACT";

    public bool IsBackendReady => m_backendClient != null && m_backendClient.IsReady;
    public bool IsCommandTimedOut => m_episodeActive && Time.realtimeSinceStartup - m_lastValidResponseTime > m_commandTimeoutMs * 0.001f;
    public int LastResponseSequence => m_lastResponseSequence;
    public float LastInferenceTimeMs => m_lastInferenceTimeMs;
    public string CurrentSessionId => m_sessionId;
    public string LastBackendStatus => m_lastBackendStatus;

    private void Awake()
    {
      ResolveReferences();
    }

    public override OperatorCommand ReadCommand()
    {
      ResolveReferences();
      PollBackend();

      if ( !m_episodeActive )
        return OperatorCommand.Zero;

      SubmitObservationIfDue();
      if ( !IsBackendReady )
        return OperatorCommand.Zero;

      return IsCommandTimedOut ? OperatorCommand.Zero : m_latestValidCommand;
    }

    public void OnEpisodeStarted( EpisodeCommandSourceContext context )
    {
      ResolveReferences();

      m_episodeActive = true;
      m_sessionId = string.Format( CultureInfo.InvariantCulture, "ep_{0:000000}", context.EpisodeIndex );
      m_nextSequence = 1;
      m_nextObservationTime = Time.realtimeSinceStartup;
      m_lastValidResponseTime = float.NegativeInfinity;
      m_latestValidCommand = OperatorCommand.Zero;
      m_lastBackendStatus = "waiting_reset";
      m_lastResponseSequence = -1;
      m_lastInferenceTimeMs = 0.0f;
      m_observationCollector?.ResetSampling();

      if ( m_backendClient != null ) {
        m_backendClient.BeginEpisode(
          new ActEpisodeConfig
          {
            task_name = m_taskName,
            seed = context.EpisodeIndex,
            fixed_dt_sec = context.FixedDeltaTimeSec,
            observation_rate_hz = m_observationRateHz,
            command_timeout_ms = m_commandTimeoutMs
          },
          m_sessionId );
      }
    }

    public void OnEpisodeStopped( string reason )
    {
      if ( m_backendClient != null && !string.IsNullOrEmpty( m_sessionId ) )
        m_backendClient.EndEpisode( reason, m_sessionId, Mathf.Max( 0, m_nextSequence - 1 ) );

      m_episodeActive = false;
      m_nextSequence = 1;
      m_nextObservationTime = 0.0f;
      m_lastValidResponseTime = float.NegativeInfinity;
      m_latestValidCommand = OperatorCommand.Zero;
      m_lastBackendStatus = string.IsNullOrWhiteSpace( reason ) ? "stopped" : reason;
      m_sessionId = string.Empty;
      m_observationCollector?.ResetSampling();
    }

    private void ResolveReferences()
    {
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );
      m_backendClient = ExcavatorRigLocator.ResolveComponent( this, m_backendClient );
    }

    private void SubmitObservationIfDue()
    {
      if ( m_backendClient == null || m_observationCollector == null )
        return;

      var now = Time.realtimeSinceStartup;
      if ( now + 1.0e-5f < m_nextObservationTime )
        return;

      var observation = m_observationCollector.Collect( m_latestValidCommand );
      m_backendClient.SubmitObservation(
        new ActStepRequest
        {
          SessionId = m_sessionId,
          Seq = m_nextSequence++,
          Observation = observation
        } );

      m_nextObservationTime = now + 1.0f / Mathf.Max( 1.0f, m_observationRateHz );
      if ( m_lastBackendStatus == "waiting_reset" )
        m_lastBackendStatus = "running";
    }

    private void PollBackend()
    {
      if ( m_backendClient == null )
        return;

      while ( m_backendClient.TryGetLatestResult( out var response ) ) {
        if ( !response.HasValue )
          continue;

        if ( response.SessionId != m_sessionId || response.Seq < 0 ) {
          if ( m_logInvalidResponses )
            Debug.LogWarning( $"Ignoring ACT response for unexpected session/seq: {response.SessionId}#{response.Seq}.", this );
          continue;
        }

        m_lastBackendStatus = string.IsNullOrWhiteSpace( response.Status ) ? "unknown" : response.Status;
        m_lastResponseSequence = response.Seq;
        m_lastInferenceTimeMs = response.InferenceTimeMs;

        if ( response.Status != "ok" ) {
          if ( m_logInvalidResponses )
            Debug.LogWarning( $"Ignoring ACT response with status '{response.Status}' for {response.SessionId}#{response.Seq}.", this );
          continue;
        }

        if ( !TrySanitizeOperatorCommand( response.OperatorCommand, out var sanitizedCommand ) ) {
          if ( m_logInvalidResponses )
            Debug.LogWarning( $"Ignoring ACT response containing NaN/Inf for {response.SessionId}#{response.Seq}.", this );
          continue;
        }

        m_latestValidCommand = sanitizedCommand.WithoutEpisodeSignals().ClampAxes();
        m_lastValidResponseTime = Time.realtimeSinceStartup;
      }
    }

    private static bool TrySanitizeOperatorCommand( OperatorCommand candidate, out OperatorCommand sanitizedCommand )
    {
      sanitizedCommand = candidate.WithoutEpisodeSignals();
      if ( !IsFinite( sanitizedCommand.LeftStickX ) ||
           !IsFinite( sanitizedCommand.LeftStickY ) ||
           !IsFinite( sanitizedCommand.RightStickX ) ||
           !IsFinite( sanitizedCommand.RightStickY ) ||
           !IsFinite( sanitizedCommand.Drive ) ||
           !IsFinite( sanitizedCommand.Steer ) ) {
        sanitizedCommand = OperatorCommand.Zero;
        return false;
      }

      sanitizedCommand = sanitizedCommand.ClampAxes();
      return true;
    }

    private static bool IsFinite( float value )
    {
      return !float.IsNaN( value ) && !float.IsInfinity( value );
    }
  }
}
