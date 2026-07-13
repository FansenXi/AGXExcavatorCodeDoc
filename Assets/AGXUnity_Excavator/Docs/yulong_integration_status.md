# YuLong Integration Status

Last updated: 2026-07-13

- Current stage: Unity control/telemetry adaptation for the active YuLong scene.
- Confirmed so far: YuLong wrapper is present, four semantic constraints are assigned, and the shared machine controller, ACT observation collector, scene reset service, active-target collision monitor, bucket locator, and mass telemetry path now resolve the YuLong rig directly.
- Control setup: the controller treats YuLong as a first-class rig kind, uses the scene-assigned YuLong axis directions, and drives swing / boom / stick / bucket through the YuLong semantic constraints.
- Control soft limits: `ExcavatorMachineController` loads `YuLong_norm.json` as a calibrated raw-angle soft-limit profile. When a normalized axis position is at/outside `[0, 1]`, outward velocity commands are forced to zero with an immediate stop, while inward commands remain allowed so the joint can recover into range.
- Swing stabilization: YuLong now captures the swing hinge angle when the commanded swing speed enters the neutral dead zone and holds that lock target until an explicit swing command releases it. The target is no longer refreshed every neutral frame, which blocks passive swing jumps when the bucket or machine rubs rigid-body geometry such as the DigArea bottom.
- Naming: YuLong runtime code now resolves the actual bucket as `bucket` first; `watou` is kept only as a deprecated fallback for older imported CAD/prefab variants.
- Shovel force feedback: `YuLongShovelSettings` caps `MaxPenetrationForce` at `30000 N` instead of the AGX default infinity. This keeps bucket-terrain penetration feedback in the same order as a mass-scaled Bobcat E85 bucket digging force while preserving real terrain reaction forces.
- Mass setup: the active `DumpArea` path remains backed by `TerrainParticleBoxMassSensor`; target mass, bucket mass, target distance, reset baselines, and hard-collision monitoring all resolve the YuLong machine root / `bucket` reference.
- DigArea measurement: YuLong DigArea penetration/depth telemetry keeps plane distance, plane depth, local-surface penetration, target-surface penetration, and removed-depth separate. `min_distance_to_dig_area_m` is the minimum distance from the bucket measurement volume to the manually assigned DigArea Box lower-face reference-plane rectangle, not to the full 3D box volume. DigArea local bucket pose uses the lowest sampled bucket measurement-volume point in DigArea local `y`, not the volume center; shovel cutting-edge/tooth samples and the target-distance proxy are no longer part of the DigArea telemetry path. `bucket_depth_below_dig_area_plane_m` is the maximum centered bucket measurement-volume extension below the manually assigned DigArea Box lower face; it is not clipped by terrain surface height. `bucket_depth_below_local_surface_m` compares the same bucket measurement volume corners against the measured current DigTerrain surface at those corner `x/z` positions. `bucket_depth_below_target_surface_m` compares those corners against `DigAreaBoxLowerFaceY - target_depth_m`. `dig_area_removed_depth_m_*` remains the 3x2 terrain-surface depth delta from the reset baseline. `bucket_dig_area_penetration_contact_mask` is driven by local terrain-surface penetration when available, with a conservative distance/depth fallback only when surface truth is unavailable. Runtime auto-alignment and name-based rebinding of the DigArea Box have been removed; the manually assigned and placed Box is now the reference footprint and plane.
- DigArea stability/debug: the DigArea object is no longer parented under `DigTerrain`, and its legacy AGX `RigidBody` plus assigned `AGXUnity.Collide.Box` component are disabled as native AGX participants so native rigid-body/geometry synchronization cannot bind the manually placed reference box to another Play Mode pose. `DigAreaMeasurement` repeats this detach step during reference resolution and treats the assigned Box as Unity-side geometry only. The runtime HUD can show the full 64D `STEP_RESP.env_state` payload plus DigArea root/Box/native pose diagnostics for manual telemetry checks.
- V2.2 data contract: the step-ack server now reports a 64D add-only `env_state`; old 0-27 indices are unchanged, and new 28-63 indices include bucket tip local pose, 3x2 surface/removed/target depth grids, valid masks, bucket mass delta, dump/offtarget deposition fields, contact masks, and collision count.
- Replay determinism diagnostics: reset now reports seed/status/terrain reset
  details through `reset_diagnostic:*` response warnings. Step and diagnostic
  realign responses can report bucket-vs-external-shape contact count and peak
  normal force through `bucket_contact_diagnostic:*` warnings, plus
  `bucket_mass_diagnostic:*` warnings that split bucket load into reported
  mass, raw mass, AGX terrain dynamic mass, handled-as-particle rigid-body mass,
  and live terrain soil particle count. Diagnostic `REALIGN_POSE` requests can
  realign the four actuator positions to a requested normalized qpos through
  `SceneResetService`; any run using realign remains diagnostic-only and cannot
  enter strict gold replay or calibrated label truth. These diagnostics do not
  extend `env_state`.
