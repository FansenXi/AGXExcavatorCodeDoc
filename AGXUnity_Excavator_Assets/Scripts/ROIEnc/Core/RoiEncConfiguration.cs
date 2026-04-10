using System;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  [Serializable]
  public sealed class RoiEncConfiguration
  {
    public enum YoloOutputLayout
    {
      Auto = 0,
      CandidateMajor = 1,
      FeatureMajor = 2
    }

    [Serializable]
    public sealed class DatasetOptions
    {
      public string RootDirectory = "../../ROI_Dataset";
      public int ExportWidth = 1920;
      public int ExportHeight = 1080;
      [Range( 0.05f, 0.95f )]
      public float ValidationSplit = 0.2f;
      public int JpegQuality = 90;
      public bool EnableImageExport = true;
      public bool EnableAutomaticStepSampling = true;
      [Min( 1 )]
      public int AutomaticCaptureEverySteps = 12;
      public bool EnableManualCaptureHotkeys = true;
      public KeyCode ManualCaptureKey = KeyCode.F10;
      public KeyCode ManualAdvanceEpisodeKey = KeyCode.F11;
      public bool CaptureWhileManualKeyHeld = false;
      [Min( 0.0f )]
      public float ManualCaptureCooldownSeconds = 0.15f;
    }

    [Serializable]
    public sealed class DetectionOptions
    {
      public string TensorRtEnginePath = "../../_model_archive/roi_detector.engine";
      public int ModelInputWidth = 640;
      public int ModelInputHeight = 640;
      public int DetectionIntervalFrames = 2;
      [Range( 0.0f, 1.0f )]
      public float ConfidenceThreshold = 0.35f;
      [Range( 0.0f, 1.0f )]
      public float NmsIouThreshold = 0.5f;
      public string OutputTensorName = string.Empty;
      public YoloOutputLayout OutputLayout = YoloOutputLayout.Auto;
      public Vector4 InputScale = Vector4.one;
      public Vector4 InputBias = Vector4.zero;
      public string[] ClassLabels = new[] { "bucket", "excavator_arm", "truck", "container", "dig_area" };
      [Min( 1 )]
      public int WarmupIterations = 3;
    }

    [Serializable]
    public sealed class SmoothingOptions
    {
      [Range( 0.0f, 1.0f )]
      public float EmaAlpha = 0.4f;
      [Range( 0.0f, 1.0f )]
      public float MatchIouThreshold = 0.3f;
      [Min( 0 )]
      public int LostFrameHoldCount = 3;
    }

    [Serializable]
    public sealed class FusionOptions
    {
      public bool EnableMotionIntensityEstimator = false;
      public bool EnableKinematicBucketFallback = false;
      [Min( 1 )]
      public int ConsecutiveBucketMissesBeforeFallback = 2;
      [Range( 0.0f, 1.0f )]
      public float KinematicFallbackConfidence = 0.55f;
      [Range( 0.0f, 1.0f )]
      public float KinematicFallbackPadding = 0.08f;
    }

    [Serializable]
    public sealed class ExternalRuntimeOptions
    {
      public bool AutoLaunchOnPlay = true;
      public bool CloseProcessOnDisable = true;
      public bool RestartIfExited = false;
      public bool CreateNoWindow = false;
      public string PythonExecutablePath = ".venv-roi/Scripts/python.exe";
      public string EntryScriptPath = "tools/roi_overlay_runtime.py";
      public string ConfigPath = "tools/roi_runtime_config.yaml";
      public string ExtraArguments = string.Empty;
    }

    [Serializable]
    public sealed class OverlayOptions
    {
      public bool Enabled = true;
      public bool DrawLabels = true;
      public bool DrawDebugPanel = true;
      public Rect FallbackOverlayRect = new Rect( 16.0f, 660.0f, 520.0f, 180.0f );
    }

    [Serializable]
    public sealed class LoggingOptions
    {
      public bool EnableCsvLogging = false;
      public string LogDirectory = "../../ExperimentLogs/roi_detection";
    }

    public DatasetOptions Dataset = new DatasetOptions();
    public DetectionOptions Detection = new DetectionOptions();
    public SmoothingOptions Smoothing = new SmoothingOptions();
    public FusionOptions Fusion = new FusionOptions();
    public ExternalRuntimeOptions ExternalRuntime = new ExternalRuntimeOptions();
    public OverlayOptions Overlay = new OverlayOptions();
    public LoggingOptions Logging = new LoggingOptions();
  }
}
