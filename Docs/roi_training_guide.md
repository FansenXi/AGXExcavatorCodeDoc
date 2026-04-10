# ROI Training Guide

Updated: 2026-04-09

## Goal

Training data export still happens in Unity, but runtime inference now happens in the external ROI sidecar instead of Barracuda.

Operator checklist:

- [roi_training_checklist.md](/C:/Users/fansen/AGXUnityExcavator/Assets/AGXUnity_Excavator/Docs/roi_training_checklist.md)

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
ROI_Dataset/
  images/
    train/
    val/
  labels/
    train/
    val/
```

The root folder is configurable through `RoiEncConfiguration.Dataset.RootDirectory`.

The raw `jpg + txt` directory is no longer the final training source of truth.
The standardized pipeline is now:

1. Unity exports raw YOLO-style scattered files into `ROI_Dataset/`
2. `tools/roi_dataset_tool.py ingest` packs each episode into `ROI_HDF5/roi_episode_XXXX.hdf5`
3. `tools/roi_eval.py dataset-quality` scores dataset readiness
4. `tools/roi_dataset_tool.py export` materializes a deduplicated YOLO view under `ROI_Export/`
5. `tools/roi_train.py` wraps export -> train -> ONNX export -> model eval

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
- `ManualExport`: captures on the 12th, 24th, 36th... local fixed step only after manual recording has been started
- `F10`: start or stop ROI recording
- `F11`: seal the current ROI recording episode without starting the next one automatically
- `R`: reset the task episode only; the next ROI recording still waits for another `F10`

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
2. Let the task episode run, but keep ROI recording idle until the framing and target state are ready.
3. Press `F10` to start ROI recording, then let the exporter capture automatically every 12 local steps.
4. Press `F10` again to stop recording, or press `F11` to seal the current recording episode explicitly.
5. If you reset the task with `R`, start the next ROI recording manually with another `F10`.
6. If you later need protocol-aligned samples, switch to `TrainingExport` and collect through step-ack episodes with the same 12-step interval.
7. End the recording so Unity writes `episode_XXXX_manifest.json` into `ROI_Dataset/manifests/`.
8. Pack the raw episode into HDF5 with `tools/roi_dataset_tool.py ingest`.
9. Run `tools/roi_eval.py dataset-quality` and inspect the report before training.
10. Export a deduplicated YOLO view from HDF5, or let `tools/roi_train.py` do it for you.
11. Train a detector and export ONNX.
12. Place the ONNX file outside `Assets/`, for example under `_model_archive/`.
13. Update `tools/roi_runtime_config.yaml` so `detector.model_path` points to that ONNX file.
14. Run `tools/run_roi_overlay_runtime.ps1 -SelfTest`.
15. Press Play in Unity with `RoiDetectionPipeline` set to `ExternalOverlayRuntime`.

## Example Commands

```bash
python tools/roi_dataset_tool.py ingest
python tools/roi_eval.py dataset-quality
python tools/roi_dataset_tool.py export
python tools/roi_train.py --epochs 100 --imgsz 960
```

Direct model-only evaluation after training:

```bash
python tools/roi_eval.py model-eval --model ..\..\ROI_Runs\roi_yolo11n\weights\best.pt --imgsz 960
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
- `tools/roi_dataset_tool.py ingest` creates one `roi_episode_XXXX.hdf5` per episode
- `tools/roi_eval.py dataset-quality` reports non-empty `train` and `val` splits before training
- boxes stay inside `[0,1]`
- bucket and target labels remain visible in the main task phases
- `tools/run_roi_overlay_runtime.ps1 -SelfTest` succeeds
- external runtime preview shows detections on live `STEP_RESP` frames
