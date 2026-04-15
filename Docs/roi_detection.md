# ROI Detection

Updated: 2026-04-10

## Summary

The formal runtime path is now Unity-internal native TensorRT.

- visual model classes:
  - `bucket`
  - `excavator_arm`
  - `truck`
  - `container`
- `dig_area` is not a model class
- `dig_area` is emitted as a rule ROI from `DigAreaMeasurement`

The external Python sidecar is no longer the primary low-latency path.

## Runtime Data Flow

1. `TrackedCameraWindow` renders FPV to a `RenderTexture`.
2. `NativeTensorRtDetectorBackend` downsamples to model input size and feeds the native TensorRT plugin.
3. The native plugin runs:
   - preprocess
   - TensorRT inference
   - bbox decode
   - confidence filter
   - per-class NMS
4. The plugin returns explicit detections as `[N,6] = x1,y1,x2,y2,score,class_id`.
5. `OnnxRoiDetector` consumes explicit detections for the native backend.
6. `RuleRoiProvider` adds `dig_area` as a rule ROI.
7. `RoiFusionPipeline` merges visual detections and rule ROI.
8. `RoiOverlayVisualizer` draws boxes in the FPV window.
9. `RoiInfoWindow` shows live debug stats and per-class matching.

## Output Contract

The native backend must return explicit detections only.

- `class_id` range: `0..3`
- labels:
  - `0 = bucket`
  - `1 = excavator_arm`
  - `2 = truck`
  - `3 = container`

`Unknown` should not appear during normal visual detection. If it does, treat it as a contract or mapping bug.

## Rule ROI

`dig_area` comes from the rule ROI path.

- source component: `DigAreaMeasurement`
- provider: `RuleRoiProvider`
- runtime source tag: `RuleRoi`
- output category label: `dig_area`

This keeps the visual model focused on appearance-driven targets while preserving a stable region ROI.

## Unity Modes

- `ManualExport`
  - local data collection
- `TrainingExport`
  - step-ack aligned export
- `NativeDetection`
  - formal runtime detection path
- `ExternalOverlayRuntime`
  - legacy helper path only

## Live Debug

Two Unity debug views matter:

- `RoiOverlayVisualizer`
  - draws boxes in the FPV window
  - tags each ROI as `Visual` or `Rule`
- `RoiInfoWindow`
  - shows:
    - `infer_ms`
    - native `raw / filter / nms / final`
    - `unknown` count
    - per-class `pred / gt / match / miss / fp`

## Key Files

- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/RoiDetectionPipeline.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Detection/OnnxRoiDetector.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Detection/Backend/NativeTensorRtDetectorBackend.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Auxiliary/RuleRoiProvider.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Fusion/RoiFusionPipeline.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Debug/RoiOverlayVisualizer.cs`
- `AGXUnity_Excavator_Assets/Scripts/ROIEnc/Debug/RoiInfoWindow.cs`
- `NativePlugins/TrtRoiBackend/src/trt_roi_api.cpp`
- `NativePlugins/TrtRoiBackend/src/trt_roi_engine.cpp`

## Runtime Acceptance

The runtime is considered healthy when:

- `ROI count` is near the visible object count
- `Unknown = 0`
- `dig_area` appears as `Rule`, not as visual detection
- native stats show `raw >> final`
- `infer_ms` is stable on the native TensorRT path
