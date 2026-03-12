using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Simulation;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class EpisodeManager : MonoBehaviour
  {
    [SerializeField]
    private bool m_autoStartOnPlay = true;

    [SerializeField]
    private bool m_autoRestartAfterReset = true;

    [SerializeField]
    private OperatorCommandSourceBehaviour m_commandSource = null;

    [SerializeField]
    private OperatorCommandSimulator m_commandSimulator = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private SceneResetService m_sceneResetService = null;

    [SerializeField]
    private ExperimentLogger m_logger = null;

    [SerializeField]
    private global::MassVolumeCounter m_massVolumeCounter = null;

    [SerializeField]
    private ExcavatorCommandInterpreter m_interpreter = new ExcavatorCommandInterpreter();

    private int m_nextEpisodeIndex = 1;
    private bool m_massVolumeCounterResolved = false;

    public int CurrentEpisodeIndex { get; private set; }
    public bool IsEpisodeRunning { get; private set; }
    public OperatorCommand LastRawCommand { get; private set; }
    public OperatorCommand LastSimulatedCommand { get; private set; }
    public ExcavatorActuationCommand LastActuationCommand { get; private set; }

    public string CurrentSourceName => m_commandSource != null ? m_commandSource.SourceName : "None";
    public string LastSavedPath => m_logger != null ? m_logger.LastSavedPath : string.Empty;
    public float MassInBucket => m_massVolumeCounter != null ? m_massVolumeCounter.MassInBucket : 0.0f;
    public float ExcavatedMass => m_massVolumeCounter != null ? m_massVolumeCounter.ExcavatedMass : 0.0f;
    public float ExcavatedVolume => m_massVolumeCounter != null ? m_massVolumeCounter.ExcavatedVolume : 0.0f;

    private void Awake()
    {
      ResolveReferences();
      DisableLegacyExcavatorInputControllers();
    }

    private void Start()
    {
      if ( m_autoStartOnPlay )
        StartEpisode();
    }

    private void Update()
    {
      ResolveReferences();

      LastRawCommand = m_commandSource != null ? m_commandSource.ReadCommand() : OperatorCommand.Zero;

      if ( LastRawCommand.StartEpisodeRequested )
        StartEpisode();

      if ( LastRawCommand.StopEpisodeRequested )
        StopEpisode( "manual_stop" );

      if ( LastRawCommand.ResetRequested ) {
        ResetEpisode();
        return;
      }

      var controlCommand = IsEpisodeRunning ? LastRawCommand.WithoutEpisodeSignals() : OperatorCommand.Zero;
      LastSimulatedCommand = m_commandSimulator != null ?
                             m_commandSimulator.Simulate( controlCommand, Time.deltaTime ) :
                             controlCommand;
      LastActuationCommand = m_interpreter.Interpret( LastSimulatedCommand );

      if ( m_machineController != null )
        m_machineController.ApplyActuationCommand( LastActuationCommand );

      if ( IsEpisodeRunning && m_logger != null ) {
        m_logger.RecordFrame(
          Time.time,
          LastRawCommand,
          LastSimulatedCommand,
          LastActuationCommand,
          m_machineController != null ? m_machineController.BucketReference : null,
          m_massVolumeCounter );
      }
    }

    public void StartEpisode()
    {
      ResolveReferences();

      if ( IsEpisodeRunning )
        StopEpisode( "restart" );

      CurrentEpisodeIndex = m_nextEpisodeIndex++;
      IsEpisodeRunning = true;
      m_commandSimulator?.ResetState();
      if ( m_commandSource is IEpisodeLifecycleAware lifecycleAware ) {
        lifecycleAware.OnEpisodeStarted( new EpisodeCommandSourceContext
        {
          EpisodeIndex = CurrentEpisodeIndex,
          FixedDeltaTimeSec = Time.fixedDeltaTime
        } );
      }

      m_logger?.BeginEpisode( CurrentEpisodeIndex, CurrentSourceName );
    }

    public void StopEpisode( string reason )
    {
      if ( !IsEpisodeRunning ) {
        m_machineController?.StopMotion();
        LastSimulatedCommand = OperatorCommand.Zero;
        LastActuationCommand = ExcavatorActuationCommand.Zero;
        return;
      }

      IsEpisodeRunning = false;
      m_machineController?.StopMotion();
      m_commandSimulator?.ResetState();
      if ( m_commandSource is IEpisodeLifecycleAware lifecycleAware )
        lifecycleAware.OnEpisodeStopped( reason );

      LastSimulatedCommand = OperatorCommand.Zero;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      m_logger?.EndEpisode( reason );
    }

    public void ResetEpisode()
    {
      StopEpisode( "reset" );
      m_sceneResetService?.ResetScene();

      if ( m_autoRestartAfterReset )
        StartEpisode();
    }

    private void ResolveReferences()
    {
      if ( m_commandSource == null )
        m_commandSource = GetComponent<OperatorCommandSourceBehaviour>();

      if ( m_commandSimulator == null )
        m_commandSimulator = GetComponent<OperatorCommandSimulator>();

      if ( m_machineController == null )
        m_machineController = GetComponent<ExcavatorMachineController>();

      if ( m_sceneResetService == null )
        m_sceneResetService = GetComponent<SceneResetService>();

      if ( m_logger == null )
        m_logger = GetComponent<ExperimentLogger>();

      if ( m_massVolumeCounter == null && !m_massVolumeCounterResolved )
        m_massVolumeCounter = FindObjectOfType<global::MassVolumeCounter>();

      m_massVolumeCounterResolved = true;
    }

    private void DisableLegacyExcavatorInputControllers()
    {
      var legacyControllers = FindObjectsByType<global::AGXUnity_Excavator.Scripts.ExcavatorInputController>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );

      foreach ( var legacyController in legacyControllers ) {
        if ( legacyController == null || !legacyController.enabled )
          continue;

        legacyController.enabled = false;
        Debug.LogWarning(
          $"Disabled legacy ExcavatorInputController on '{legacyController.gameObject.name}' because EpisodeManager is using the new control chain.",
          legacyController );
      }
    }

    public bool CurrentSourceBackendReady => ( m_commandSource as IActCommandDiagnostics )?.IsBackendReady ?? false;
    public bool CurrentSourceTimedOut => ( m_commandSource as IActCommandDiagnostics )?.IsCommandTimedOut ?? false;
    public int CurrentSourceSequence => ( m_commandSource as IActCommandDiagnostics )?.LastResponseSequence ?? -1;
    public float CurrentSourceInferenceTimeMs => ( m_commandSource as IActCommandDiagnostics )?.LastInferenceTimeMs ?? 0.0f;
    public string CurrentSourceSessionId => ( m_commandSource as IActCommandDiagnostics )?.CurrentSessionId ?? string.Empty;
    public string CurrentSourceBackendStatus => ( m_commandSource as IActCommandDiagnostics )?.LastBackendStatus ?? string.Empty;
  }
}
