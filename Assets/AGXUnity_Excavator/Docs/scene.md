# AGXUnity Excavator Task Scene - Current V0 Reference

**Status:** current English source of truth for the Unity/AGX side  
**Last updated:** 2026-05-14
**Companion translation:** `Docs/scene.zh-CN.md` is a reading-only mirror; if the two files ever diverge, this English file wins.

This document is no longer an implementation plan. It describes the scene and task contract that are currently implemented across the Unity repo and the linked Python testbed workflow.

Current Repo B project:

- Unity project directory: `/home/pingfan/AGXUnityE85ExcavatorSim`
- Main machine rig: YuLong (`remake3` scene rig with `ExcavatorYuLong`)
- The historical asset folder and C# namespace still use `AGXUnity_Excavator`
  for compatibility with existing scene references.

If this file conflicts with older drafts, prefer:

1. this file
2. `Docs/protocol.md`
3. the current code and scene assets

## 1. Scope

The current V0 scene is a fixed-reset excavator digging task with:

- one excavator in a fixed initial pose
- one fixed soil pile
- one active dump target selected at runtime
- 4D arm control only: `swing / boom / stick / bucket`
- FPV image export
- mass-based task signals
- distance-based target-approach / near-collision signal
- active-target hard-collision summary export

The current step-ack contract intentionally excludes:

- `drive / steer / track` from the action space
- explicit phase labels
- full collision/contact event export as a required V0 feature

## 2. Task Definition

The current task is:

> In a fixed reset scene, control the excavator using only
> `swing / boom / stick / bucket`
> to execute one or more
> scoop -> transport -> dump -> retain
> cycles, and deliver enough material into the currently active dump target.

The active target is currently:

- `DumpArea`

This task definition is target-centric, not bucket-centric. Bucket mass is still exported and still useful for analysis, but the current mission is defined by delivered mass credited to the selected target.

## 3. What Is Implemented

### 3.1 Scene and Targets

The current main scene provides:

- fixed YuLong excavator pose
- fixed soil pile / dig zone
- a scene `DigArea` guide rendered as a transparent fill with a colored contour
- FPV camera for step-ack export
- a `DumpArea` target backed by `TerrainParticleBoxMassSensor`
- an enabled `AgxSimStepAckServer` configured to listen on TCP port `5057` in Play Mode

The shared control stack now resolves the active YuLong rig as a first-class
machine type. `ExcavatorMachineController` binds the YuLong swing / boom /
stick / bucket semantic constraints, applies the scene-assigned YuLong axis
directions, and the observation/reset/collision/mass paths use the YuLong
machine root and `watou` bucket reference. Because the custom model's native
joint limits are not yet a perfect mechanical bound, the controller also uses
`YuLong_norm.json` as a calibrated soft-limit profile: when an axis is at or
outside normalized `[0, 1]`, velocity commands that would push farther outward
are forced to zero with an immediate stop, while return commands are still
allowed. YuLong also enables a neutral swing lock: when the commanded swing
speed is within the neutral dead zone, the swing hinge is locked at its current
angle so bucket/soil reaction forces do not passively rotate the upper body.

Scene scale:

- the scene environment scale source of truth is
  `CodexSceneScaleConfig.DefaultEnvironmentScale`
- the current default is `1.5`
- the previous `1.25` scale is kept only as `LegacyEnvironmentScale` for
  migration and detection
- use `Tools/AGX Excavator/Codex/Scale Scene Environment To Default 1.5x` to
  rescale an existing scene from the detected current scale to the default
- the same tool can be driven by `Temp/CodexSceneEnvironmentScale.request`; the
  request supports `target_scale`, optional `current_scale`, optional
  `pivot_world`, `apply_environment_physics`, `scale_cameras`, and
  `scale_lights`
- the scale tool scans the loaded scene for spatial scene elements and excludes
  the excavator hierarchy automatically, so newly added non-excavator objects
  are included without extending a hard-coded object-name list
- use `Tools/AGX Excavator/Codex/Audit Scene Environment Scale` after a scale
  migration to compare generated object dimensions, AGX `Box.HalfExtents`, and
  terrain sizes; terrain XZ is checked against the measured Dig/Dump footprint
  inner-board size rather than only against a hard-coded scale constant
- use `Tools/AGX Excavator/Codex/Repair Audited Scene Environment Dimensions`
  or `Temp/CodexSceneScaleRepair.request` only to correct audited generated
  scene-environment dimension drift; it does not move or resize the excavator
