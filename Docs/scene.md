# AGXUnity Excavator Task Scene - Current V0 Reference

**Status:** current English source of truth for the Unity/AGX side  
**Last updated:** 2026-03-25  
**Companion translation:** `Docs/scene.zh-CN.md` is a reading-only mirror; if the two files ever diverge, this English file wins.

This document is no longer an implementation plan. It describes the scene and task contract that are currently implemented across the Unity repo and the linked Python testbed workflow.

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
> cycles, and leave enough material stably retained inside the currently active dump target.

The active target can currently be:

- `ContainerBox`
- `TruckBed`

This task definition is target-centric, not bucket-centric. Bucket mass is still exported and still useful for analysis, but the current mission is defined by retained mass in the selected target.

## 3. What Is Implemented

### 3.1 Scene and Targets

The current main scene provides:

- fixed excavator pose
- fixed soil pile / dig zone
- a scene `DigArea` guide rendered as a transparent fill with a colored contour
- FPV camera for step-ack export
- a static rigid `ContainerBox` target
- a `BedTruck` target with runtime target switching

Runtime target routing is implemented, so the same exported field names continue to refer to the **currently active target**.
The runtime HUD also exposes DigArea good-start state, DigArea touch state, and
bucket depth below the DigArea plane for quick operator validation.

### 3.2 Target Mass Measurement

The current Unity implementation already supports:

- target mass measurement inside the active target measurement volume
- reset-relative net deposited mass
- runtime switching between `ContainerBox` and `TruckBed`
- aggregation across all active `DeformableTerrainBase` instances
- inclusion of `HandleAsParticle` dynamic rigid bodies such as `Dynamic Rock`

Truck-specific handling is also implemented:

- the truck bed `MovableTerrain` helper object is disabled before AGX initialization so dumped soil stays as dynamic particles
- existing truck bed support `Box` collisions are re-enabled
- the truck measurement volume is derived from truck bed support `Box` geometry plus configurable top headroom

### 3.3 Distance Export

The current V0 contract now exports:

- `min_distance_to_target_m`

This is the approximate minimum distance between:

- the current bucket target-distance proxy volume
- the currently active target distance geometry

Current behavior:

- it is distance-based, not collision-based
- the current scene defaults to a dedicated, editor-configurable bucket proxy
  volume exposed on `ExcavationMassTracker`
- the target side now prefers the active target's hard box shapes and only
  falls back to a target distance volume when those shapes are unavailable
- for `TruckBed`, this means the distance is measured against truck hard-body
  box geometry rather than the bed mass-measurement headroom volume
- for `TruckBed`, helper `*FailureVolume` shapes such as the dump/top failure
  volumes are excluded from both distance geometry and hard-collision shape
  filtering
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
- when the active target is `TruckBed`, the hard-surface shape set covers the full `BedTruck` collision body, not only the bed/trunk measurement region
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
- truck rigid-body / constraint state
- terrain state
- target mass counters
- bucket / target measurement baselines

The current reset goal is stable baseline reproducibility, not strict seeded determinism.

### 3.6 Step-Ack Bridge

The current Unity bridge already supports:

- manual stepping via `DoStep()`
- binary framed TCP step-ack transport
- FPV raw RGB export
- 4D `qpos`
- 4D `qvel`
- 9D `env_state`

## 4. Current Export Contract

The current exported observation is:

- `images["fpv"]`
- `qpos`
- `qvel`
- `env_state`

Current `env_state` order:

`[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m]`

Field semantics:

- `mass_in_bucket_kg`: current bucket-contained dynamic material estimate
- `excavated_mass_kg`: current excavation progress signal from the bucket-side tracker
- `mass_in_target_box_kg`: current mass retained in the active dump target
- `deposited_mass_in_target_box_kg`: reset-relative net retained mass in the active dump target
- `min_distance_to_target_m`: approximate minimum bucket-proxy-to-active-target distance
- `target_hard_collision_count`: cumulative episode count of monitored excavator-vs-active-target hard collisions
- `target_contact_max_normal_force_n`: per-step maximum monitored excavator-vs-active-target solved normal force in Newtons
- `min_distance_to_dig_area_m`: approximate minimum bucket-measurement-volume distance to the scene `DigArea`
- `bucket_depth_below_dig_area_plane_m`: `max(0, dig_plane_y - bucket_world_min_y)` for the current bucket measurement volume

The target-distance field now prefers the dedicated bucket target-distance
proxy volume configured on `ExcavationMassTracker`, and compares it against the
active target's distance geometry. The DigArea fields continue to use the
bucket measurement volume that `ExcavationMassTracker` uses for bucket-mass
estimation.

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
   Signals: `mass_in_bucket_kg`, `min_distance_to_target_m`
3. `depositing`
   Retained mass in the active target starts increasing.
   Signals: `mass_in_target_box_kg`, `deposited_mass_in_target_box_kg`
4. `retained_success`
   Net retained mass in the active target stays above the configured success
   threshold long enough to count as task success.
   Signal: `deposited_mass_in_target_box_kg`

Current reward range:

- `0.0` idle / no meaningful progress yet
- `0.0 - 1.0` loading progress
- `1.0 - 2.0` loaded and moving toward the target
- `2.0 - 3.0` depositing into the target
- `4.0` retained success held

The tracker also emits optional per-step success/fail logs such as
`good_dig_start`, `load_progress`, `approach_progress`,
`deposit_progress`, `load_outside_dig_area`, `spill_before_target`,
`unsafe_target_distance`, and `hard_target_collision` for debugging. These
logs are testbed-side diagnostics; they are not part of the Unity wire
protocol.

Current testbed penalty behavior:

- if cumulative `target_hard_collision_count` increases for a step, the testbed applies one fixed `hard_collision_penalty = 0.75`
- this penalty does not change the success rule
- Unity `STEP_RESP.reward` still mirrors retained target mass only; the collision penalty stays testbed-side

## 6. Operational Flow

The intended episode flow is now:

1. reset the scene
2. confirm or set the active dump target
3. scoop material from the soil pile
   The intended good start is now: bucket measurement volume touches the
   `DigArea` region and digs below the DigArea plane while load increases.
4. transport the load toward the selected target
5. dump material into the target
6. wait for settling / retained-mass confirmation
7. either terminate on success or continue with another scoop cycle

The task does **not** require Unity to export explicit stage IDs. Stage interpretation should be inferred from:

- `mass_in_bucket_kg`
- `mass_in_target_box_kg`
- `deposited_mass_in_target_box_kg`
- `min_distance_to_target_m`
- arm pose and FPV image

## 7. What Has Been Finished

The following items that used to be planned are now complete enough to be treated as current scene behavior:

- fixed V0 scene layout
- binary step-ack export
- FPV export
- target mass export
- reset-relative deposited-mass export
- truck target integration
- runtime target switching
- truck-inclusive reset
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