- Dynamic soil reset: ordinary `AGXUnity.Model.DeformableTerrain` instances are
  cleared through native `Terrain.clearAllSoilParticles()` before and after
  terrain recreation; non-standard terrain providers keep the older
  per-particle fallback.
- Soil determinism status: `RESET.seed` is applied to Unity's managed random
  source, but no AGX deformable-terrain soil/native seed API is confirmed in
  this branch. The reset diagnostic therefore reports
  `soil_seed_status=not_supported` rather than claiming strict soil
  determinism.
- Editor automation: `CodexPlayModeBootstrap` can be triggered from Unity `-executeMethod` or the `Temp/CodexPlayModeBootstrap.request` file. It saves open scenes, exits Play Mode, waits for compilation/domain reload, opens the YuLong main scene, re-enters Play Mode, and writes status once `AgxSimStepAckServer` is listening.
- Editor pose snapshots: `CodexExcavatorPoseSnapshotUtility` and
  `CodexExcavatorPoseBakeUtility` now resolve the active YuLong rig first
  through `ExcavatorYuLong` / `remake3`, then fall back to the legacy
  `Excavator_BobcatE85` root. A successful Play Mode capture also writes
  `Temp/CodexExcavatorPoseSnapshot/playmode_target_pose.json`, which is the
  default input for `Bake Excavator Pose Snapshot To Scene`. YuLong reset-pose
  capture/bake should therefore be done with a fresh snapshot after this change;
  older Bobcat-root snapshots should not be reused for YuLong reset calibration.
  The bake step also derives a normalized reset qpos from YuLong
  `joint1..joint4.current_angle` using `YuLong_norm.json` and stores it on
  `AgxSimStepAckServer`. Backend resets now restore the baked scene pose and
  then realign the native joint state to that qpos with a short burn-in and
  final velocity clear, so ACT `qpos/qvel` observations match the visual reset
  pose instead of falling back to the old zero-angle posture.
- Current reset baseline: the main scene is pinned to the previous
  bootstrap-rollout start qpos `[0.5, 0.42134494, 0.6205778, 0.5007238]`.
  The `remake3` root transform matches the May 22 scene baseline, while child
  link transforms are left on the prefab defaults instead of serialized
  capture/bake pose overrides.
- Terrain setup: measured terrain builders and repair/audit utilities derive Dig/Dump terrain XZ from the current `Dig_Footprint` / `Dump_Footprint` minus measured board thickness. The current measured inner-board terrain size is `2.4m x 2.9m` for both DigTerrain and DumpTerrainReceiver.
- Validation note: `dotnet build AGXUnityE85ExcavatorSim.sln --no-restore` passes after restore. Play Mode motion/QC still needs an operator run in Unity to validate the mechanical joint origins/axes under load.
- Scope reminder: current phase remains fixed-station 4D arm control only, with no track/drive/steer integration.
