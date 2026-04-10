# ROI Training Checklist

Updated: 2026-04-09

## Use

This checklist is the operator-facing runbook for ROI dataset collection, dataset packing, quality review, training, and runtime rollout.

Mark each item only when it is actually complete.

## A. Pre-Flight

- [ ] Unity scene is open in `AGXUnity_Excavator.unity`.
- [ ] `RoiDetectionPipeline` mode is `ManualExport`.
- [ ] `EpisodeManager` is enabled.
- [ ] `EpisodeManager` command source is `Keyboard`.
- [ ] `AgxSimStepAckServer` is disabled.
- [ ] `RoiEncConfiguration.Dataset.ExportWidth = 1920`.
- [ ] `RoiEncConfiguration.Dataset.ExportHeight = 1080`.
- [ ] `AutomaticCaptureEverySteps = 12`.
- [ ] `SwitchableTargetMassSensor` has an active target.
- [ ] `DigAreaMeasurement` exists and is valid in the scene.
- [ ] Dataset root points to `C:\Users\fansen\AGXUnityExcavator\ROI_Dataset`.
- [ ] HDF5 root is ready at `C:\Users\fansen\AGXUnityExcavator\ROI_HDF5`.
- [ ] Python environment `.venv-roi` is available.

## B. Control Reminder

- [ ] `Enter` starts the task episode.
- [ ] `Backspace` stops the task episode.
- [ ] `R` resets the task episode only.
- [ ] `F10` starts ROI recording when idle.
- [ ] `F10` stops ROI recording when already recording.
- [ ] `F11` seals the current ROI recording episode.
- [ ] `F6/F7` cycles control source.
- [ ] `F8/F9` cycles target.

## C. Recording Strategy

- [ ] Recording episode definition is treated as independent from task episode definition.
- [ ] Do not start ROI recording until camera framing and target state are ready.
- [ ] Use different initial poses across recording episodes.
- [ ] Mix both `Truck` and `Container` targets across the batch.
- [ ] Mix faster and slower operator rhythms across the batch.
- [ ] Mix different swing angles and boom heights across the batch.
- [ ] Keep at least one stable benchmark subset for later repeatable eval.

## D. Per-Episode Capture Checklist

- [ ] Start the task episode.
- [ ] Move the excavator into a useful starting pose.
- [ ] Confirm the target and dig area are visible or will enter view during this cycle.
- [ ] Press `F10` to start ROI recording.
- [ ] Cover stage 1: approach / align to dig area.
- [ ] Cover stage 2: digging / scooping.
- [ ] Cover stage 3: lifting away from dig area.
- [ ] Cover stage 4: swing toward target.
- [ ] Cover stage 5: dumping over target.
- [ ] Include overlap cases where `bucket` and `truck/container` are both visible.
- [ ] Include partial visibility and edge-of-frame motion when possible.
- [ ] Stop recording with `F10` or seal it with `F11`.
- [ ] Confirm the HUD shows recording stopped before starting the next setup.

## E. Batch Coverage Goals

- [ ] At least `8` recording episodes collected.
- [ ] Preferred first formal batch reaches `15-20` recording episodes.
- [ ] All 5 classes appear in the batch:
  - [ ] `bucket`
  - [ ] `excavator_arm`
  - [ ] `truck`
  - [ ] `container`
  - [ ] `dig_area`
- [ ] Both `train` and `val` splits contain frames after ingest.
- [ ] Deduplicated total frame count is at least `500` before first trial training.
- [ ] Preferred deduplicated total frame count reaches `1500-3000`.

## F. After Recording

- [ ] Unity has written manifest files under `ROI_Dataset/manifests/`.
- [ ] Raw dataset still contains matching `images/` and `labels/`.
- [ ] Run dataset ingest:

```bash
python tools/roi_dataset_tool.py ingest
```

- [ ] Run dataset quality evaluation:

```bash
python tools/roi_eval.py dataset-quality
```

- [ ] Open `ROI_HDF5/quality_report.json`.
- [ ] Check `quality.status`.
- [ ] Check `quality.issues`.
- [ ] Check `split_kept_counts.train > 0`.
- [ ] Check `split_kept_counts.val > 0`.
- [ ] Check no class in `class_histogram` is `0`.
- [ ] Check `dedup_rate` is not obviously extreme for the whole batch.

## G. Quality Gate

- [ ] Status is not `blocked`.
- [ ] `train` split is non-empty.
- [ ] `val` split is non-empty.
- [ ] Fewer than 8 episodes warning has been cleared.
- [ ] Zero-coverage class warning has been cleared.
- [ ] A random sample of raw labels looks visually correct.

## H. Training

- [ ] Start training only after the quality gate is met.
- [ ] Run the pipeline:

```bash
python tools/roi_train.py --epochs 100 --imgsz 960
```

- [ ] Wait for export -> train -> ONNX export -> eval to finish.
- [ ] Confirm `ROI_Runs/.../weights/best.pt` exists.
- [ ] Confirm ONNX export exists.
- [ ] Confirm `ROI_HDF5/eval_report.json` or train report has been updated.
- [ ] Confirm `ROI_Runs/eval_history.csv` has a new row.

## I. Runtime Rollout

- [ ] Move the selected ONNX model outside `Assets/`, for example into `_model_archive/`.
- [ ] Update `tools/roi_runtime_config.yaml` so `detector.model_path` points to the trained ONNX.
- [ ] Run runtime self-test:

```bash
tools/run_roi_overlay_runtime.ps1 -SelfTest
```

- [ ] Confirm self-test video and CSV are generated.
- [ ] Switch Unity to `ExternalOverlayRuntime`.
- [ ] Press Play and confirm the sidecar starts.

## J. Done Criteria

- [ ] The dataset is packed in `ROI_HDF5/` as append-only per-episode files.
- [ ] The dataset has passed the readiness gate.
- [ ] A trained ONNX model exists.
- [ ] Runtime config points to the trained model.
- [ ] External runtime produces valid overlay output.

## K. Common Failure Notes

- [ ] If dedup is too high, record more motion-heavy segments.
- [ ] If `train` is empty, collect more recording episodes.
- [ ] If a class is missing, verify the target setup and camera framing.
- [ ] If ingest reports inconsistent image shapes, keep the same export resolution within a batch.
- [ ] If manifest is missing, stop or seal recording before leaving Play mode.
