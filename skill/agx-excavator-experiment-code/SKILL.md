---
name: agx-excavator-experiment-code
description: Work on AGXUnity excavator experiment runtime — episodes, reset, target mass, bucket-target distance, logging, HUD. Use when editing EpisodeManager, SceneResetService, SwitchableTargetMassSensor, TargetMassSensorBase, TruckBedMassSensor, TerrainParticleBoxMassSensor, BucketTargetDistanceMeasurementUtility, ExperimentLogger, TeleopEpisodeExporter, ExperimentHUD, or ExcavationMassTracker. Triggers on episode export, teleop recording, target switch, truck bed mass, container target, reset terrain, experiment HUD.
---

# Excavator experiment and measurement code

## Mental model

- One active dump target at a time (`ContainerBox` vs `TruckBed`); exported fields refer to the **active** target.
- Mission signals combine bucket mass, excavation tracker, target retained mass, reset-relative deposited mass, and approximate bucket–target distance.

## Suggested change order

1. **Sensors / utilities** — `TargetMassSensorBase.cs`, `SwitchableTargetMassSensor.cs`, `TruckBedMassSensor.cs`, `TerrainParticleBoxMassSensor.cs`, `BucketTargetDistanceMeasurementUtility.cs`.
2. **Aggregation** — `ActObservationCollector.cs` (if new signals join `env_state`).
3. **Lifecycle** — `EpisodeManager.cs`, `SceneResetService.cs`, `ExperimentLogger.cs`, `TeleopEpisodeExporter.cs`.
4. **Presentation** — `ExperimentHUD.cs` (display only; avoid duplicating business rules).

## Reset and baseline

When changing mass baselines or target switching, trace `SceneResetService` and sensor reset paths so reset-relative `deposited_mass_in_target_box_kg` stays coherent.

## Documentation

If behavior visible on the wire or in `env_state` changes, update `Docs/scene.md` and `Docs/protocol.md` in the same task.

Match existing naming, serialization patterns, and logging style in this folder; avoid new dependencies unless the repo already uses them.
