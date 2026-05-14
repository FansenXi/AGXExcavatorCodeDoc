# YuLong Integration Status

Last updated: 2026-05-14

- Current stage: Unity control/telemetry adaptation for the active YuLong scene.
- Confirmed so far: YuLong wrapper is present, four semantic constraints are assigned, and the shared machine controller, ACT observation collector, scene reset service, active-target collision monitor, bucket locator, and mass telemetry path now resolve the YuLong rig directly.
- Control setup: the controller treats YuLong as a first-class rig kind, uses the scene-assigned YuLong axis directions, and drives swing / boom / stick / bucket through the YuLong semantic constraints.
- Control soft limits: `ExcavatorMachineController` loads `YuLong_norm.json` as a calibrated raw-angle soft-limit profile. When a normalized axis position is at/outside `[0, 1]`, outward velocity commands are forced to zero with an immediate stop, while inward commands remain allowed so the joint can recover into range.
- Mass setup: the active `DumpArea` path remains backed by `TerrainParticleBoxMassSensor`; target mass, bucket mass, target distance, reset baselines, and hard-collision monitoring all resolve the YuLong machine root / `watou` bucket reference.
- DigArea measurement: YuLong DigArea touch/depth telemetry now samples the `watou` `DeformableTerrainShovel` cutting edge, tooth direction, and top edge before falling back to the legacy bucket DigArea proxy box. This keeps `min_distance_to_dig_area_m` and `bucket_depth_below_dig_area_plane_m` tied to the actual shovel edge rather than the old E85 proxy volume.
- Terrain setup: measured terrain builders and repair/audit utilities derive Dig/Dump terrain XZ from the current `Dig_Footprint` / `Dump_Footprint` minus measured board thickness. The current measured inner-board terrain size is `2.4m x 2.9m` for both DigTerrain and DumpTerrainReceiver.
- Validation note: `dotnet build AGXUnityE85ExcavatorSim.sln --no-restore` passes after restore. Play Mode motion/QC still needs an operator run in Unity to validate the mechanical joint origins/axes under load.
- Scope reminder: current phase remains fixed-station 4D arm control only, with no track/drive/steer integration.
