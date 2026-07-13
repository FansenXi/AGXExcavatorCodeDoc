# YuLong Integration Status

Last updated: 2026-07-07

- Current stage: Unity control/telemetry adaptation for the active YuLong scene.
- Replay determinism branch: `codex/yulong-replay-determinism-reset`, based on
  `fs/Yulong-v2_2@dcb81bc`, is the active debugging branch for replay drift.
- Confirmed so far: YuLong wrapper is present, four semantic constraints are assigned, and the shared machine controller, ACT observation collector, scene reset service, active-target collision monitor, bucket locator, and mass telemetry path now resolve the YuLong rig directly.
- Control setup: the controller treats YuLong as a first-class rig kind, uses the scene-assigned YuLong axis directions, and drives swing / boom / stick / bucket through the YuLong semantic constraints.
- Control soft limits: `ExcavatorMachineController` loads `YuLong_norm.json` as a calibrated raw-angle soft-limit profile. When a normalized axis position is at/outside `[0, 1]`, outward velocity commands are forced to zero with an immediate stop, while inward commands remain allowed so the joint can recover into range.
- Swing stabilization: YuLong now captures the swing hinge angle when the commanded swing speed enters the neutral dead zone and holds that lock target until an explicit swing command releases it. The target is no longer refreshed every neutral frame, which blocks passive swing jumps when the bucket or machine rubs rigid-body geometry such as the DigArea bottom.
- Naming: YuLong runtime code now resolves the actual bucket as `bucket` first; `watou` is kept only as a deprecated fallback for older imported CAD/prefab variants.
- Mass setup: the active `DumpArea` path remains backed by `TerrainParticleBoxMassSensor`; target mass, bucket mass, target distance, reset baselines, and hard-collision monitoring all resolve the YuLong machine root / `bucket` reference.
- DigArea measurement: YuLong DigArea touch/depth telemetry samples the `bucket` `DeformableTerrainShovel` cutting edge, tooth direction, and top edge before falling back to the legacy bucket DigArea proxy box. This keeps `min_distance_to_dig_area_m` and `bucket_depth_below_dig_area_plane_m` tied to the actual shovel edge rather than the old E85 proxy volume.
- V2.2 data contract: the step-ack server now reports a 64D add-only `env_state`; old 0-27 indices are unchanged, and new 28-63 indices include bucket tip local pose, 3x2 surface/removed/target depth grids, valid masks, bucket mass delta, dump/offtarget deposition fields, contact masks, and collision count.
- Replay determinism diagnostics: reset now reports seed/status/terrain reset
  details through `reset_diagnostic:*` response warnings, and step responses can
  report bucket-vs-external-shape contact count and peak normal force through
  `bucket_contact_diagnostic:*` warnings. Step responses can also report
  `bucket_mass_diagnostic:*` warnings to split bucket load into raw reported
  mass, AGX terrain dynamic mass, handled-as-particle rigid-body mass, and live
  terrain soil particle count. Diagnostic `REALIGN_POSE` requests can lock the
  four actuator positions to a requested qpos to inspect qpos drift, but any run
  using realign remains diagnostic-only and cannot enter strict gold replay or
  calibrated label truth. These diagnostics do not extend `env_state`.
- Dynamic soil reset: ordinary `AGXUnity.Model.DeformableTerrain` instances are
  cleared through native `Terrain.clearAllSoilParticles()` before and after
  terrain recreation; non-standard terrain providers keep the older
  per-particle fallback.
- Soil determinism status: `RESET.seed` is applied to Unity's managed random
  source, but no AGX deformable-terrain soil/native seed API is confirmed in
  this branch. The reset diagnostic therefore reports
  `soil_seed_status=not_supported` rather than claiming strict soil determinism.
- Gold replay status: old 2026-05-15 source HDF5 episodes are diagnostic-only
  until the strict replay gate passes. Gold samples require no realign, no
  exceptions, short A/A qpos and bucket-mass stability, and same-branch fresh
  source replay reproducibility.
- Terrain setup: measured terrain builders and repair/audit utilities derive Dig/Dump terrain XZ from the current `Dig_Footprint` / `Dump_Footprint` minus measured board thickness. The current measured inner-board terrain size is `2.4m x 2.9m` for both DigTerrain and DumpTerrainReceiver.
- Validation note: `dotnet build AGXUnityE85ExcavatorSim.sln --no-restore`
  passes after restore. `CodexPlayModeBootstrap` refreshes the asset database
  before the request-file smoke transition so edited diagnostics are imported
  before entering Play Mode.
- Scope reminder: current phase remains fixed-station 4D arm control only, with no track/drive/steer integration.