- use `Tools/AGX Excavator/Codex/Build Measured Dig And Dump Terrains` or
  `Temp/CodexMeasuredTerrainAreas.request` to rebuild DigTerrain and
  DumpTerrainReceiver from the current `Dig_Footprint` / `Dump_Footprint` and
  measured board thickness. The current YuLong scene measures both terrain XZ
  footprints as `2.4m x 2.9m`, matching the board inner dimensions.
- use `Tools/AGX Excavator/Codex/Capture Excavator Pose Snapshot` or
  `Temp/CodexExcavatorPoseSnapshot.request` after the excavator has been posed
  in Play Mode to export the current excavator root transform, rigid-body
  transforms, constraint frames, controller values, and key component transforms
  for IK/initial-pose baking analysis
- use `Temp/CodexExcavatorActuatedPose.request` with `duration=<seconds>` when
  an automated Play Mode window is needed for live step-ack smoke/replay runs;
  the editor helper keeps Play Mode alive for up to `3600s`, then stops motion
  and exits Play Mode
- use `Tools/AGX Excavator/Codex/Bake Excavator Pose Snapshot To Scene` or
  `Temp/CodexExcavatorPoseBake.request` to apply a captured Play Mode pose
  snapshot back to the Edit Mode scene; the request defaults to
  `Temp/CodexExcavatorPoseSnapshot/playmode_target_pose.json` and writes the
  excavator transform samples, AGX constraint frames, and captured controller
  values into the scene after first backing up the `.unity` file
- use `Tools/AGX Excavator/Codex/Capture Mass Telemetry Snapshot` or
  `Temp/CodexMassTelemetrySnapshot.request` during Play Mode when debugging
  reward/mass telemetry; it exports the current bucket tracker, active target
  router, observation collector values, and target-sensor internal accumulation
  fields to `Temp/CodexMassTelemetrySnapshot/result.json`. For DumpArea mass
  debugging, compare `entered_particle_mass_kg`, `entered_particle_hash_count`,
  `active_particle_hash_count`, `live_terrain_particle_mass_in_box_raw_kg`,
  `live_handled_as_particle_mass_in_box_raw_kg`, and
  `official_target_mass_source`

Runtime target routing is implemented, so the same exported field names continue to refer to the **currently active target**.
The runtime HUD also exposes DigArea good-start state, DigArea touch state, and
bucket depth below the DigArea plane for quick operator validation. The DigArea
runtime visual now includes an orange 3x2 Cell Entry grid child under the
DigArea collide Box. It is aligned from the Box transform and footprint rather
than the deformable terrain height, so digging the terrain lower does not pull
the grid down into the pit. The orange grid has its own visibility toggle, so it
can stay visible even when the broader DigArea fill/contour runtime visual is
disabled. Terrain auto-align for the DigArea collide Box is performed once when
the measurement component initializes only when explicitly enabled; it is off in
the YuLong scene to preserve the calibrated dig-depth plane.
When `AgxSimStepAckServer` is serving and temporarily disables
`EpisodeManager.Update()`, the HUD now falls back to the latest
`ActObservationCollector` task-state sample for live mass, target-distance,
DigArea, and active-target-collision telemetry instead of showing stale
EpisodeManager-side cached values.

### 3.2 Target Mass Measurement

The current Unity implementation already supports:

- target mass measurement inside the active target measurement volume
- reset-relative delivered deposited mass
- a single active `DumpArea` target path
- aggregation across all active `DeformableTerrainBase` instances
- inclusion of `HandleAsParticle` dynamic rigid bodies such as `Dynamic Rock`
- unique entered-particle delivered mass as the official success / QC source;
  bucket-unload inference and heightmap-density conversion remain diagnostic
  only

- the dump area can absorb dumped terrain particles through its receiver terrain, so official target mass is credited by the unique entered-particle ledger instead of by a live-particle snapshot
- existing dump area support `Box` collisions are re-enabled
- the dump-area measurement volume is derived from dump area support `Box` geometry plus configurable top headroom
- editor terrain builders locate the active dump mass sensor by `TargetName == "DumpArea"`, so the current scene object may keep its serialized object name (`SubmergedBox`) while still acting as the `DumpArea` target.

### 3.3 Distance Export

The current V0 contract now exports:

- `min_distance_to_target_m`

This is the horizontal outside-distance between:

- the current bucket target-distance proxy volume
- the currently active `DumpArea` clearance footprint

Current behavior:

- it is footprint-distance based, not collision/contact based
- the current scene defaults to a dedicated, editor-configurable bucket proxy
  volume exposed on `ExcavationMassTracker`
- the target side uses the active target clearance footprint, not hard-body
  collision boxes or the dump-area mass-measurement headroom volume
- `0.0` means the bucket proxy footprint overlaps the `DumpArea` footprint
- if no dedicated proxy configuration is available, Unity falls back to older
  bucket measurement geometry sources
- it is exported alongside mass signals in `env_state`
- it returns `-1.0` when the distance cannot be evaluated

### 3.4 Active-Target Hard Collision Export

The current Unity scene also exports two active-target hard-collision summary
signals:

- `target_hard_collision_count`
- `target_contact_max_normal_force_n`

Current behavior:

- source shapes are the enabled AGX `Collide.Shape` components under the excavator root, covering bucket / arm / chassis
- target shapes come from the currently active target sensor hard-surface shape set
- when the active target is `DumpArea`, the hard-surface shape set covers the `DumpArea` collision body while clearance geometry uses the dump-area footprint
- `target_hard_collision_count` is cumulative within the current episode
- a continuous excavator-vs-target contact session increments `target_hard_collision_count` at most once
- while the excavator remains in contact with the target, the count does not keep rising every frame
- after the excavator leaves the target, the next qualifying touch can increment the count again
- the current scene default is `hard_collision_normal_force_thresh_n = 5000.0`
- `target_contact_max_normal_force_n` records the maximum monitored solved normal-force magnitude from the completed step
- these fields are summary metrics for reward / diagnostics; they do not replace the current mass-based success rule

### 3.5 Reset

The current reset path already restores:

- excavator pose and arm state
- dump-area rigid-body / constraint state
- terrain state
- target mass counters
- bucket / target measurement baselines
- the editor DigTerrain repair path also rebuilds the filled terrain data and
  removes saved `DigArea*Runtime` visual material references before saving the
  scene, so Play Mode-only terrain offsets and transparent overlays do not
  persist into the scene asset
- `DeformableTerrain` uses a `DontSave` TerrainData clone in Play Mode and
  normalizes terrain transforms/references around editor scene saves, including
  reset-triggered terrain height rebuilds, so a Play Mode save cannot persist
  the AGX `MaximumDepth` runtime offset back into `DigTerrain` or
  `DumpTerrainReceiver`

The current reset goal is stable baseline reproducibility, not strict seeded determinism.

### 3.6 Step-Ack Bridge

The current Unity bridge already supports:

- manual stepping via `DoStep()`
- binary framed TCP step-ack transport
- FPV raw RGB export
- 4D `qpos`
- 4D `qvel`
- 16D `env_state`

The step-ack export path already measures DigArea geometry through
`ActObservationCollector`. It does not depend on `EpisodeManager` staying
enabled while the server is listening. `DigAreaMeasurement` uses the calibrated
scene `AGXUnity.RigidBody.DigArea` Box as the measurement footprint and depth
plane. For the YuLong `watou` bucket, the touch/depth path samples the attached
`DeformableTerrainShovel` cutting edge, tooth direction, and top edge first, and
falls back to the older bucket DigArea proxy volume only when shovel geometry is
not available. Runtime grid rendering is visual-only; terrain auto-align is
disabled in the YuLong scene so replay labels keep the same dig-depth plane as
teleop records.

### 3.7 Dual-Path VR Spectator Presentation

The current main scene now also includes a dormant dual-path VR spectator
scaffold that is kept in the repo for future PCVR presentation work.

Current project status:

- the VR spectator code path is intentionally kept in place, but it is **not**
  part of the currently validated Linux desktop workflow
- the current supported day-to-day presentation path remains the normal desktop
  scene rendering path
- the intended future target for this spectator scaffold is Windows desktop
  PCVR with SteamVR acting as the system OpenXR runtime
- when XR startup fails on the current Linux setup, that is treated as a normal
  fallback-to-desktop outcome rather than a blocker for the main excavation
  workflow

Current behavior:

- the existing desktop `Main Camera` remains the only camera responsible for the
  desktop game window
- the desktop view keeps its current `LinkCamera`, HUD, and auxiliary-window
  behavior
- a scene-level `VrSpectatorBootstrap` component on the desktop `Main Camera`
  attempts to start OpenXR at runtime without changing the step-ack / teleop /
  ACT control pipeline
