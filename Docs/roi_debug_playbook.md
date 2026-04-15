# ROI Debug Playbook

Updated: 2026-04-10

## Use This Order

1. Check `Status` in `RoiInfoWindow`
2. Check `Unknown`
3. Check native `raw / filter / nms / final`
4. Check per-class `pred / gt / match / miss / fp`
5. Only then judge the model

## Good Runtime Signs

- `Unknown = 0`
- `raw` is much larger than `final`
- `ROI count` is near the visible object count
- `dig_area` is shown as `Rule`
- `infer_ms` is stable

## If You See Hundreds or Thousands of Boxes

Most likely causes:

- raw tensor was parsed with the wrong contract
- native postprocess did not run
- explicit `[N,6]` output was misread as a YOLO tensor

First checks:

- verify native backend returns explicit detections
- verify Unity consumes `[N,6]`
- verify `Unknown` is not rising with box count

## If You See `Unknown`

Treat it as a bug, not as a valid class.

Typical causes:

- `class_id` out of range
- label mapping drift
- raw tensor misparse

## If `dig_area` Looks Wrong

Debug the rule path, not the model.

`dig_area` should come from:

- `RuleRoiProvider`
- `DigAreaMeasurement`

## If Accuracy Looks Weak

Check these in order:

1. live per-class match counts
2. benchmark eval results
3. label quality
4. class coverage
5. hard-case coverage:
   - bucket edge-of-frame
   - bucket occlusion
   - container overlap
   - fast swing

If labels or class semantics are wrong, fix the dataset first and re-record.
