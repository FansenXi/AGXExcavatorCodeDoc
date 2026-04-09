# ROI Auxiliary Signal Evaluation

Updated: 2026-04-08

## Purpose

Phase 1 includes two auxiliary mechanisms that can be compared against pure-vision detection:

- `MotionIntensityEstimator`
- `KinematicBucketProjector`

The first one is observational in Phase 1. The second one can actively inject a bucket fallback ROI when the detector misses the bucket for multiple detection cycles.

## Experiment Groups

- `VisualOnly`
- `VisualPlusMotionIntensity`
- `VisualPlusKinematicFallback`
- `VisualPlusAllSignals`

`RoiDetectionPipeline` exposes the experiment group through `m_experimentGroup`.

## Logged Metrics

When CSV logging is enabled, `RoiDetectionLogger` records:

- time
- frame id
- step id
- experiment group
- motion intensity
- total ROI count
- bucket detected flag
- best bucket IoU against scene-graph GT
- best bucket confidence
- whether kinematic fallback was used

## Recommended Test Conditions

Use the same episode set across all experiment groups and compare:

- nominal lighting
- heavy motion
- partial occlusion
- dust or clutter overlays
- backlit cases

## Decision Rule

Keep an auxiliary path only if it improves one of the following without causing large false-positive regressions:

- bucket detection recall
- bucket recovery latency after misses
- ROI temporal stability
- GT IoU consistency

## Current Implementation Notes

- `MotionIntensityEstimator` uses transform motion as a lightweight proxy.
- `KinematicBucketProjector` projects bucket renderer bounds through the current FPV camera.
- kinematic fallback is guarded by consecutive bucket misses and confidence/padding settings in `RoiEncConfiguration.Fusion`.
