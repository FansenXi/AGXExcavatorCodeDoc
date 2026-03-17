using System;
using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Simulation;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

    private OperatorCommandSourceBehaviour[] m_availableSources = Array.Empty<OperatorCommandSourceBehaviour>();
    private int m_nextEpisodeIndex = 1;
    private bool m_massVolumeCounterResolved = false;

    public int CurrentEpisodeIndex { get; private set; }
    public bool IsEpisodeRunning { get; private set; }
    public OperatorCommand LastRawCommand { get; private set; }
    public OperatorCommand LastSimulatedCommand { get; private set; }
    public ExcavatorActuationCommand LastActuationCommand { get; private set; }

    public string CurrentSourceName => m_commandSource != null ? m_commandSource.SourceName : "None";
    public string CurrentControlLayout => m_interpreter != null ? m_interpreter.LayoutDescription : string.Empty;
    public string LastSavedPath => m_logger != null ? m_logger.LastSavedPath : string.Empty;
    public float MassInBucket => m_massVolumeCounter != null ? m_massVolumeCounter.MassInBucket : 0.0f;
    public float ExcavatedMass => m_massVolumeCounter != null ? m_massVolumeCounter.ExcavatedMass : 0.0f;
    public float ExcavatedVolume => m_massVolumeCounter != null ? m_massVolumeCounter.ExcavatedVolume : 0.0f;
    public int AvailableSourceCount => m_availableSources != null ? m_availableSources.Length : 0;
    public int CurrentSourceIndex => GetSourceIndex( m_commandSource );

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
      HandleSourceSwitchHotkeys();

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
        var hardwareDiagnostics = m_commandSource as IHardwareCommandDiagnostics;
        m_logger.RecordFrame(
          Time.time,
          LastRawCommand,
          LastSimulatedCommand,
          LastActuationCommand,
          hardwareDiagnostics,
          m_machineController != null ? m_machineController.BucketReference : null,
          m_massVolumeCounter );
      }
    }

    public OperatorCommandSourceBehaviour GetAvailableSource( int index )
    {
      ResolveReferences();
      return index >= 0 && index < AvailableSourceCount ? m_availableSources[ index ] : null;
    }

    public string GetAvailableSourceDisplayName( int index )
    {
      var source = GetAvailableSource( index );
      if ( source == null )
        return string.Empty;

      var duplicateCount = 0;
      for ( var sourceIndex = 0; sourceIndex < AvailableSourceCount; ++sourceIndex ) {
        var otherSource = m_availableSources[ sourceIndex ];
        if ( otherSource != null && otherSource.SourceName == source.SourceName )
          ++duplicateCount;
      }

      return duplicateCount > 1 ? $"{source.SourceName} ({source.gameObject.name})" : source.SourceName;
    }

    public void RefreshAvailableSources()
    {
      var discoveredSources = DiscoverAvailableSources();
      m_availableSources = discoveredSources;

      if ( AvailableSourceCount == 0 ) {
        m_commandSource = null;
        return;
      }

      if ( m_commandSource == null ||
           GetSourceIndex( m_commandSource ) < 0 ||
           ShouldPromotePreferredSource( m_commandSource, discoveredSources ) )
        m_commandSource = ChooseDefaultSource( discoveredSources );

      SyncSourceEnabledStates();
    }

    public bool SetCommandSourceByIndex( int index, bool restartEpisodeIfRunning = true )
    {
      var source = GetAvailableSource( index );
      return SetCommandSource( source, restartEpisodeIfRunning );
    }

    public bool CycleCommandSource( int direction, bool restartEpisodeIfRunning = true )
    {
      ResolveReferences();
      if ( AvailableSourceCount <= 1 )
        return false;

      var normalizedDirection = direction < 0 ? -1 : 1;
      var currentIndex = CurrentSourceIndex >= 0 ? CurrentSourceIndex : 0;
      var targetIndex = ( currentIndex + normalizedDirection + AvailableSourceCount ) % AvailableSourceCount;
      return SetCommandSourceByIndex( targetIndex, restartEpisodeIfRunning );
    }

    public bool SetCommandSource( OperatorCommandSourceBehaviour source, bool restartEpisodeIfRunning = true )
    {
      ResolveReferences();
      if ( source == null || source == m_commandSource )
        return false;

      if ( GetSourceIndex( source ) < 0 ) {
        RefreshAvailableSources();
        if ( GetSourceIndex( source ) < 0 )
          return false;
      }

      var shouldRestartEpisode = restartEpisodeIfRunning && IsEpisodeRunning;
      if ( shouldRestartEpisode )
        StopEpisode( "source_switch" );
      else
        m_machineController?.StopMotion();

      m_commandSource = source;
      SyncSourceEnabledStates();

      LastRawCommand = OperatorCommand.Zero;
      LastSimulatedCommand = OperatorCommand.Zero;
      LastActuationCommand = ExcavatorActuationCommand.Zero;

      if ( shouldRestartEpisode )
        StartEpisode();

      return true;
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

    private void HandleSourceSwitchHotkeys()
    {
      if ( AvailableSourceCount <= 1 )
        return;

      var requestedIndex = GetRequestedSourceIndexHotkey();
      if ( requestedIndex >= 0 && requestedIndex < AvailableSourceCount ) {
        SetCommandSourceByIndex( requestedIndex );
        return;
      }

      var cycleDirection = GetSourceCycleDirectionHotkey();
      if ( cycleDirection != 0 )
        CycleCommandSource( cycleDirection );
    }

    private static int GetSourceCycleDirectionHotkey()
    {
#if ENABLE_INPUT_SYSTEM
      var keyboard = Keyboard.current;
      if ( keyboard != null ) {
        if ( keyboard.f6Key.wasPressedThisFrame )
          return -1;

        if ( keyboard.f7Key.wasPressedThisFrame )
          return 1;

        return 0;
      }
#endif

      if ( Input.GetKeyDown( KeyCode.F6 ) )
        return -1;

      if ( Input.GetKeyDown( KeyCode.F7 ) )
        return 1;

      return 0;
    }

    private static int GetRequestedSourceIndexHotkey()
    {
#if ENABLE_INPUT_SYSTEM
      var keyboard = Keyboard.current;
      if ( keyboard != null )
        return GetRequestedSourceIndexFromKeyboard( keyboard );
#endif

      if ( Input.GetKeyDown( KeyCode.Alpha1 ) )
        return 0;
      if ( Input.GetKeyDown( KeyCode.Alpha2 ) )
        return 1;
      if ( Input.GetKeyDown( KeyCode.Alpha3 ) )
        return 2;
      if ( Input.GetKeyDown( KeyCode.Alpha4 ) )
        return 3;
      if ( Input.GetKeyDown( KeyCode.Alpha5 ) )
        return 4;
      if ( Input.GetKeyDown( KeyCode.Alpha6 ) )
        return 5;
      if ( Input.GetKeyDown( KeyCode.Alpha7 ) )
        return 6;
      if ( Input.GetKeyDown( KeyCode.Alpha8 ) )
        return 7;
      if ( Input.GetKeyDown( KeyCode.Alpha9 ) )
        return 8;

      return -1;
    }

#if ENABLE_INPUT_SYSTEM
    private static int GetRequestedSourceIndexFromKeyboard( Keyboard keyboard )
    {
      if ( keyboard == null )
        return -1;

      if ( keyboard.digit1Key.wasPressedThisFrame )
        return 0;
      if ( keyboard.digit2Key.wasPressedThisFrame )
        return 1;
      if ( keyboard.digit3Key.wasPressedThisFrame )
        return 2;
      if ( keyboard.digit4Key.wasPressedThisFrame )
        return 3;
      if ( keyboard.digit5Key.wasPressedThisFrame )
        return 4;
      if ( keyboard.digit6Key.wasPressedThisFrame )
        return 5;
      if ( keyboard.digit7Key.wasPressedThisFrame )
        return 6;
      if ( keyboard.digit8Key.wasPressedThisFrame )
        return 7;
      if ( keyboard.digit9Key.wasPressedThisFrame )
        return 8;

      return -1;
    }
#endif

    private void ResolveReferences()
    {
      if ( AvailableSourceCount == 0 || m_commandSource == null || GetSourceIndex( m_commandSource ) < 0 )
        RefreshAvailableSources();

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

    private OperatorCommandSourceBehaviour[] DiscoverAvailableSources()
    {
      var discoveredSources = FindObjectsByType<OperatorCommandSourceBehaviour>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None );

      if ( discoveredSources == null || discoveredSources.Length == 0 )
        return Array.Empty<OperatorCommandSourceBehaviour>();

      var sameRootSources = new List<OperatorCommandSourceBehaviour>();
      var otherSources = new List<OperatorCommandSourceBehaviour>();
      foreach ( var source in discoveredSources ) {
        if ( source == null )
          continue;

        if ( source.transform.root == transform.root )
          sameRootSources.Add( source );
        else
          otherSources.Add( source );
      }

      sameRootSources.Sort( CompareSources );
      otherSources.Sort( CompareSources );

      if ( sameRootSources.Count == 0 )
        return otherSources.ToArray();

      if ( otherSources.Count == 0 )
        return sameRootSources.ToArray();

      // Keep local rig sources first, but do not discard valid sources on other roots.
      sameRootSources.AddRange( otherSources );
      return sameRootSources.ToArray();
    }

    private static int CompareSources( OperatorCommandSourceBehaviour left, OperatorCommandSourceBehaviour right )
    {
      if ( left == right )
        return 0;

      if ( left == null )
        return 1;

      if ( right == null )
        return -1;

      var nameComparison = string.Compare( left.SourceName, right.SourceName, StringComparison.Ordinal );
      if ( nameComparison != 0 )
        return nameComparison;

      return string.Compare( left.gameObject.name, right.gameObject.name, StringComparison.Ordinal );
    }

    private OperatorCommandSourceBehaviour ChooseDefaultSource( OperatorCommandSourceBehaviour[] sources )
    {
      if ( sources == null || sources.Length == 0 )
        return null;

      for ( var sourceIndex = 0; sourceIndex < sources.Length; ++sourceIndex ) {
        var source = sources[ sourceIndex ];
        if ( source is FarmStickOperatorCommandSource && source.enabled )
          return source;
      }

      for ( var sourceIndex = 0; sourceIndex < sources.Length; ++sourceIndex ) {
        var source = sources[ sourceIndex ];
        if ( source != null && source.enabled )
          return source;
      }

      return sources[ 0 ];
    }

    private static bool ShouldPromotePreferredSource( OperatorCommandSourceBehaviour currentSource,
                                                      OperatorCommandSourceBehaviour[] sources )
    {
      if ( currentSource == null || sources == null || sources.Length == 0 )
        return false;

      if ( currentSource is FarmStickOperatorCommandSource )
        return false;

      if ( currentSource is not KeyboardOperatorCommandSource )
        return false;

      for ( var sourceIndex = 0; sourceIndex < sources.Length; ++sourceIndex ) {
        var source = sources[ sourceIndex ];
        if ( source is FarmStickOperatorCommandSource && source.enabled )
          return true;
      }

      return false;
    }

    private void SyncSourceEnabledStates()
    {
      if ( m_availableSources == null || m_availableSources.Length == 0 )
        return;

      for ( var sourceIndex = 0; sourceIndex < m_availableSources.Length; ++sourceIndex ) {
        var source = m_availableSources[ sourceIndex ];
        if ( source == null )
          continue;

        var shouldEnable = source == m_commandSource;
        if ( source.enabled != shouldEnable )
          source.enabled = shouldEnable;
      }
    }

    private int GetSourceIndex( OperatorCommandSourceBehaviour source )
    {
      if ( source == null || m_availableSources == null )
        return -1;

      return Array.IndexOf( m_availableSources, source );
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
    public bool CurrentSourceHasHardwareDiagnostics => m_commandSource is IHardwareCommandDiagnostics;
    public bool CurrentSourceDeviceConnected => ( m_commandSource as IHardwareCommandDiagnostics )?.DeviceConnected ?? false;
    public string CurrentSourceDeviceDisplayName => ( m_commandSource as IHardwareCommandDiagnostics )?.DeviceDisplayName ?? string.Empty;
    public string CurrentSourceProfileName => ( m_commandSource as IHardwareCommandDiagnostics )?.ProfileName ?? string.Empty;
    public string CurrentSourceBindingStatus => ( m_commandSource as IHardwareCommandDiagnostics )?.BindingStatus ?? string.Empty;
    public string CurrentSourceRawInputSummary => ( m_commandSource as IHardwareCommandDiagnostics )?.LastRawInputSummary ?? string.Empty;
    public HardwareInputSnapshot CurrentSourceHardwareInputSnapshot => ( m_commandSource as IHardwareCommandDiagnostics )?.LastRawInputSnapshot ?? HardwareInputSnapshot.Zero;
  }
}