- when XR starts successfully, Unity creates a dedicated runtime `XROrigin` and
  XR-only spectator camera for the HMD
- the XR spectator camera does **not** replace the desktop `Main Camera`
- the XR spectator rig mirrors the desktop `Main Camera` world pose every frame
  through `VrMainCameraMirror`
- HMD head pose still contributes its own local 6DoF tracking on top of that
  mirrored base pose, so the headset gets stereoscopic XR rendering rather than
  a flat monitor-style clone
- when VR is active, the desktop `Main Camera` renders with `Target Eye = None`
  so it stays on the desktop display only, while the XR spectator camera renders
  with `Target Eye = Both` for the HMD
- audio is switched from the desktop `AudioListener` to the XR spectator camera
  while VR is active
- if OpenXR cannot start, the project stays in pure desktop mode and the scene
  continues to render exactly as before

Presentation boundary:

- no VR hand/controller interaction is added
- no VR locomotion is added
- no VR-specific HUD is added
- `TrackedCameraWindow` / FPV capture / `AgxSimStepAckServer` continue to run on
  their existing path and do not become the HMD main view
- the current repo does **not** claim Linux x86_64 HMD availability as a
  validated delivery target for this feature

Scene consistency note:

- the FPV `FollowCamera` object is no longer tagged `MainCamera`
- the desktop `Main Camera` remains the single authoritative `MainCamera` in the
  scene
- the VR spectator scripts are best understood as a future-facing scaffold, not
  a guaranteed cross-platform runtime feature in the current repo state

## 4. Current Export Contract

The current exported observation is:

- `images["fpv"]`
- `qpos`
- `qvel`
- `env_state`

`qpos` normalization is profile-backed. `ActObservationCollector` loads
actuator raw min/max ranges from a JSON profile, with the legacy Cat365
baseline saved as
`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/CAT365_norm.json`.
During Play Mode, the HUD actuator calibration controls can track observed raw
min/max values and save a named profile JSON for the current machine. Swing
uses `[-pi, pi]` by default.

Current `env_state` order:

`[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m, target_horizontal_distance_m, bucket_height_above_target_rim_m, bucket_over_target_footprint_mask, dump_clearance_ok_mask, bucket_dump_area_relative_x_m, bucket_dump_area_relative_z_m, bucket_dump_area_footprint_outside_distance_m]`

Field semantics:

- `mass_in_bucket_kg`: current bucket-contained dynamic material estimate; readings at or below `2.0 kg` are treated as `0.0 kg` to suppress empty-bucket residual noise
- `excavated_mass_kg`: current excavation progress signal from the bucket-side tracker
- `mass_in_target_box_kg`: reset-relative delivered terrain mass credited to the active `DumpArea`; each terrain particle is counted once, using its AGX particle mass, when it first enters the DumpArea measurement volume after reset. The ledger deduplicates by global `particle.hash()` and releases disappeared hashes, so one live particle exposed by multiple terrain providers is not double-counted while later scoops can still count reused hashes
- `deposited_mass_in_target_box_kg`: same unique particle-entry ledger as `mass_in_target_box_kg` in the current YuLong scene; it excludes bucket-unload inference and heightmap-density conversion
- `min_distance_to_target_m`: bucket proxy footprint outside-distance to the active `DumpArea` clearance footprint
- `target_hard_collision_count`: cumulative episode count of monitored excavator-vs-active-target hard collisions
- `target_contact_max_normal_force_n`: per-step maximum monitored excavator-vs-active-target solved normal force in Newtons
- `min_distance_to_dig_area_m`: approximate minimum distance from the YuLong shovel edge samples, or fallback bucket DigArea proxy volume, to the calibrated `DigArea` Box
- `bucket_depth_below_dig_area_plane_m`: maximum YuLong shovel edge depth, or fallback bucket DigArea proxy depth, below the calibrated DigArea Box center plane; the signal becomes positive when the shovel samples go below the DigArea plane inside the footprint

The target-distance field uses the dedicated bucket target-distance proxy volume
configured on `ExcavationMassTracker`, and compares its footprint against the
active `DumpArea` clearance footprint. During step-ack serving, these DigArea
and target metrics continue to update in both the wire payload and the runtime HUD via
`ActObservationCollector`; only the local `EpisodeManager`-side good-dig latch
logic remains paused while that component is disabled.

For precise wire details, use `Docs/protocol.md`.

## 5. Current Success and Reward Semantics in the Testbed

The linked Python testbed is now aligned to the target-based mission.

