using System;
using System.Collections.Generic;
using System.IO;
using AGXUnity_Excavator.Scripts.Control.Core;
using AGXUnity_Excavator.Scripts.Control.Execution;
using AGXUnity_Excavator.Scripts.Control.Sources;
using AGXUnity_Excavator.Scripts.Experiment;
using AGXUnity_Excavator.Scripts.Presentation;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using AGXUnity_Excavator.Scripts.ROIEnc.Debug;
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
      ManualExport = 3
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

    private long m_frameCounter = 0;
    private long m_lastExportedStepId = long.MinValue;
    private long m_lastObservedStepId = long.MinValue;
    private long m_manualCaptureStepId = 0;
    private bool m_resetLatch = false;
    private float m_lastMotionIntensity = 0.0f;
    private float m_lastManualCaptureTime = float.NegativeInfinity;
    private bool m_externalRuntimeLaunchAttempted = false;
    private System.Diagnostics.Process m_externalRuntimeProcess = null;

    public PipelineMode Mode => m_mode;
    public RoiEncConfiguration Configuration => m_configuration;
    public TrackedCameraWindow FpvCamera => m_fpvCamera;

    private void OnEnable()
    {
      ResolveReferences();
      EnsureHelpers();
      UpdateOverlayStatus();
    }

    private void OnDisable()
    {
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
        case PipelineMode.Disabled:
        default:
          m_lastStatus = "disabled";
          UpdateOverlayStatus();
          break;
      }
    }

    private void ResolveReferences()
    {
      m_machineController = ExcavatorRigLocator.ResolveComponent( this, m_machineController );
      m_fpvCamera = ExcavatorRigLocator.ResolveComponent( this, m_fpvCamera );
      m_stepAckServer = ExcavatorRigLocator.ResolveComponent( this, m_stepAckServer );
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
        UpdateOverlayStatus( stepId: m_manualCaptureStepId );
        return;
      }

      var options = m_configuration.Dataset ?? new RoiEncConfiguration.DatasetOptions();
      if ( Input.GetKeyDown( options.ManualAdvanceEpisodeKey ) )
        AdvanceManualExportEpisodeInternal( "hotkey" );

      var captureRequested =
        options.EnableManualCaptureHotkeys &&
        ( options.CaptureWhileManualKeyHeld ?
            Input.GetKey( options.ManualCaptureKey ) :
            Input.GetKeyDown( options.ManualCaptureKey ) );

      if ( captureRequested ) {
        if ( Time.unscaledTime < m_lastManualCaptureTime + Mathf.Max( 0.0f, options.ManualCaptureCooldownSeconds ) ) {
          m_lastStatus = $"manual_export_cooldown:{options.ManualCaptureKey}";
          UpdateOverlayStatus( stepId: m_manualCaptureStepId );
          return;
        }

        if ( !TryExportManualSample( "manual_hotkey", out var imagePath, out _, out var error ) ) {
          m_lastStatus = $"manual_export_write_failed:{error}";
          UpdateOverlayStatus( stepId: m_manualCaptureStepId );
          return;
        }

        m_lastManualCaptureTime = Time.unscaledTime;
        m_lastStatus = $"manual_export_ok:{imagePath}";
        UpdateOverlayStatus( stepId: m_manualCaptureStepId );
        return;
      }

      m_lastStatus =
        $"manual_export_ready:capture={options.ManualCaptureKey},episode={options.ManualAdvanceEpisodeKey},ep={m_labelExporter.CurrentEpisodeIndex},next_step={m_manualCaptureStepId + 1}";
      UpdateOverlayStatus( stepId: m_manualCaptureStepId );
    }

    [ContextMenu( "Capture Manual Export Sample" )]
    public void CaptureManualExportSample()
    {
      ResolveReferences();
      EnsureHelpers();

      if ( !TryExportManualSample( "context_menu", out var imagePath, out _, out var error ) ) {
        m_lastStatus = $"manual_export_write_failed:{error}";
        UpdateOverlayStatus( stepId: m_manualCaptureStepId );
        return;
      }

      m_lastManualCaptureTime = Time.unscaledTime;
      m_lastStatus = $"manual_export_ok:{imagePath}";
      UpdateOverlayStatus( stepId: m_manualCaptureStepId );
    }

    [ContextMenu( "Advance Manual Export Episode" )]
    public void AdvanceManualExportEpisode()
    {
      ResolveReferences();
      EnsureHelpers();
      AdvanceManualExportEpisodeInternal( "context_menu" );
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

    private bool TryExportManualSample( string trigger, out string imagePath, out string labelPath, out string error )
    {
      imagePath = string.Empty;
      labelPath = string.Empty;
      error = string.Empty;

      if ( m_fpvCamera == null || m_labelExporter == null ) {
        error = "manual_export_dependencies_missing";
        return false;
      }

      var manualStepId = NextManualCaptureStepId();
      if ( !TryBuildTrainingFrameSample( manualStepId, out var frameSample, out error ) )
        return false;

      if ( !m_labelExporter.TryExport( frameSample, m_configuration.Dataset, out imagePath, out labelPath, out error ) )
        return false;

      m_lastExportedStepId = manualStepId;
      if ( !string.IsNullOrWhiteSpace( trigger ) )
        m_lastStatus = $"manual_export_ok:{trigger}:{imagePath}";
      return true;
    }

    private void AdvanceManualExportEpisodeInternal( string trigger )
    {
      if ( m_labelExporter == null ) {
        m_lastStatus = "manual_export_episode_advance_failed:exporter_missing";
        UpdateOverlayStatus( stepId: m_manualCaptureStepId );
        return;
      }

      m_labelExporter.AdvanceEpisode();
      m_manualCaptureStepId = 0;
      m_lastExportedStepId = long.MinValue;
      m_lastObservedStepId = long.MinValue;
      m_resetLatch = false;
      m_lastStatus = $"manual_export_episode_advanced:{trigger}:ep={m_labelExporter.CurrentEpisodeIndex}";
      UpdateOverlayStatus( stepId: m_manualCaptureStepId );
    }

    private void HandleEpisodeTransitions()
    {
      if ( m_stepAckServer == null || m_labelExporter == null )
        return;

      var requestType = m_stepAckServer.LastRequestTypeName;
      var stepId = m_stepAckServer.LastRequestStepId;
      if ( requestType == "ResetReq" ) {
        if ( !m_resetLatch ) {
          m_labelExporter.AdvanceEpisode();
          m_lastExportedStepId = long.MinValue;
          m_lastObservedStepId = long.MinValue;
          m_resetLatch = true;
        }

        return;
      }

      m_resetLatch = false;
      if ( requestType == "StepReq" && stepId >= 0 ) {
        if ( m_lastObservedStepId != long.MinValue && stepId < m_lastObservedStepId ) {
          m_labelExporter.AdvanceEpisode();
          m_lastExportedStepId = long.MinValue;
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

    private long NextManualCaptureStepId()
    {
      m_manualCaptureStepId += 1;
      return m_manualCaptureStepId;
    }

    private static long NowUnixTimeNs()
    {
      return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L;
    }
  }
}
