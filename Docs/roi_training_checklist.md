# ROI Training Checklist

Updated: 2026-04-10

## A. Pre-Flight

- [ ] Unity scene is open in `AGXUnity_Excavator.unity`
- [ ] `RoiDetectionPipeline = ManualExport`
- [ ] `EpisodeManager` is enabled
- [ ] `EpisodeManager command source = Keyboard`
- [ ] `AgxSimStepAckServer` is disabled
- [ ] export size is `1920x1080`
- [ ] automatic capture interval is `12` steps
- [ ] dataset root points to `ROI_Dataset`
- [ ] HDF5 root points to `ROI_HDF5`
- [ ] Python env `.venv-roi` is available

## B. Control Reminder

- [ ] `Enter` starts the task episode
- [ ] `Backspace` stops the task episode
- [ ] `R` resets the task episode only
- [ ] `F10` starts or stops ROI recording
- [ ] `F11` seals the current ROI recording episode
- [ ] `F8/F9` cycles target

## C. Recording Rules

- [ ] recording episode is treated separately from task episode
- [ ] do not start recording until framing and target state are ready
- [ ] vary start pose, swing angle, boom height, and rhythm across episodes
- [ ] record both `truck` and `container` targets
- [ ] keep a small frozen benchmark subset outside the main training batch

## D. Visual Class Coverage

- [ ] all 4 visual classes appear in the batch:
  - [ ] `bucket`
  - [ ] `excavator_arm`
  - [ ] `truck`
  - [ ] `container`
- [ ] `dig_area` is treated as rule ROI, not as a trained visual class

## E. Batch Goals

- [ ] at least `8` recording episodes collected
- [ ] preferred first formal batch reaches `15-20` episodes
- [ ] deduplicated frame count is at least `500` before first trial training
- [ ] preferred deduplicated frame count reaches `1500-3000`

## F. After Recording

- [ ] Unity wrote manifests under `ROI_Dataset/manifests/`
- [ ] raw dataset still has matching `images/` and `labels/`
- [ ] run ingest:

```bash
python tools/roi_dataset_tool.py ingest
```

- [ ] run quality evaluation:

```bash
python tools/roi_eval.py dataset-quality
```

- [ ] `train` split is non-empty
- [ ] `val` split is non-empty
- [ ] all 4 visual classes have non-zero coverage
- [ ] random labels look correct

## G. Training

- [ ] run:

```bash
python tools/roi_train.py --epochs 100 --imgsz 640
```

- [ ] confirm `best.pt` exists
- [ ] confirm ONNX export exists
- [ ] confirm eval report / train report updated
- [ ] confirm `ROI_Runs/eval_history.csv` has a new row

## H. Runtime Rollout

- [ ] export ONNX from the selected checkpoint
- [ ] build or refresh the TensorRT engine under `_model_archive/`
- [ ] update `RoiEncConfiguration.Detection.TensorRtEnginePath` if needed
- [ ] switch Unity to `NativeDetection`
- [ ] press Play and confirm:
  - [ ] `Unknown = 0`
  - [ ] `dig_area` appears as `Rule`
  - [ ] `ROI count` is reasonable
  - [ ] native `infer_ms` is within target

## I. Done

- [ ] dataset is packed in `ROI_HDF5/`
- [ ] readiness gate is passed
- [ ] trained model exists
- [ ] TensorRT engine points at the selected model
- [ ] native runtime produces valid overlay output