Current default AGX success rule in the testbed:

- signal: `deposited_mass_in_target_box_kg`
- threshold: `100.0 kg`
- hold time: `25` control steps

These are current defaults, not final tuned values. They are expected to be refined after pilot target-mass runs.

The testbed computes the primary AGX mission reward locally from exported
`env_state`.

Unity now also mirrors the main target-retention success signal into
`STEP_RESP.reward` as a backup transport field:

- `STEP_RESP.reward = deposited_mass_in_target_box_kg`

This Unity-side `reward` is a backup success proxy, not the main shaped mission
reward used by the testbed.

The mission is still treated as one continuous objective. The testbed does not
require Unity to export explicit stage IDs. Reward is attached to observable
sub-targets inside that single mission:

1. `loading`
   The bucket starts gaining meaningful soil mass **after** a qualified DigArea
   good start.
   Signals: `mass_in_bucket_kg`, `excavated_mass_kg`,
   `min_distance_to_dig_area_m`, `bucket_depth_below_dig_area_plane_m`
2. `approaching_target`
   A loaded bucket moves closer to the currently active target.
   Signals: `mass_in_bucket_kg`, `target_horizontal_distance_m`,
   `bucket_dump_area_footprint_outside_distance_m`
3. `depositing`
   Delivered mass credited to the active target starts increasing.
   Signals: `mass_in_target_box_kg`, `deposited_mass_in_target_box_kg`
4. `retained_success`
   Reset-relative delivered mass in the active target stays above the configured success
   threshold long enough to count as task success.
   Signal: `deposited_mass_in_target_box_kg`

Current reward range:

- `0.0` idle / no meaningful progress yet
- `0.0 - 1.0` loading progress
- `1.0 - 2.0` loaded and moving toward the target
- `2.0 - 3.0` depositing into the target
- `4.0` delivered-mass success held

The tracker also emits optional per-step success/fail logs such as
`good_dig_start`, `load_progress`, `approach_progress`,
`deposit_progress`, `load_outside_dig_area`, `spill_before_target`,
`unsafe_target_distance`, and `hard_target_collision` for debugging. These
logs are testbed-side diagnostics; they are not part of the Unity wire
protocol.

Current testbed penalty behavior:

- if cumulative `target_hard_collision_count` increases for a step, the testbed applies one fixed `hard_collision_penalty = 0.75`
- this penalty does not change the success rule
- Unity `STEP_RESP.reward` still mirrors delivered target mass only; the collision penalty stays testbed-side

## 6. Operational Flow

The intended episode flow is now:

1. reset the scene
2. confirm or set the active dump target
3. scoop material from the soil pile
   The intended good start is now: bucket DigArea proxy touches the
   calibrated `DigArea` region and digs below the DigArea plane while load increases.
4. transport the load toward the selected target
5. dump material into the target
6. wait for delivered-mass confirmation
7. either terminate on success or continue with another scoop cycle

The task does **not** require Unity to export explicit stage IDs. Stage interpretation should be inferred from:

- `mass_in_bucket_kg`
- `mass_in_target_box_kg`
- `deposited_mass_in_target_box_kg`
- explicit dump-area geometry, especially `target_horizontal_distance_m`,
  `dump_clearance_ok_mask`, and
  `bucket_dump_area_footprint_outside_distance_m`
- arm pose and FPV image

## 7. What Has Been Finished

The following items that used to be planned are now complete enough to be treated as current scene behavior:

- fixed V0 scene layout
- binary step-ack export
- FPV export
- target mass export
- reset-relative deposited-mass export
- dump-area target integration
- dump-area target routing
- dump-area reset
- distance export
- active-target hard-collision summary export
- testbed-side AGX mission reward
- testbed-side named-signal success configuration

Because these items are implemented, this file no longer keeps the old implementation checklist / validation-plan structure.

## 8. Open Items and Non-Goals

The following are still intentionally open or out of scope for the current V0 contract:

- full collision/contact event export is not part of the current primary contract; only the active-target hard-collision summary metrics are exported
- `drive / steer / track` are not part of the current step-ack action space
- explicit phase labels are not exported
- success threshold tuning still needs pilot-data calibration
- exact geometric collision-risk fields beyond the current distance signal and active-target hard-collision summaries are not exported

## 9. Working Rule for Future Updates

Future scene/task decisions should be written into this English file first.

The Chinese mirror:

- is for reading convenience only
- must stay up to date with this file
- must not become the decision authority if wording diverges
