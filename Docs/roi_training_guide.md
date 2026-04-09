# ROI Training Guide

Updated: 2026-04-09

## Goal

Training data export still happens in Unity, but runtime inference now happens in the external ROI sidecar instead of Barracuda.

## Label Classes

The current class set is:

1. `bucket`
2. `excavator_arm`
3. `truck`
4. `container`
5. `dig_area`

## Export Path

`RoiDetectionPipeline` writes samples under the same dataset root in both `ManualExport` and `TrainingExport` modes:

```text
AGXUnity_Excavator_Assets/ROI_Dataset/
  images/
    train/
    val/
  labels/
    train/
    val/
```

The root folder is configurable through `RoiEncConfiguration.Dataset.RootDirectory`.

## Recommended Capture Modes

- `ManualExport`: preferred for local data collection while driving the excavator directly in Unity. This mode does not require the Python ACT backend or the step-ack protocol. It uses a local fixed-step counter and captures automatically every configured step interval.
- `TrainingExport`: use this when you need exported samples to stay aligned with step-ack requests, resets, and externally driven episodes. The same step-interval rule applies, but the interval is measured against step-ack progression inside each episode.

For the current scene, the intended default recording path is:

- `RoiDetectionPipeline = ManualExport`
- `EpisodeManager command source = Keyboard`
- `AgxSimStepAckServer = disabled`
- `Automatic step sampling = enabled`
- `Automatic capture interval = 12 steps`

## Split Logic

- split unit: episode
- split policy: deterministic hash of episode index
- default split: `80/20`

In `TrainingExport`, an episode advances when the step-ack server receives a reset request or when step ids roll backwards.

In `ManualExport`, the exporter follows `EpisodeManager` episodes when that component is active. Without step-ack, the capture cadence still works because the exporter falls back to Unity `FixedUpdate` steps.

## Sampling Policy

- default policy: automatic step sampling enabled
- default interval: every `12` steps
- `TrainingExport`: captures on the 12th, 24th, 36th... step within each step-ack episode
- `ManualExport`: captures on the 12th, 24th, 36th... local fixed step within each local episode
- manual hotkey capture remains available as an override for edge cases

## Label Sources

`SceneGraphLabelGenerator` currently derives labels from:

- bucket renderer hierarchy
- arm renderer hierarchy near the bucket chain
- active target measurement volume from `SwitchableTargetMassSensor.CurrentTarget`
- dig area measurement volume from `DigAreaMeasurement`

Bounding boxes are projected into normalized top-left image coordinates and filtered by minimum area.

## Exported Sample Format

- image format: `jpg`
- label format: YOLO `txt`
- image size: configured by `RoiEncConfiguration.Dataset.ExportWidth/ExportHeight`
- default size: `1920x1080`

## Recommended Training Loop

1. Run Unity in `ManualExport` mode and drive the excavator with the `EpisodeManager` keyboard source for local collection.
2. Start an episode and let the exporter capture automatically every 12 local steps.
3. Use the manual capture hotkey only when you want to force an extra sample outside the regular cadence.
4. If you later need protocol-aligned samples, switch to `TrainingExport` and collect through step-ack episodes with the same 12-step interval.
5. Inspect a random subset of exported `jpg/txt` pairs.
6. Train a detector on the exported dataset.
7. Export an ONNX model for the external runtime.
8. Place the ONNX file outside `Assets/`, for example under `_model_archive/`.
9. Update `tools/roi_runtime_config.yaml` so `detector.model_path` points to that ONNX file.
10. Run `tools/run_roi_overlay_runtime.ps1 -SelfTest`.
11. Press Play in Unity with `RoiDetectionPipeline` set to `ExternalOverlayRuntime`.

## Example Commands

```bash
yolo detect train model=yolo11n.pt data=AGXUnity_Excavator_Assets/ROI_Dataset/dataset.yaml epochs=100 imgsz=640
yolo export model=best.pt format=onnx opset=17
```

Suggested runtime placement:

```text
_model_archive/
  best.onnx
  roi_smoke_detector.onnx
```

## Validation Checklist

- image count matches label count
- no empty or malformed YOLO rows
- boxes stay inside `[0,1]`
- bucket and target labels remain visible in the main task phases
- `tools/run_roi_overlay_runtime.ps1 -SelfTest` succeeds
- external runtime preview shows detections on live `STEP_RESP` frames
