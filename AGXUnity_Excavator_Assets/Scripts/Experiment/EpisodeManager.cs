using System;
using System.Collections.Generic;
using System.Globalization;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Simulation;
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEngine;
using UnityEngine.Serialization;

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
    private bool m_cutManualInputUntilNeutralAfterTransition = true;

    [SerializeField]
    [Range( 0.0f, 0.25f )]
    private float m_inputNeutralThreshold = 0.05f;

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_inputNeutralHoldDurationSec = 0.2f;

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

    [FormerlySerializedAs( "m_massVolumeCounter" )]
    [SerializeField]
    private global::ExcavationMassTracker m_massTracker = null;

    [FormerlySerializedAs( "m_targetBoxMassSensor" )]
    [SerializeField]
    private global::SwitchableTargetMassSensor m_targetMassSensor = null;

    [SerializeField]
    private global::ActiveTargetCollisionMonitor m_activeTargetCollisionMonitor = null;

    [SerializeField]
    private global::DigAreaMeasurement m_digAreaMeasurement = null;

    [SerializeField]
    [Min( 0.0f )]
    private float m_goodDigAreaTouchToleranceM = 0.05f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_goodDigBelowPlaneDepthToleranceM = 0.02f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_goodDigLoadDeltaToleranceKg = 5.0f;

    [SerializeField]
    private ExcavatorCommandInterpreter m_interpreter = new ExcavatorCommandInterpreter();

    private OperatorCommandSourceBehaviour[] m_availableSources = Array.Empty<OperatorCommandSourceBehaviour>();
    private int m_nextEpisodeIndex = 1;
    [FormerlySerializedAs( "m_massVolumeCounterResolved" )]
    private bool m_massTrackerResolved = false;
    private bool m_targetMassSensorResolved = false;
    private bool m_inputCutActive = false;
    private float m_inputNeutralSinceTime = -1.0f;
    private float m_minDistanceToDigArea = -1.0f;
    private float m_bucketDepthBelowDigAreaPlane = 0.0f;
    private float m_previousMassInBucketSample = 0.0f;
    private float m_previousExcavatedMassSample = 0.0f;
    private bool m_goodDigStartLatched = false;
    private bool m_goodDigStartThisFrame = false;

    public int CurrentEpisodeIndex { get; private set; }
    public bool IsEpisodeRunning { get; private set; }
    public OperatorCommand LastRawCommand { get; private set; }
    public OperatorCommand LastSimulatedCommand { get; private set; }
    public ExcavatorActuationCommand LastActuationCommand { get; private set; }

    public string CurrentSourceName => m_commandSource != null ? m_commandSource.SourceName : "None";
    public string CurrentControlLayout => m_interpreter != null ? m_interpreter.LayoutDescription : string.Empty;
    public string CurrentTargetName => m_targetMassSensor != null ? m_targetMassSensor.CurrentTargetName : "None";
    public string LastSavedPath => m_logger != null ? m_logger.LastSavedPath : string.Empty;
    public float MassInBucket => m_massTracker != null ? m_massTracker.MassInBucket : 0.0f;
    public float ExcavatedMass => m_massTracker != null ? m_massTracker.ExcavatedMass : 0.0f;
    public float MassInTargetBox => m_targetMassSensor != null ? m_targetMassSensor.MassInBox : 0.0f;
    public float DepositedMassInTargetBox => m_targetMassSensor != null ? m_targetMassSensor.DepositedMass : 0.0f;
    public float MinDistanceToTarget =>
      m_targetMassSensor != null &&
      m_targetMassSensor.TryMeasureBucketDistance( m_machineController != null ? m_machineController.BucketReference : null,
                                                   out var minDistanceMeters ) ?
        minDistanceMeters :
        -1.0f;
    public float MinDistanceToDigArea => m_minDistanceToDigArea;
    public float BucketDepthBelowDigAreaPlane => m_bucketDepthBelowDigAreaPlane;
    public bool IsBucketTouchingDigArea =>
      m_minDistanceToDigArea >= 0.0f &&
      m_minDistanceToDigArea <= m_goodDigAreaTouchToleranceM;
    public bool IsBucketBelowDigAreaPlane =>
      m_bucketDepthBelowDigAreaPlane >= m_goodDigBelowPlaneDepthToleranceM;
    public bool GoodDigStartLatched => m_goodDigStartLatched;
    public bool GoodDigStartThisFrame => m_goodDigStartThisFrame;
    public int TargetHardCollisionCount => m_activeTargetCollisionMonitor != null ? m_activeTargetCollisionMonitor.TargetHardCollisionCount : 0;
    public float TargetContactMaxNormalForceN => m_activeTargetCollisionMonitor != null ? m_activeTargetCollisionMonitor.TargetContactMaxNormalForceN : 0.0f;
    public int AvailableSourceCount => m_availableSources != null ? m_availableSources.Length : 0;
    public int CurrentSourceIndex => GetSourceIndex( m_commandSource );
    public int AvailableTargetCount => m_targetMassSensor != null ? m_targetMassSensor.AvailableTargetCount : 0;
    public int CurrentTargetIndex => m_targetMassSensor != null ? m_targetMassSensor.CurrentTargetIndex : -1;
    public bool IsTransitionInputCutActive => m_inputCutActive;
    public string TransitionInputCutHint => CurrentSourceHasHardwareDiagnostics ?
                                            "Release joystick and buttons to continue." :
                                            "Release all control inputs to continue.";

    private void Awake()
    {
      ResolveReferences();
      DisableLegacyExcavatorInputControllers();
      ResetGoodDigStartTracking();
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

      var rawCommand = m_commandSource != null ? m_commandSource.ReadCommand() : OperatorCommand.Zero;
      LastRawCommand = ApplyTransitionInputCut( rawCommand );

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

      RefreshGoodDigStartState();

      if ( IsEpisodeRunning ) {
        var actDiagnostics = m_commandSource as IActCommandDiagnostics;
        if ( m_logger != null ) {
          var hardwareDiagnostics = m_commandSource as IHardwareCommandDiagnostics;
          m_logger.RecordFrame(
            Time.time,
            LastRawCommand,
            LastSimulatedCommand,
            LastActuationCommand,
            actDiagnostics,
            hardwareDiagnostics,
            m_machineController != null ? m_machineController.BucketReference : null,
            m_massTracker,
            m_targetMassSensor,
            m_activeTargetCollisionMonitor );
        }
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

    public string GetAvailableTargetDisplayName( int index )
    {
      ResolveReferences();
      return m_targetMassSensor != null ? m_targetMassSensor.GetAvailableTargetDisplayName( index ) : string.Empty;
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

    public bool SetTargetByIndex( int index )
    {
      ResolveReferences();
      return m_targetMassSensor != null && m_targetMassSensor.SetActiveTargetByIndex( index );
    }

    public bool CycleTarget( int direction )
    {
      ResolveReferences();
      return m_targetMassSensor != null && m_targetMassSensor.CycleTarget( direction );
    }

    [ContextMenu( "Log Current Target Distance Diagnostic" )]
    private void LogCurrentTargetDistanceDiagnostic()
    {
      ResolveReferences();
      LogTargetDistanceDiagnosticForCurrentTarget();
    }

    [ContextMenu( "Log All Target Distance Diagnostics" )]
    private void LogAllTargetDistanceDiagnostics()
    {
      ResolveReferences();

      if ( m_targetMassSensor == null || m_targetMassSensor.AvailableTargetCount <= 0 ) {
        Debug.LogWarning( "Target distance diagnostic skipped because no active target sensor is available.", this );
        return;
      }

      var originalTargetIndex = m_targetMassSensor.CurrentTargetIndex;
      for ( var targetIndex = 0; targetIndex < m_targetMassSensor.AvailableTargetCount; ++targetIndex ) {
        m_targetMassSensor.SetActiveTargetByIndex( targetIndex );
        LogTargetDistanceDiagnosticForCurrentTarget();
      }

      if ( originalTargetIndex >= 0 )
        m_targetMassSensor.SetActiveTargetByIndex( originalTargetIndex );
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

      if ( IsEpisodeRunning || ( m_logger != null && m_logger.IsRecording ) )
        StopEpisode( "restart" );

      CurrentEpisodeIndex = m_nextEpisodeIndex++;
      IsEpisodeRunning = true;
      ArmInputCut();
      m_machineController?.StartEngine();
      m_commandSimulator?.ResetState();
      if ( m_commandSource is IEpisodeLifecycleAware lifecycleAware ) {
        lifecycleAware.OnEpisodeStarted( new EpisodeCommandSourceContext
        {
          EpisodeIndex = CurrentEpisodeIndex,
          FixedDeltaTimeSec = Time.fixedDeltaTime
        } );
      }

      m_logger?.BeginEpisode( CurrentEpisodeIndex, CurrentSourceName );
      ResetGoodDigStartTracking();
    }

    public void StopEpisode( string reason )
    {
      var wasEpisodeRunning = IsEpisodeRunning;
      IsEpisodeRunning = false;
      ArmInputCut();
      m_machineController?.StopEngine();
      m_commandSimulator?.ResetState();
      if ( wasEpisodeRunning && m_commandSource is IEpisodeLifecycleAware lifecycleAware )
        lifecycleAware.OnEpisodeStopped( reason );

      LastRawCommand = OperatorCommand.Zero;
      LastSimulatedCommand = OperatorCommand.Zero;
      LastActuationCommand = ExcavatorActuationCommand.Zero;
      ResetGoodDigStartTracking();

      if ( m_logger != null && m_logger.IsRecording )
        m_logger.EndEpisode( reason );
    }

    public void ResetEpisode()
    {
      ResetEpisode( m_autoRestartAfterReset );
    }

    public void ResetEpisode( bool restartEpisode )
    {
      StopEpisode( "reset" );
      m_sceneResetService?.ResetScene();
      m_activeTargetCollisionMonitor?.ResetMonitoring();
      ResetGoodDigStartTracking();

      if ( restartEpisode )
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

      if ( m_massTracker == null && !m_massTrackerResolved )
        m_massTracker = ExcavatorRigLocator.ResolveComponent( this, m_massTracker );

      if ( m_targetMassSensor == null && !m_targetMassSensorResolved )
        m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( this, m_targetMassSensor );

      if ( m_activeTargetCollisionMonitor == null )
        m_activeTargetCollisionMonitor = ExcavatorRigLocator.ResolveComponent( this, m_activeTargetCollisionMonitor );

      if ( m_activeTargetCollisionMonitor == null ) {
        var monitorHost = m_machineController != null ? m_machineController.gameObject : gameObject;
        m_activeTargetCollisionMonitor = monitorHost.GetComponent<global::ActiveTargetCollisionMonitor>();
        if ( m_activeTargetCollisionMonitor == null )
          m_activeTargetCollisionMonitor = monitorHost.AddComponent<global::ActiveTargetCollisionMonitor>();
      }
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = ExcavatorRigLocator.ResolveComponent( this, m_digAreaMeasurement );
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = global::DigAreaMeasurement.FindOrCreateInScene();
      else
        m_digAreaMeasurement.ResolveReferences();

      m_massTrackerResolved = true;
      m_targetMassSensorResolved = true;

      m_targetMassSensor?.RefreshTargets();
    }

    private void RefreshGoodDigStartState()
    {
      RefreshDigAreaMeasurements();
      m_goodDigStartThisFrame = false;

      var currentMassInBucket = MassInBucket;
      var currentExcavatedMass = ExcavatedMass;
      if ( IsEpisodeRunning && !m_goodDigStartLatched ) {
        var rawLoadProgress = ( currentMassInBucket - m_previousMassInBucketSample ) >= m_goodDigLoadDeltaToleranceKg ||
                              ( currentExcavatedMass - m_previousExcavatedMassSample ) >= m_goodDigLoadDeltaToleranceKg;
        if ( rawLoadProgress &&
             IsBucketTouchingDigArea &&
             IsBucketBelowDigAreaPlane ) {
          m_goodDigStartLatched = true;
          m_goodDigStartThisFrame = true;
        }
      }

      m_previousMassInBucketSample = currentMassInBucket;
      m_previousExcavatedMassSample = currentExcavatedMass;
    }

    private void RefreshDigAreaMeasurements()
    {
      var bucketReference = m_machineController != null ? m_machineController.BucketReference : null;
      if ( m_digAreaMeasurement != null &&
           m_digAreaMeasurement.TryMeasureBucketDigAreaMetrics( bucketReference,
                                                                out var minDistanceToDigAreaMeters,
                                                                out var bucketDepthBelowDigAreaPlaneMeters ) ) {
        m_minDistanceToDigArea = minDistanceToDigAreaMeters;
        m_bucketDepthBelowDigAreaPlane = bucketDepthBelowDigAreaPlaneMeters;
        return;
      }

      m_minDistanceToDigArea = -1.0f;
      m_bucketDepthBelowDigAreaPlane = 0.0f;
    }

    private void ResetGoodDigStartTracking()
    {
      RefreshDigAreaMeasurements();
      m_goodDigStartLatched = false;
      m_goodDigStartThisFrame = false;
      m_previousMassInBucketSample = MassInBucket;
      m_previousExcavatedMassSample = ExcavatedMass;
    }

    private void LogTargetDistanceDiagnosticForCurrentTarget()
    {
      var bucketReference = m_machineController != null ? m_machineController.BucketReference : null;
      var currentTarget = m_targetMassSensor != null ? m_targetMassSensor.CurrentTarget : null;
      if ( bucketReference == null || currentTarget == null ) {
        Debug.LogWarning( "Target distance diagnostic skipped because bucket reference or active target is missing.", this );
        return;
      }

      if ( !BucketTargetDistanceMeasurementUtility.TryDiagnoseDistance( bucketReference,
                                                                        currentTarget,
                                                                        out var diagnostic ) ) {
        Debug.LogWarning(
          $"Target distance diagnostic failed for target '{currentTarget.TargetName}'.",
          this );
        return;
      }

      var reportLines = new List<string>
      {
        "Configured min_distance_to_target_m diagnostic",
        $"target={currentTarget.TargetName}",
        $"approximate_min_distance_m={FormatFloat( diagnostic.ApproximateDistanceMeters )}",
        $"bucket_proxy_source={diagnostic.BucketBoxSource}",
        $"target_geometry_source={diagnostic.TargetGeometrySource}",
        DescribeMeasurementBox( "bucket_proxy_volume", diagnostic.BucketBox ),
        DescribeMeasurementBox( "target_distance_geometry", diagnostic.TargetBox ),
        DescribeClosestSample( diagnostic.ClosestSample )
      };

      if ( diagnostic.ClosestSample.IsValid && diagnostic.ClosestSample.DistanceMeters <= 1.0e-4f ) {
        var sourceLabel = diagnostic.ClosestSample.SourceIsBucket ? "bucket" : "target";
        var otherLabel = diagnostic.ClosestSample.SourceIsBucket ? "target" : "bucket";
        reportLines.Add(
          $"zero_distance_interpretation={sourceLabel} sample point at normalized local {FormatVector3( diagnostic.ClosestSample.NormalizedSourcePoint )} is already inside the {otherLabel} distance geometry, so the configured proxy distance drops to 0 before the hard surfaces necessarily meet." );
      }

      Debug.Log( string.Join( "\n", reportLines ), this );
    }

    private static string DescribeMeasurementBox( string label, OrientedMeasurementBox measurementBox )
    {
      var worldCenter = measurementBox.Frame != null ?
                        measurementBox.Frame.TransformPoint( measurementBox.CenterLocal ) :
                        measurementBox.CenterLocal;
      var localSize = 2.0f * measurementBox.HalfExtents;
      if ( !TryGetWorldAabb( measurementBox, out var worldMin, out var worldMax ) ) {
        return $"{label}: frame={DescribeTransform( measurementBox.Frame )}, world_center={FormatVector3( worldCenter )}, local_size={FormatVector3( localSize )}, world_aabb=unavailable";
      }

      return
        $"{label}: frame={DescribeTransform( measurementBox.Frame )}, world_center={FormatVector3( worldCenter )}, local_size={FormatVector3( localSize )}, world_aabb_min={FormatVector3( worldMin )}, world_aabb_max={FormatVector3( worldMax )}, world_aabb_size={FormatVector3( worldMax - worldMin )}";
    }

    private static string DescribeClosestSample( MeasurementBoxClosestSample closestSample )
    {
      if ( !closestSample.IsValid )
        return "closest_sample=unavailable";

      var sourceLabel = closestSample.SourceIsBucket ? "bucket" : "target";
      var otherLabel = closestSample.SourceIsBucket ? "target" : "bucket";
      return
        $"closest_sample: source={sourceLabel}, sample_local_normalized={FormatVector3( closestSample.NormalizedSourcePoint )}, sample_world={FormatVector3( closestSample.SourcePointWorld )}, closest_point_on_{otherLabel}={FormatVector3( closestSample.ClosestPointWorld )}, sample_distance_m={FormatFloat( closestSample.DistanceMeters )}";
    }

    private static bool TryGetWorldAabb( OrientedMeasurementBox measurementBox, out Vector3 worldMin, out Vector3 worldMax )
    {
      worldMin = Vector3.positiveInfinity;
      worldMax = Vector3.negativeInfinity;
      if ( measurementBox.Frame == null || !measurementBox.IsValid )
        return false;

      for ( var xSign = -1; xSign <= 1; xSign += 2 ) {
        for ( var ySign = -1; ySign <= 1; ySign += 2 ) {
          for ( var zSign = -1; zSign <= 1; zSign += 2 ) {
            var worldCorner = measurementBox.CornerWorld( xSign, ySign, zSign );
            worldMin = Vector3.Min( worldMin, worldCorner );
            worldMax = Vector3.Max( worldMax, worldCorner );
          }
        }
      }

      return true;
    }

    private static string DescribeTransform( Transform transformValue )
    {
      if ( transformValue == null )
        return "none";

      var pathSegments = new List<string>();
      var current = transformValue;
      while ( current != null ) {
        pathSegments.Add( current.name );
        current = current.parent;
      }

      pathSegments.Reverse();
      return string.Join( "/", pathSegments );
    }

    private static string FormatVector3( Vector3 value )
    {
      return string.Format(
        CultureInfo.InvariantCulture,
        "({0:0.###}, {1:0.###}, {2:0.###})",
        value.x,
        value.y,
        value.z );
    }

    private static string FormatFloat( float value )
    {
      return value.ToString( "0.####", CultureInfo.InvariantCulture );
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

    private void ArmInputCut()
    {
      m_inputCutActive = m_cutManualInputUntilNeutralAfterTransition && !( m_commandSource is IActCommandDiagnostics );
      m_inputNeutralSinceTime = -1.0f;
    }

    private OperatorCommand ApplyTransitionInputCut( OperatorCommand rawCommand )
    {
      if ( !m_inputCutActive )
        return rawCommand;

      if ( IsNeutralCommand( rawCommand ) ) {
        if ( m_inputNeutralSinceTime < 0.0f )
          m_inputNeutralSinceTime = Time.unscaledTime;

        if ( Time.unscaledTime - m_inputNeutralSinceTime >= m_inputNeutralHoldDurationSec )
          m_inputCutActive = false;
      }
      else {
        m_inputNeutralSinceTime = -1.0f;
      }

      return OperatorCommand.Zero;
    }

    private bool IsNeutralCommand( OperatorCommand command )
    {
      return Mathf.Abs( command.LeftStickX ) <= m_inputNeutralThreshold &&
             Mathf.Abs( command.LeftStickY ) <= m_inputNeutralThreshold &&
             Mathf.Abs( command.RightStickX ) <= m_inputNeutralThreshold &&
             Mathf.Abs( command.RightStickY ) <= m_inputNeutralThreshold &&
             Mathf.Abs( command.Drive ) <= m_inputNeutralThreshold &&
             Mathf.Abs( command.Steer ) <= m_inputNeutralThreshold &&
             !command.ResetRequested &&
             !command.StartEpisodeRequested &&
             !command.StopEpisodeRequested;
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
