using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.ROIEnc.Auxiliary;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using AGXUnity_Excavator.Scripts.ROIEnc.Debug;
using AGXUnity_Excavator.Scripts.ROIEnc.Detection;
using AGXUnity_Excavator.Scripts.ROIEnc.Fusion;
using AGXUnity_Excavator.Scripts.ROIEnc.Training;
using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc
{
  public class RoiDetectionPipeline : MonoBehaviour
  {
    public enum PipelineMode
    {
      Disabled = 0,
      TrainingExport = 1,
      ExternalOverlayRuntime = 2,
      ManualExport = 3,
      NativeDetection = 4
    }

    [SerializeField]
    private PipelineMode m_mode = PipelineMode.ExternalOverlayRuntime;

    [SerializeField]
    private RoiEncConfiguration m_configuration = new RoiEncConfiguration();

    [SerializeField]
    private RoiDetectionExperimentGroup m_experimentGroup = RoiDetectionExperimentGroup.VisualOnly;

    [SerializeField]
    private TrackedCameraWindow m_fpvCamera = null;

    [SerializeField]
    private AgxSimStepAckServer m_stepAckServer = null;

    [SerializeField]
    private EpisodeManager m_episodeManager = null;

    [SerializeField]
    private ExcavatorMachineController m_machineController = null;

    [SerializeField]
    private SwitchableTargetMassSensor m_targetMassSensor = null;

    [SerializeField]
    private DigAreaMeasurement m_digAreaMeasurement = null;

    [SerializeField]
    private ActObservationCollector m_observationCollector = null;

    [SerializeField]
    private RoiOverlayVisualizer m_overlayVisualizer = null;

    [SerializeField]
    private string m_lastStatus = "idle";

    private SceneGraphLabelGenerator m_sceneGraphLabelGenerator = null;
    private DatasetWriter m_datasetWriter = null;
    private SemanticLabelExporter m_labelExporter = null;
    private readonly List<RoiDescriptor> m_overlayDetections = new List<RoiDescriptor>();

    private OnnxRoiDetector m_nativeDetector = null;
    private RoiTemporalSmoother m_temporalSmoother = null;
    private RoiFusionPipeline m_fusionPipeline = null;
    private MotionIntensityEstimator m_motionEstimator = null;
    private readonly List<RoiDescriptor> m_rawDetections = new List<RoiDescriptor>();
    private readonly List<RoiDescriptor> m_smoothedDetections = new List<RoiDescriptor>();
    private readonly List<RoiDescriptor> m_fusedDetections = new List<RoiDescriptor>();
    private long m_nativeDetectionFrameSkipCounter = 0;
    private float m_lastNativeInferenceMs = 0.0f;

    private long m_frameCounter = 0;
    private long m_lastExportedStepId = long.MinValue;
    private long m_lastObservedStepId = long.MinValue;
    private long m_trainingEpisodeBaseStepId = long.MinValue;
    private long m_manualFixedStepId = -1;
    private long m_pendingManualAutoCaptureStepId = long.MinValue;
    private int m_lastManualEpisodeIndex = 0;
    private bool m_lastManualEpisodeRunning = false;
    private bool m_manualRecordingActive = false;
    private bool m_resetLatch = false;
    private float m_lastMotionIntensity = 0.0f;
    private bool m_externalRuntimeLaunchAttempted = false;
    private System.Diagnostics.Process m_externalRuntimeProcess = null;

    public PipelineMode Mode => m_mode;
    public RoiEncConfiguration Configuration => m_configuration;
    public TrackedCameraWindow FpvCamera => m_fpvCamera;
    public bool IsManualRecordingActive => m_mode == PipelineMode.ManualExport && m_manualRecordingActive;
    public int CurrentDatasetEpisodeIndex => m_labelExporter != null ? m_labelExporter.CurrentEpisodeIndex : 0;
    public int ManualRecordingStepInterval =>
      Mathf.Max( 1, m_configuration != null && m_configuration.Dataset != null ? m_configuration.Dataset.AutomaticCaptureEverySteps : 1 );
    public KeyCode ManualRecordingToggleKey =>
      m_configuration != null && m_configuration.Dataset != null ? m_configuration.Dataset.ManualCaptureKey : KeyCode.F10;
    public KeyCode ManualRecordingSealKey =>
      m_configuration != null && m_configuration.Dataset != null ? m_configuration.Dataset.ManualAdvanceEpisodeKey : KeyCode.F11;
    public long ManualRecordingStepOrdinal => m_manualFixedStepId >= 0 ? m_manualFixedStepId + 1L : 0L;

    private void OnEnable()
    {
      ResolveReferences();
      EnsureHelpers();
      m_lastExportedStepId = long.MinValue;
      m_lastObservedStepId = long.MinValue;
      m_trainingEpisodeBaseStepId = long.MinValue;
      m_lastManualEpisodeIndex = m_episodeManager != null ? m_episodeManager.CurrentEpisodeIndex : 0;
      m_lastManualEpisodeRunning = m_episodeManager == null || m_episodeManager.IsEpisodeRunning;
      m_manualRecordingActive = false;
      ResetManualSamplingState();
      UpdateOverlayStatus();
    }

    private void OnDisable()
    {
      TryWriteCurrentEpisodeManifest( "disable" );
      ShutdownExternalRuntimeProcess();
      DisposeHelpers();
    }

    private void LateUpdate()
    {
      ResolveReferences();
      EnsureHelpers();

      switch ( m_mode ) {
        case PipelineMode.TrainingExport:
          RunTrainingExportTick();
          break;
        case PipelineMode.ExternalOverlayRuntime:
          RunExternalOverlayRuntimeTick();
          break;
        case PipelineMode.ManualExport:
          RunManualExportTick();
          break;
        case PipelineMode.NativeDetection:
          RunNativeDetectionTick();
          break;
        case PipelineMode.Disabled:
        default:
          m_lastStatus = "disabled";
          UpdateOverlayStatus();
          break;
      }
    }

    private void FixedUpdate()
    {
      ResolveReferences();
      EnsureHelpers();

      if ( m_mode != PipelineMode.ManualExport )
        return;

      SyncManualEpisodeState();
      UpdateManualAutomaticSampling();
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_fpvCamera = ExcavatorRigLocator.ResolveComponent( this, m_fpvCamera );
      m_stepAckServer = ExcavatorRigLocator.ResolveComponent( this, m_stepAckServer );
      m_episodeManager = ExcavatorRigLocator.ResolveComponent( this, m_episodeManager );
      m_targetMassSensor = ExcavatorRigLocator.ResolveComponent( this, m_targetMassSensor );
      m_digAreaMeasurement = ExcavatorRigLocator.ResolveComponent( this, m_digAreaMeasurement );
      m_observationCollector = ExcavatorRigLocator.ResolveComponent( this, m_observationCollector );

      if ( m_overlayVisualizer == null )
        m_overlayVisualizer = GetComponent<RoiOverlayVisualizer>();

      if ( m_overlayVisualizer == null && m_mode != PipelineMode.Disabled )
        m_overlayVisualizer = gameObject.AddComponent<RoiOverlayVisualizer>();
    }

    private void EnsureHelpers()
    {
      if ( m_sceneGraphLabelGenerator == null )
        m_sceneGraphLabelGenerator = new SceneGraphLabelGenerator( this, m_machineController, m_targetMassSensor, m_digAreaMeasurement );

      if ( m_datasetWriter == null )
        m_datasetWriter = new DatasetWriter();

      if ( m_labelExporter == null )
        m_labelExporter = new SemanticLabelExporter( m_sceneGraphLabelGenerator, m_datasetWriter );

      if ( m_overlayVisualizer != null )
        m_overlayVisualizer.Configure( m_fpvCamera, m_configuration.Overlay );
    }

    private void DisposeHelpers()
    {
      m_datasetWriter?.Dispose();
      m_datasetWriter = null;
      m_labelExporter = null;
      m_sceneGraphLabelGenerator = null;

      m_nativeDetector?.Dispose();
      m_nativeDetector = null;
      m_temporalSmoother = null;
      m_fusionPipeline = null;
      m_motionEstimator = null;
    }

    private void RunTrainingExportTick()
    {
      if ( m_stepAckServer == null || m_fpvCamera == null || m_labelExporter == null ) {
        m_lastStatus = "training_export_waiting_for_dependencies";
        UpdateOverlayStatus();
        return;
      }

      HandleEpisodeTransitions();
      if ( m_stepAckServer.LastRequestTypeName != "StepReq" || m_stepAckServer.LastRequestStepId < 0 ) {
        m_lastStatus = "training_export_waiting_for_step_ack";
        UpdateOverlayStatus();
        return;
      }

      var currentStepId = m_stepAckServer.LastRequestStepId;
      if ( currentStepId == m_lastExportedStepId ) {
        m_lastStatus = "training_export_no_new_step";
        UpdateOverlayStatus();
        return;
      }

      var datasetOptions = m_configuration.Dataset ?? new RoiEncConfiguration.DatasetOptions();
      if ( !ShouldCaptureTrainingStep( currentStepId, datasetOptions ) ) {
        var stepInterval = Mathf.Max( 1, datasetOptions.AutomaticCaptureEverySteps );
        m_lastStatus = datasetOptions.EnableAutomaticStepSampling ?
                       $"training_export_waiting_for_step_interval:every={stepInterval},step={currentStepId}" :
                       $"training_export_waiting_for_new_step:step={currentStepId}";
        UpdateOverlayStatus( stepId: currentStepId );
        return;
      }

      if ( !TryBuildTrainingFrameSample( currentStepId, out var frameSample, out var error ) ) {
        m_lastStatus = $"training_export_capture_failed:{error}";
        UpdateOverlayStatus();
        return;
      }

      if ( !m_labelExporter.TryExport( frameSample, m_configuration.Dataset, out var imagePath, out _, out error ) ) {
        m_lastStatus = $"training_export_write_failed:{error}";
        UpdateOverlayStatus();
        return;
      }

      m_lastExportedStepId = currentStepId;
      m_lastStatus = $"training_export_ok:{imagePath}";
      UpdateOverlayStatus( frameSample.frameId, frameSample.stepId );
    }

    private void RunManualExportTick()
    {
      if ( m_fpvCamera == null || m_labelExporter == null ) {
        m_lastStatus = "manual_export_waiting_for_dependencies";
        UpdateOverlayStatus( stepId: CurrentManualStepId );
        return;
      }

      SyncManualEpisodeState();
      var options = m_configuration.Dataset ?? new RoiEncConfiguration.DatasetOptions();
      if ( options.EnableManualCaptureHotkeys && Input.GetKeyDown( options.ManualCaptureKey ) ) {
        if ( m_manualRecordingActive ) {
          StopManualRecording( "toggle_hotkey" );
        }
        else {
          BeginManualRecording( "toggle_hotkey" );
        }

        UpdateOverlayStatus( stepId: CurrentManualStepId );
        return;
      }

      if ( Input.GetKeyDown( options.ManualAdvanceEpisodeKey ) ) {
        SealManualRecordingEpisode( "advance_hotkey" );
        UpdateOverlayStatus( stepId: CurrentManualStepId );
        return;
      }

      if ( m_manualRecordingActive && TryConsumePendingManualAutoCaptureStep( out var automaticStepId ) ) {
        if ( !TryExportManualSample( automaticStepId, "auto_step", out var imagePath, out _, out var error ) ) {
          m_lastStatus = $"manual_export_write_failed:{error}";
          UpdateOverlayStatus( stepId: automaticStepId );
          return;
        }

        m_lastStatus = $"manual_export_ok:auto_step:{imagePath}";
        UpdateOverlayStatus( stepId: automaticStepId );
        return;
      }

      var stepInterval = Mathf.Max( 1, options.AutomaticCaptureEverySteps );
      m_lastStatus = m_manualRecordingActive ?
                     $"manual_export_recording:auto_every={stepInterval}step,toggle={options.ManualCaptureKey},seal={options.ManualAdvanceEpisodeKey},dataset_ep={m_labelExporter.CurrentEpisodeIndex},step={ManualRecordingStepOrdinal}" :
                     $"manual_export_idle:toggle={options.ManualCaptureKey},seal={options.ManualAdvanceEpisodeKey},dataset_ep={m_labelExporter.CurrentEpisodeIndex},task_ep={( m_episodeManager != null ? m_episodeManager.CurrentEpisodeIndex : 0 )}";
      UpdateOverlayStatus( stepId: CurrentManualStepId );
    }

    [ContextMenu( "Capture Manual Export Sample" )]
    public void CaptureManualExportSample()
    {
      ResolveReferences();
      EnsureHelpers();

      if ( !TryExportManualSample( CurrentManualStepId, "context_menu", out var imagePath, out _, out var error ) ) {
        m_lastStatus = $"manual_export_write_failed:{error}";
        UpdateOverlayStatus( stepId: CurrentManualStepId );
        return;
      }

      m_lastStatus = $"manual_export_ok:{imagePath}";
      UpdateOverlayStatus( stepId: CurrentManualStepId );
    }

    [ContextMenu( "Advance Manual Export Episode" )]
    public void AdvanceManualExportEpisode()
    {
      ResolveReferences();
      EnsureHelpers();
      SealManualRecordingEpisode( "context_menu" );
    }

    private void RunExternalOverlayRuntimeTick()
    {
      if ( m_stepAckServer == null || m_fpvCamera == null ) {
        m_lastStatus = "external_runtime_waiting_for_camera_or_step_ack";
        UpdateOverlayStatus();
        return;
      }

      if ( m_configuration.ExternalRuntime.AutoLaunchOnPlay )
        EnsureExternalRuntimeProcess();

      if ( m_externalRuntimeProcess != null ) {
        if ( m_externalRuntimeProcess.HasExited ) {
          var exitCode = m_externalRuntimeProcess.ExitCode;
          m_externalRuntimeProcess.Dispose();
          m_externalRuntimeProcess = null;
          m_lastStatus = $"external_runtime_exited:{exitCode}";
          if ( m_configuration.ExternalRuntime.RestartIfExited )
            m_externalRuntimeLaunchAttempted = false;
        }
        else {
          var runtimeName = Path.GetFileName( m_configuration.ExternalRuntime.EntryScriptPath );
          m_lastStatus = $"external_runtime_running:{runtimeName}";
        }
      }
      else if ( m_externalRuntimeLaunchAttempted ) {
        m_lastStatus = "external_runtime_not_running";
      }
      else {
        m_lastStatus = "external_runtime_ready";
      }

      m_lastMotionIntensity = 0.0f;
      UpdateOverlayStatus( stepId: m_stepAckServer.LastRequestStepId );
    }

    private void RunNativeDetectionTick()
    {
      if ( m_fpvCamera == null ) {
        m_lastStatus = "native_detection_waiting_for_camera";
        UpdateOverlayStatus();
        return;
      }

      EnsureNativeDetector();
      if ( m_nativeDetector == null ) {
        UpdateOverlayStatus();
        return;
      }

      var detectionOptions = m_configuration.Detection ?? new RoiEncConfiguration.DetectionOptions();
      var intervalFrames = Mathf.Max( 1, detectionOptions.DetectionIntervalFrames );
      var preserveFailureStatus = false;

      m_nativeDetectionFrameSkipCounter += 1;
      var shouldRunDetection = m_nativeDetectionFrameSkipCounter >= intervalFrames;

      var frameId = NextFrameId();
      var stepId = m_stepAckServer != null ? m_stepAckServer.LastRequestStepId : -1L;

      if ( shouldRunDetection ) {
        m_nativeDetectionFrameSkipCounter = 0;

        if ( !m_nativeDetector.IsReady )
          m_lastStatus = "native_detection_initializing";

        if ( !m_fpvCamera.TryGetLiveRenderTexture( out var sourceCamera, out var renderTexture, out _ ) ||
             renderTexture == null ) {
          m_lastStatus = "native_detection_render_texture_unavailable";
          UpdateOverlayStatus( frameId, stepId );
          return;
        }

        var frameSample = new RoiFrameSample
        {
          frameId = frameId,
          stepId = stepId,
          captureTimeNs = NowUnixTimeNs(),
          width = renderTexture.width,
          height = renderTexture.height,
          sourceTexture = renderTexture,
          sourceCamera = sourceCamera != null ? sourceCamera : m_fpvCamera.RuntimeCamera,
          sourceWindow = m_fpvCamera
        };

        m_rawDetections.Clear();
        if ( !m_nativeDetector.TryGetRois( frameSample, m_rawDetections, out var detectError ) ) {
          m_lastStatus = $"native_detection_failed:{detectError}";
          UpdateOverlayStatus( frameId, stepId );
          return;
        }

        m_lastNativeInferenceMs = m_nativeDetector.IsReady
                                    ? GetNativeBackendInferenceMs()
                                    : 0.0f;
      }

      if ( m_temporalSmoother == null )
        m_temporalSmoother = new RoiTemporalSmoother();

      m_temporalSmoother.Smooth( shouldRunDetection ? m_rawDetections : null,
                                 m_configuration.Smoothing,
                                 m_smoothedDetections );

      if ( m_fusionPipeline == null )
        m_fusionPipeline = new RoiFusionPipeline();

      var sourceCamera2 = m_fpvCamera.RuntimeCamera;
      m_fusionPipeline.Fuse( sourceCamera2, m_machineController, frameId,
                             m_smoothedDetections, m_configuration.Fusion,
                             m_fusedDetections );

      if ( m_motionEstimator == null )
        m_motionEstimator = new MotionIntensityEstimator();

      var bucketTransform = m_machineController != null ? m_machineController.BucketReference : null;
      m_lastMotionIntensity = m_motionEstimator.Update( bucketTransform, Time.time );

      m_overlayDetections.Clear();
      m_overlayDetections.AddRange( m_fusedDetections );

      preserveFailureStatus = !shouldRunDetection &&
                              !string.IsNullOrEmpty( m_lastStatus ) &&
                              m_lastStatus.StartsWith( "native_detection_failed:", StringComparison.Ordinal );

      if ( !preserveFailureStatus ) {
        m_lastStatus = $"native_detection_ok:rois={m_overlayDetections.Count},infer_ms={m_lastNativeInferenceMs:F2}," +
                       $"motion={m_lastMotionIntensity:F3},interval={intervalFrames}";
      }

      UpdateOverlayStatus( frameId, stepId );
    }

    private void EnsureNativeDetector()
    {
      if ( m_nativeDetector != null )
        return;

      m_nativeDetector = new OnnxRoiDetector( null, m_configuration );

      if ( !m_nativeDetector.IsReady ) {
        m_lastStatus = $"native_detection_backend_init:{( m_nativeDetector.IsReady ? "ok" : "pending" )}";
      }
    }

    private float GetNativeBackendInferenceMs()
    {
      try {
        var field = typeof( OnnxRoiDetector ).GetField( "m_backend",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
        if ( field == null )
          return 0.0f;

        var backend = field.GetValue( m_nativeDetector );
        if ( backend is Detection.Backend.NativeTensorRtDetectorBackend nativeBackend )
          return nativeBackend.LastInferenceMs;
      }
      catch ( Exception ) {
      }

      return 0.0f;
    }

    private bool TryExportManualSample( long stepId, string trigger, out string imagePath, out string labelPath, out string error )
    {
      imagePath = string.Empty;
      labelPath = string.Empty;
      error = string.Empty;

      if ( m_fpvCamera == null || m_labelExporter == null ) {
        error = "manual_export_dependencies_missing";
        return false;
      }

      var resolvedStepId = System.Math.Max( 0L, stepId );
      if ( !TryBuildTrainingFrameSample( resolvedStepId, out var frameSample, out error ) )
        return false;

      if ( !m_labelExporter.TryExport( frameSample, m_configuration.Dataset, out imagePath, out labelPath, out error ) )
        return false;

      m_lastExportedStepId = resolvedStepId;
      if ( !string.IsNullOrWhiteSpace( trigger ) )
        m_lastStatus = $"manual_export_ok:{trigger}:{imagePath}";
      return true;
    }

    private void SealManualRecordingEpisode( string trigger )
    {
      if ( !m_manualRecordingActive ) {
        m_lastStatus = $"manual_export_idle:no_active_recording:{trigger}";
        UpdateOverlayStatus( stepId: CurrentManualStepId );
        return;
      }

      StopManualRecording( $"seal:{trigger}" );
      m_lastStatus = $"manual_export_episode_sealed:{trigger}:ep={m_labelExporter.CurrentEpisodeIndex}";
      UpdateOverlayStatus( stepId: CurrentManualStepId );
    }

    private void HandleEpisodeTransitions()
    {
      if ( m_stepAckServer == null || m_labelExporter == null )
        return;

      var requestType = m_stepAckServer.LastRequestTypeName;
      var stepId = m_stepAckServer.LastRequestStepId;
      if ( requestType == "ResetReq" ) {
        if ( !m_resetLatch ) {
          TryWriteCurrentEpisodeManifest( "step_ack_reset" );
          m_labelExporter.AdvanceEpisode();
          m_lastExportedStepId = long.MinValue;
          m_lastObservedStepId = long.MinValue;
          m_trainingEpisodeBaseStepId = long.MinValue;
          m_resetLatch = true;
        }

        return;
      }

      m_resetLatch = false;
      if ( requestType == "StepReq" && stepId >= 0 ) {
        if ( m_lastObservedStepId != long.MinValue && stepId < m_lastObservedStepId ) {
          TryWriteCurrentEpisodeManifest( "step_ack_rewind" );
          m_labelExporter.AdvanceEpisode();
          m_lastExportedStepId = long.MinValue;
          m_trainingEpisodeBaseStepId = long.MinValue;
        }

        m_lastObservedStepId = stepId;
      }
    }

    private bool TryBuildTrainingFrameSample( long stepId, out RoiFrameSample frameSample, out string error )
    {
      frameSample = null;
      error = string.Empty;

      if ( m_fpvCamera == null ) {
        error = "training_camera_missing";
        return false;
      }

      m_fpvCamera.TryGetLiveRenderTexture( out var sourceCamera, out _, out _ );
      if ( !m_fpvCamera.TryCaptureRgb24( out var rgb24, out var width, out var height ) ) {
        error = "training_rgb24_capture_failed";
        return false;
      }

      frameSample = new RoiFrameSample
      {
        frameId = NextFrameId(),
        stepId = stepId,
        captureTimeNs = NowUnixTimeNs(),
        width = width,
        height = height,
        rgb24 = rgb24,
        sourceCamera = sourceCamera != null ? sourceCamera : m_fpvCamera.RuntimeCamera,
        sourceWindow = m_fpvCamera
      };
      return true;
    }

    private void UpdateOverlayStatus( long frameId = -1, long stepId = -1 )
    {
      if ( m_overlayVisualizer == null )
        return;

      m_overlayVisualizer.Configure( m_fpvCamera, m_configuration.Overlay );
      m_overlayVisualizer.UpdateOverlay( frameId >= 0 ? frameId : m_frameCounter,
                                         stepId >= 0 ? stepId : ( m_stepAckServer != null ? m_stepAckServer.LastRequestStepId : -1 ),
                                         m_overlayDetections,
                                         m_lastMotionIntensity,
                                         m_lastStatus );
    }

    private void SyncManualEpisodeState()
    {
      if ( m_mode != PipelineMode.ManualExport )
        return;

      var currentEpisodeIndex = m_episodeManager != null ? m_episodeManager.CurrentEpisodeIndex : 0;
      var isEpisodeRunning = m_episodeManager == null || m_episodeManager.IsEpisodeRunning;

      if ( m_episodeManager != null && currentEpisodeIndex > m_lastManualEpisodeIndex ) {
        if ( m_manualRecordingActive )
          StopManualRecording( "task_episode_advanced" );
        ResetManualSamplingState();
      }
      else if ( !isEpisodeRunning && m_lastManualEpisodeRunning ) {
        if ( m_manualRecordingActive )
          StopManualRecording( "task_episode_stopped" );
        ResetManualSamplingState();
      }

      m_lastManualEpisodeIndex = currentEpisodeIndex;
      m_lastManualEpisodeRunning = isEpisodeRunning;
    }

    private void UpdateManualAutomaticSampling()
    {
      var options = m_configuration.Dataset ?? new RoiEncConfiguration.DatasetOptions();
      if ( !options.EnableAutomaticStepSampling )
        return;

      if ( !m_manualRecordingActive )
        return;

      if ( m_episodeManager != null && !m_episodeManager.IsEpisodeRunning )
        return;

      m_manualFixedStepId += 1;
      var stepInterval = Mathf.Max( 1, options.AutomaticCaptureEverySteps );
      if ( ( m_manualFixedStepId + 1 ) % stepInterval == 0 )
        m_pendingManualAutoCaptureStepId = m_manualFixedStepId;
    }

    private bool ShouldCaptureTrainingStep( long stepId, RoiEncConfiguration.DatasetOptions options )
    {
      if ( stepId < 0 )
        return false;

      options = options ?? new RoiEncConfiguration.DatasetOptions();
      if ( !options.EnableAutomaticStepSampling )
        return true;

      if ( m_trainingEpisodeBaseStepId == long.MinValue )
        m_trainingEpisodeBaseStepId = stepId;

      var stepOrdinal = stepId - m_trainingEpisodeBaseStepId + 1L;
      var stepInterval = Mathf.Max( 1, options.AutomaticCaptureEverySteps );
      return stepOrdinal > 0 && stepOrdinal % stepInterval == 0L;
    }

    private bool TryConsumePendingManualAutoCaptureStep( out long stepId )
    {
      stepId = m_pendingManualAutoCaptureStepId;
      if ( stepId == long.MinValue )
        return false;

      m_pendingManualAutoCaptureStepId = long.MinValue;
      return true;
    }

    private void ResetManualSamplingState()
    {
      m_manualFixedStepId = -1;
      m_pendingManualAutoCaptureStepId = long.MinValue;
    }

    private void EnsureExternalRuntimeProcess()
    {
      if ( m_externalRuntimeLaunchAttempted &&
           ( m_configuration == null ||
             m_configuration.ExternalRuntime == null ||
             !m_configuration.ExternalRuntime.RestartIfExited ) &&
           m_externalRuntimeProcess == null ) {
        return;
      }

      if ( m_externalRuntimeProcess != null ) {
        if ( !m_externalRuntimeProcess.HasExited )
          return;

        m_externalRuntimeProcess.Dispose();
        m_externalRuntimeProcess = null;
      }

      if ( !RoiExternalRuntimeLauncher.TryLaunch( m_configuration.ExternalRuntime, selfTest: false, out m_externalRuntimeProcess, out var error ) ) {
        m_externalRuntimeLaunchAttempted = true;
        m_lastStatus = $"external_runtime_launch_failed:{error}";
        return;
      }

      m_externalRuntimeLaunchAttempted = true;
      m_lastStatus = $"external_runtime_launch_ok:{m_externalRuntimeProcess.Id}";
    }

    private void ShutdownExternalRuntimeProcess()
    {
      if ( m_externalRuntimeProcess == null )
        return;

      if ( m_configuration == null ||
           m_configuration.ExternalRuntime == null ||
           !m_configuration.ExternalRuntime.CloseProcessOnDisable ) {
        m_externalRuntimeProcess.Dispose();
        m_externalRuntimeProcess = null;
        return;
      }

      try {
        if ( !m_externalRuntimeProcess.HasExited ) {
          m_externalRuntimeProcess.Kill();
          m_externalRuntimeProcess.WaitForExit( 1000 );
        }
      }
      catch ( Exception ) {
      }
      finally {
        m_externalRuntimeProcess.Dispose();
        m_externalRuntimeProcess = null;
      }
    }

    private long NextFrameId()
    {
      m_frameCounter += 1;
      return m_frameCounter;
    }

    private long CurrentManualStepId => System.Math.Max( 0L, m_manualFixedStepId );

    private static long NowUnixTimeNs()
    {
      return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L;
    }

    private void BeginManualRecording( string trigger )
    {
      if ( m_labelExporter == null ) {
        m_lastStatus = $"manual_export_recording_start_failed:{trigger}:exporter_missing";
        return;
      }

      m_labelExporter.AdvanceEpisode();
      m_manualRecordingActive = true;
      ResetManualSamplingState();
      m_lastExportedStepId = long.MinValue;
      m_lastStatus = $"manual_export_recording_started:{trigger}:ep={m_labelExporter.CurrentEpisodeIndex}";
    }

    private void StopManualRecording( string reason )
    {
      TryWriteCurrentEpisodeManifest( reason );
      m_manualRecordingActive = false;
      ResetManualSamplingState();
      m_lastExportedStepId = long.MinValue;
      m_lastStatus = $"manual_export_recording_stopped:{reason}:ep={m_labelExporter.CurrentEpisodeIndex}";
    }

    private void TryWriteCurrentEpisodeManifest( string reason )
    {
      if ( m_labelExporter == null || m_configuration == null || m_configuration.Dataset == null )
        return;

      if ( !m_labelExporter.TryWriteEpisodeManifest( m_configuration.Dataset,
                                                     m_mode.ToString(),
                                                     m_configuration.Detection != null ? m_configuration.Detection.ClassLabels : null,
                                                     out var manifestPath,
                                                     out var error ) ) {
        if ( !string.IsNullOrWhiteSpace( error ) )
          m_lastStatus = $"dataset_manifest_failed:{reason}:{error}";
        return;
      }

      if ( !string.IsNullOrWhiteSpace( manifestPath ) )
        m_lastStatus = $"dataset_manifest_ok:{reason}:{manifestPath}";
    }
  }
}
