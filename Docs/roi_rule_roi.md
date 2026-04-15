# ROI Rule ROI

Updated: 2026-04-10

## Purpose

Rule ROI is the non-model ROI path.

Use it for regions that are:

- scene-defined
- stable
- better described by geometry than by appearance

In v1, rule ROI is used for `dig_area`.

## Current Source

- provider: `RuleRoiProvider`
- scene input: `DigAreaMeasurement`
- output category: `dig_area`
- output source: `RuleRoi`

The provider projects the dig-area footprint into the camera view and emits a `RoiDescriptor`.

## Why `dig_area` Moved Out of the Model

`dig_area` is not a normal object detection target:

- it is configured by the scene
- it does not need appearance-based recognition
- the intended ROI is the configured footprint, not a visually inferred terrain patch

Keeping it as rule ROI makes training simpler and runtime semantics clearer.

## Future Extension

The same rule ROI layer is intended to support future sources such as:

- user hand-drawn screen regions
- user-defined world-space work zones
- scenario-specific fixed safety zones
