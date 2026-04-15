# ROI Training Guide

Updated: 2026-04-10

## Summary

The training target has changed.

- visual detection classes:
  - `bucket`
  - `excavator_arm`
  - `truck`
  - `container`
- `dig_area` is not trained as a detection class
- `dig_area` is provided at runtime by rule ROI
- deployment target: `640x640 + TensorRT FP16`
- capture resolution stays `1920x1080`

## Operator Checklist

- [roi_training_checklist.md](/C:/Users/fansen/AGXUnityExcavator/Assets/AGXUnity_Excavator/Docs/roi_training_checklist.md)

## Dataset Semantics

Use two sets:

- `train/val`
  - main training and validation data
- `benchmark`
  - a frozen subset of repeatable episodes kept outside the main training batch for comparison across model versions

The HDF5 pipeline remains the source of truth. Raw `jpg + txt` exports are only staging data.

## Recording Defaults

- `RoiDetectionPipeline = ManualExport`
- `EpisodeManager command source = Keyboard`
- `AgxSimStepAckServer = disabled`
- export size = `1920x1080`
- automatic capture every `12` steps
- `F10` start / stop ROI recording
- `F11` seal the current recording episode

## Class Set

The only trained visual classes are:

1. `bucket`
2. `excavator_arm`
3. `truck`
4. `container`

Do not record `dig_area` as a training label for the next dataset generation.

## Pipeline

1. Record raw samples in Unity.
2. Pack raw episodes into HDF5:

```bash
python tools/roi_dataset_tool.py ingest
```

3. Run dataset quality evaluation:

```bash
python tools/roi_eval.py dataset-quality
```

4. Train and export:

```bash
python tools/roi_train.py --epochs 100 --imgsz 640
```

5. Optional standalone validation:

```bash
python tools/roi_eval.py model-eval --model ..\..\ROI_Runs\roi_yolo11n_4class\weights\best.pt --imgsz 640
```

## Deployment Artifacts

Training produces:

- `best.pt`
- `best.onnx`

The Unity native runtime consumes a TensorRT engine:

- default path: `_model_archive/roi_detector.engine`
- Unity setting: `RoiEncConfiguration.Detection.TensorRtEnginePath`

Convert the exported ONNX to an engine with your TensorRT workflow before switching the runtime model.

## Readiness Rules

Before starting a formal training run:

- `train` and `val` must both be non-empty
- all 4 visual classes must have coverage
- random labels must look correct
- benchmark episodes must be kept separate from the main training batch

## Runtime Rollout

After training:

1. export ONNX
2. build / refresh the TensorRT engine
3. point `TensorRtEnginePath` at the new engine
4. switch Unity to `NativeDetection`
5. verify:
   - `Unknown = 0`
   - `dig_area` comes from `Rule`
   - `ROI count` is reasonable
   - `infer_ms` stays within target
