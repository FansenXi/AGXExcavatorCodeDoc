# ROI Training Guide

Updated: 2026-04-08

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

`RoiDetectionPipeline` in `TrainingExport` mode writes samples under:

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

## Split Logic

- split unit: episode
- split policy: deterministic hash of episode index
- default split: `80/20`

An episode advances when the step-ack server receives a reset request or when step ids roll backwards.

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
- default size: `576x384`

## Recommended Training Loop

1. Run Unity in `TrainingExport` mode while driving or replaying step-ack episodes.
2. Inspect a random subset of exported `jpg/txt` pairs.
3. Train a detector on the exported dataset.
4. Export an ONNX model for the external runtime.
5. Place the ONNX file outside `Assets/`, for example under `_model_archive/`.
6. Update `tools/roi_runtime_config.yaml` so `detector.model_path` points to that ONNX file.
7. Run `tools/run_roi_overlay_runtime.ps1 -SelfTest`.
8. Press Play in Unity with `RoiDetectionPipeline` set to `ExternalOverlayRuntime`.

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
