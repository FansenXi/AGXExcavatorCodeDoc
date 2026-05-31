# AGXUnity Step-Ack Binary Protocol

**Status:** current implementation truth source for Unity side<br>
**Last updated:** 2026-05-23
**Implementation files:**
- `AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimProtocol.cs`
- `AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs`
- `AGXUnity_Excavator_Assets/Scripts/Presentation/TrackedCameraWindow.cs`
- `AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs`
- `AGXUnity_Excavator_Assets/Scripts/Experiment/SwitchableTargetMassSensor.cs`
- `AGXUnity_Excavator_Assets/Scripts/Experiment/TargetMassSensorBase.cs`
- `AGXUnity_Excavator_Assets/Scripts/TerrainParticleBoxMassSensor.cs`

This document describes the protocol that is currently implemented in the Unity repo.
If older draft documents conflict with this file, this file and the code win.

Current Repo B project identity:
- Unity project directory: `/home/pingfan/AGXUnityE85ExcavatorSim`
- `ActHelloPayload.project`: `AGXUnityE85ExcavatorSim`
- historical asset paths still begin with `Assets/AGXUnity_Excavator/...`

## 1. Scope

The protocol is used by the Unity `AgxSimStepAckServer` for:
- `GET_INFO`
- `RESET`
- `STEP`
- `REALIGN_POSE`, a replay-only actuator pose correction used when old source
  data contains stochastic swing jumps that no longer reproduce after the Unity
  physics fix

It is a TCP binary protocol with:
- fixed-size frame header
- binary payloads
- CRC32 over payload bytes
- raw RGB image transport for V0

Current control semantics:
- action semantics: normalized `actuator_speed_cmd` in `[-1, 1]`; Unity's
  `ExcavatorMachineController` maps full command to machine-level per-axis
  max speed and acceleration before writing target speeds, so HUD machine
  response tuning affects manual, ACT, and step-ack control paths consistently
- manual ISO/SAE mapping now only selects axis/sign/dead-zone; speed magnitude
  lives in the machine response limits rather than in the command interpreter
- action order: `[swing_speed_cmd, boom_speed_cmd, stick_speed_cmd, bucket_speed_cmd]`
- the active YuLong controller applies calibrated `YuLong_norm.json` soft
  limits before writing target speeds: at/outside normalized `[0, 1]`, commands
  that move farther out of range are zeroed with an immediate stop, while
  commands that move back into range are allowed
- YuLong captures the swing hinge angle when the command first enters the
  neutral dead zone, then holds that lock target until a deliberate swing
  command releases it. The lock target is not refreshed while already locked,
  so rigid-body contact impulses cannot be promoted into a new swing setpoint
  and cause passive upper-body jumps.
- V0 task scope is fixed-position / stationary digging; drive / steer / track
  motion are intentionally excluded from the current step-ack action space
- current baseline consumes pending step-ack requests on Unity `Update`
- latency / transport experiments belong to dedicated transport branches and are
  outside the baseline protocol described in this document

Current observation semantics:
- qpos order: `[swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]`
- qvel order: `[swing_speed, boom_speed, stick_speed, bucket_speed]`
- env_state order:
  `[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m, target_horizontal_distance_m, bucket_height_above_target_rim_m, bucket_over_target_footprint_mask, dump_clearance_ok_mask, bucket_dump_area_relative_x_m, bucket_dump_area_relative_z_m, bucket_dump_area_footprint_outside_distance_m]`

qpos normalization:
- `ActObservationCollector` loads actuator raw min/max ranges from a JSON normalization profile instead of relying on script defaults
- the active YuLong scene uses `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/YuLong_norm.json`
- the saved Cat365 baseline profile is `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Calibration/CAT365_norm.json`
- the HUD calibration controls can start/stop raw range tracking, reset samples, save the observed range to a named JSON profile, and reload the selected profile
- swing normalization should normally remain `[-pi, pi]`; saved manual calibration profiles keep that default unless explicitly configured otherwise

`mass_in_bucket_kg` semantics:
- this field is the current bucket-contained dynamic material estimate
- Unity sums the AGX shovel dynamic terrain mass and configured `HandleAsParticle` dynamic rigid bodies inside the bucket measurement volume
- bucket mass readings at or below `2.0 kg` are treated as `0.0 kg` to suppress empty-bucket residual noise

`mass_in_target_box_kg` semantics:
- this field always refers to the **currently active Unity dump target**
- the current YuLong scene uses `DumpArea` as the active target
- it is the reset-relative delivered mass credited to the `DumpArea`
  measurement footprint: each AGX terrain particle is counted once, using the
  particle's own mass, when it first enters the DumpArea volume after reset
- the reset logic primes hashes for particles already inside the volume, so
  leftover particles at episode start do not count as new delivered mass
- the ledger deduplicates by global AGX `particle.hash()` and releases hashes
  for particles that have disappeared before the next scan; this avoids
  double-counting when multiple terrain providers expose the same live particle,
  while still allowing later scoops to count if AGX reuses a hash after
  receiver-terrain absorption
- this is intentionally not a live retained-particle snapshot; the current
  receiver terrain can absorb dumped particles immediately, so retained
  snapshots would drop to zero even though material was delivered
- field names stay stable within the current step-ack protocol

`deposited_mass_in_target_box_kg` semantics:
- this field uses the same reset-relative unique particle-entry ledger as
  `mass_in_target_box_kg` in the current YuLong scene
- `HandleAsParticle` rigid bodies, when enabled, are added from their
  reset-relative live mass inside the same measurement volume
- bucket-unload inference and heightmap-density conversion are not part of the
  official mass signal; settled/static terrain mass remains only a diagnostic
  fallback path when the unique-entry ledger is disabled for debugging

`min_distance_to_target_m` semantics:
- this field is the current bucket proxy footprint outside-distance to the active `DumpArea` clearance footprint
- it is the scalar footprint-distance counterpart of `bucket_dump_area_footprint_outside_distance_m`; `0.0` means the bucket proxy footprint overlaps the DumpArea footprint
- the current scene exposes the bucket proxy volume on `ExcavationMassTracker` for direct editor tuning
- the target side uses the active target clearance footprint, not hard collision boxes or legacy truck-bed geometry
- the current implementation is footprint-distance based and does not require collision/contact export
- this field remains at index 4 in the ordered `env_state`
- `-1.0` means the distance could not be evaluated for the current frame

`target_hard_collision_count` semantics:
- this field is the cumulative episode count of hard-collision occurrences against the currently active target
- Unity increments it only when a new continuous excavator-vs-target contact session reaches the hard-collision threshold
- while the excavator remains in continuous contact with the target, the count does not continue increasing every frame
- after the excavator leaves the target, the next qualifying touch can increment the count again
- the current Unity scene default threshold is `hard_collision_normal_force_thresh_n = 5000.0`
- source shapes are the enabled AGX `Collide.Shape` components under the excavator root, which covers bucket / arm / chassis
- target shapes come from the currently active target sensor hard-surface shape set
- in the YuLong scene this hard-surface set represents the active `DumpArea`

`target_contact_max_normal_force_n` semantics:
- this field is the maximum solved normal-force magnitude observed during the just-completed simulation step across all monitored excavator-vs-active-target contacts
- `0.0` means no monitored active-target contact was observed for that step

`min_distance_to_dig_area_m` semantics:
- this field is the minimum geometric distance between the current bucket
  measurement volume and the calibrated `DigArea` lower-face reference-plane
  rectangle, not the full 3D box volume
- in the YuLong scene, Unity uses only the bucket measurement volume configured
  by `ExcavationMassTracker`; it does not mix in the target-distance proxy or
  `DeformableTerrainShovel` cutting-edge / tooth-direction samples
- `DigAreaMeasurement` treats the scene-assigned DigArea Box reference as the
  source of truth for the measurement footprint and plane
- runtime auto-alignment of the DigArea Box has been removed. The manually
  placed DigArea Box is the reference frame; consumers should treat its plane
  and the measured terrain surface as separate signals rather than assuming the
  plane is always the live soil surface
- runtime name-based DigArea Box rebinding has also been removed. If the scene
  reference is missing, DigArea telemetry is unavailable instead of silently
  selecting another box
- the DigArea object is not parented under DigTerrain. Its legacy AGX
  `RigidBody` component is disabled in the scene and `DigAreaMeasurement`
  disables it again during reference resolution. The assigned
  `AGXUnity.Collide.Box` component is also disabled as an AGX native shape while
  kept as a Unity-side geometry/half-extents source. This prevents AGX native
  rigid-body or geometry synchronization from overriding manual Play Mode edits
  of the calibrated DigArea transform.
- `0.0` means the bucket measurement volume touches or intersects the DigArea
  lower-face reference-plane rectangle
- `-1.0` means the distance could not be evaluated for the current frame

V2.2 `env_state` contract:
- the old 28 fields stay at indices `0..27` unchanged
- indices `28..63` are appended for YuLong data collection and bring the ordered
  state length to `64`
- appended fields cover bucket tip local `x/y/z`, depth below local/target surface,
  3x2 DigArea `surface_depth`, `removed_depth`, `target_depth`, `valid_mask`,
  `bucket_mass_delta_kg`, mirrored dump-area deposited mass,
  `offtarget_deposited_mass_kg`, geometry/contact masks, and
  `hard_collision_count`
- `offtarget_deposited_mass_kg = -1.0` means the active scene has no reliable
  off-target deposited-mass sensor; consumers must treat it as unavailable rather
  than as zero
- DigArea grid depths are meters below the manually placed DigArea Box lower
  face, positive downward; row-major order is
  `r0c0, r0c1, r1c0, r1c1, r2c0, r2c1`
- YuLong DigArea grid depths use the same calibrated DigArea Box plane as
  `bucket_depth_below_dig_area_plane_m`: Unity samples terrain-surface points
  in each 3x2 cell, transforms those points into the DigArea Box local frame,
  and reports positive downward distance from the box lower face. The preferred
  surface source is a downward physics raycast onto the DigTerrain collider;
  live AGX `DeformableTerrainBase` native height and then Unity `TerrainData`
  are compatibility fallbacks. Unity averages multiple points per cell and
  computes `removed_depth = max(0, current_surface_depth - reset_baseline)`.
  An opt-in mass-attributed coverage fallback exists for diagnostics, but it is
  disabled by default because it is a planner proxy rather than a pure
  geometric soil-surface measurement.

`bucket_depth_below_dig_area_plane_m` semantics:
- this field is geometric depth below the manually placed DigArea Box plane,
  positive downward. It is a plane measurement, not a terrain-surface/contact
  measurement.
- Unity computes it from the centered bucket measurement volume: it transforms
  the volume corners into the DigArea Box frame and reports the farthest amount
  that the bucket volume extends below the DigArea Box lower face. For the thin
  manually placed DigArea Box, that lower face is the operator-controlled
  reference plane.
- the value is not clipped by current terrain height and is not forced to zero
  merely because the measured point is outside the DigArea footprint. Consumers
  should combine plane depth with `bucket_tip_dig_area_x/z`,
  `min_distance_to_dig_area_m`, and
  `bucket_dig_area_penetration_contact_mask` when they need in-footprint soil contact.

`bucket_depth_below_local_surface_m` semantics:
- this field is the current bucket penetration below the measured local terrain
  surface, positive downward. Unity computes it from the centered bucket
  measurement volume corners, samples the DigTerrain surface at each corner's
  DigArea-local `x/z`, and reports the maximum
  `surface_local_y - bucket_corner_local_y`.
- it is a terrain-surface/contact signal, not a fixed-plane signal. If the soil
  has already been excavated lower in that local region, this value becomes
  shallower for the same absolute bucket pose.

`bucket_depth_below_target_surface_m` semantics:
- this field uses the same centered bucket measurement volume corners, but
  compares them to the target cut surface at
  `DigAreaBoxLowerFaceY - target_depth_m`
- it reports how far the bucket geometry has gone below the desired target
  depth surface, independent of the currently measured terrain height

`bucket_dig_area_penetration_contact_mask` semantics:
- this field is a working-edge soil-contact approximation, not a pure DigArea
  plane-crossing flag
- when local terrain-surface depth is available, Unity sets this mask only from
  `bucket_depth_below_local_surface_m > 0.005m`
- if local surface depth cannot be measured, Unity falls back to a conservative
  DigArea proximity check: near the DigArea footprint and below the plane by
  more than `0.005m`
- `bucket_depth_below_dig_area_plane_m > 0` alone is intentionally insufficient
  to claim contact

`target_horizontal_distance_m` semantics:
- explicit horizontal planar distance between the bucket target-distance proxy footprint and the active dump-area clearance footprint
- `0.0` means the footprints overlap
- `-1.0` means the explicit dump-area geometry could not be evaluated

`bucket_height_above_target_rim_m` semantics:
- bucket proxy bottom height relative to the active dump-area clearance volume top/rim
- positive values mean the proxy bottom is above the dump-area rim/top
- negative values mean the proxy bottom is below it

`bucket_over_target_footprint_mask` semantics:
- float mask, encoded as `0.0` or `1.0`
- `1.0` means the bucket target-distance proxy footprint overlaps the active dump-area footprint

`dump_clearance_ok_mask` semantics:
- float mask, encoded as `0.0` or `1.0`
- `1.0` means the bucket is within the active dump area's horizontal clearance tolerance and `bucket_height_above_target_rim_m >= 0.0`

`bucket_dump_area_relative_x_m` / `bucket_dump_area_relative_z_m` semantics:
- bucket proxy center in the active dump area's local horizontal frame
- these signed fields are used when a planner needs a corridor, not just unsigned footprint proximity

`bucket_dump_area_footprint_outside_distance_m` semantics:
- unsigned horizontal distance from the bucket proxy footprint to the active dump-area footprint
- `0.0` means the footprint overlaps or is inside the active dump-area footprint

## 2. Byte Order and Primitive Encoding

All numeric values use .NET `BinaryWriter` / `BinaryReader` encoding:
- little-endian integers
- little-endian IEEE754 `float32`

Primitive encodings:
- `bool` -> `uint8` (`0` or `1`)
- `string` -> `int32 byte_len` + UTF-8 bytes
- `float[]` -> `int32 len` + `len * float32`
- `string[]` -> `int32 len` + repeated encoded strings
- `bytes` -> `int32 byte_len` + raw bytes

## 3. Frame Header

Every TCP message is:
- `header[16 bytes]`
- `payload[payload_len bytes]`

Header layout:

| Field | Type | Value / Meaning |
| --- | --- | --- |
| `magic` | `uint32` | `0xA6A6A6A6` |
| `version` | `uint16` | `1` |
| `msg_type` | `uint16` | see section 4 |
| `payload_len` | `uint32` | payload byte length |
| `crc32` | `uint32` | CRC32 of payload only |

CRC32 details:
- polynomial: `0xEDB88320`
- initial value: `0xFFFFFFFF`
- final xor: `0xFFFFFFFF`

Unity currently rejects frames if:
- `magic` is wrong
- `version` is not `1`
- `payload_len` is too large
- CRC check fails

## 4. Message Types

| Name | Numeric value |
| --- | --- |
| `GET_INFO_REQ` | `1` |
| `GET_INFO_RESP` | `2` |
| `RESET_REQ` | `3` |
| `RESET_RESP` | `4` |
| `STEP_REQ` | `5` |
| `STEP_RESP` | `6` |
| `REALIGN_POSE_REQ` | `7` |
| `REALIGN_POSE_RESP` | `8` |

## 5. Request Payloads

### 5.1 GET_INFO_REQ

Payload:
- empty payload allowed

### 5.2 RESET_REQ

Binary field order:
1. `seed: int32`
2. `reset_terrain: bool`
3. `reset_pose: bool`
4. `client_time_ns: int64` optional
5. `scenario_id: string` optional

Notes:
- Unity accepts zero-length payload and falls back to defaults.
- Optional trailing fields may be omitted.

### 5.3 STEP_REQ

Binary field order:
1. `step_id: int64`
2. `action: float32[]`
3. `client_time_ns: int64` optional
4. `planner_debug_json: string` optional, only present when field 3 is present

Constraints:
- action length must be at least `4`
- Unity currently consumes the first four action values in this order:
  `[swing, boom, stick, bucket]`
- `planner_debug_json` is a diagnostic-only tail field. Missing, empty, or
  malformed JSON must not change action execution; Unity only uses it for the
  runtime Planner HUD and DigArea corridor visualizer. Current V2.4 payloads
  may include the pre-step bucket center/tip in DigArea-local coordinates; the
  visualizer draws that actual bucket-tip marker separately from the planned
  corridor entry so operators can see execution error at dig handoff.

### 5.4 REALIGN_POSE_REQ

Binary field order:
1. `step_id: int64`
2. `qpos: float32[]`
3. `qvel: float32[]`
4. `burn_in_steps: int32`
5. `client_time_ns: int64`
6. `realign_reason: string`

Constraints and behavior:
- qpos length must be at least `4` in the same order advertised by
  `GET_INFO_RESP.qpos_order`
- Unity currently uses qpos and ignores qvel except for logging/forward
  compatibility
- qpos values are normalized; Unity denormalizes them with the active actuator
  normalization profile, writes each available `LockController.Position`, zeros
  the excavator rigid-body velocities, and then runs up to `100` burn-in
  simulation steps
- clients may keep non-target axes unchanged by sending the current replay qpos
  value for those axes; `tb-replay --realign-axis swing` uses that mode
- this request must not reset terrain, scene pose, DigArea surface baseline, or
  measurement ledgers; it is intended only for replay salvage after a known
  source-data actuator jump

## 6. Common Response Prefix

All response payloads start with:
1. `success: bool`
2. `error: string`

If `success == 0`, the rest of the payload for that response type is still emitted in its normal layout, but only the prefix and warnings should be trusted.

## 7. GET_INFO_RESP Payload

After the common response prefix, fields are written in this order:
1. `protocol_version: string`
2. `dt: float32`
3. `control_hz: float32`
4. `action_semantics: string`
5. `action_order: string[]`
6. `qpos_order: string[]`
7. `qvel_order: string[]`
8. `env_state_order: string[]`
9. `camera_names: string[]`
10. `supports_reset_pose: bool`
11. `supports_images: bool`
12. `cameras: camera_descriptor[]`
13. `warnings: string[]`

`camera_descriptor` field order:
1. `name: string`
2. `width: int32`
3. `height: int32`
4. `fps: float32`
5. `pixel_format: string`
6. `row_order: string`

Current Unity values:
- `protocol_version = "agx-sim/v0"`
- `action_semantics = "actuator_speed_cmd"`
- `camera_names = ["fpv"]` if the FPV camera is configured
- `supports_reset_pose = true`
- `supports_images = true` if the FPV camera is configured
- `pixel_format = "raw_rgb"`
- `row_order = "top_to_bottom"`
- current FPV view pose still comes from `TrackedCameraWindow`, which now supports
  an Inspector-side `m_localRotationOffsetEuler` view-direction trim without
  changing protocol fields

## 8. RESET_RESP Payload

After the common response prefix, fields are written in this order:
1. `reset_applied: bool`
2. `dt: float32`
3. `control_hz: float32`
4. `warnings: string[]`

Current behavior:
- `reset_applied = true` when `reset_terrain || reset_pose`
- when `reset_pose = true` and `reset_terrain = false`, Unity resets pose / counters without forcing a terrain height reset
- when both flags are true, Unity performs the full scene reset path, including dump-area rigid bodies and constraints
- when `reset_terrain = true`, Unity rebuilds the deformable terrain native instance so dynamic soil mass/particles are cleared as part of reset, including particles that were still trapped in the bucket
- for step-ack serving, a successful reset also re-arms the machine controller engine so subsequent `STEP_REQ` actions take effect immediately
- Unity reset path prefers `SceneResetService.ResetScene(resetTerrain, resetPose)` and only falls back to `EpisodeManager.ResetEpisode(...)` for full resets
- when `AgxSimStepAckServer` is configured to disable `EpisodeManager` while serving, the reset path may still arm the manual input-cut state for later hand-back, but the HUD "Release Controls" popup is only shown while `EpisodeManager` itself is enabled
- terrain reset is handled by `ResetTerrain` / `SceneResetService`; the excavation metrics component no longer mutates terrain heights during reset
- baseline step-ack requests are consumed on Unity `Update`
- transport-branch latency experiments may use other scheduling paths, but they
  are outside the baseline payload contract documented here

## 9. STEP_RESP / REALIGN_POSE_RESP Payload

After the common response prefix, both response types write the same observation
layout in this order:
1. `step_id: int64`
2. `qpos: float32[]`
3. `qvel: float32[]`
4. `env_state: float32[]`
5. `image_format: string`
6. `image_w: int32`
7. `image_h: int32`
8. `image_payload: bytes`
9. `reward: float32`
10. `sim_time_ns: int64`
11. `warnings: string[]`

Current Unity values:
- `qpos.len = 4`
- `qvel.len = 4`
- `env_state.len = 64`
- `env_state_order` keeps the original 16 entries unchanged and appends the
  3x2 DigArea Cell Entry fields:
  `bucket_dig_area_cell_in_bounds_mask`, `dig_area_long_axis`,
  `dig_area_grid_long_count`, `dig_area_grid_short_count`,
  `bucket_dig_area_relative_x_m`, `bucket_dig_area_relative_y_m`,
  `bucket_dig_area_relative_z_m`, `bucket_dig_area_long_norm`,
  `bucket_dig_area_short_norm`, `bucket_dig_area_long_index`,
  `bucket_dig_area_short_index`, `bucket_dig_area_cell_id`
- `reward = deposited_mass_in_target_box_kg`
- `image_format = "raw_rgb"` when FPV capture succeeds
- `image_w = 0`, `image_h = 0`, `image_payload = empty` when no FPV frame is available
- FPV capture renders directly from the tracked camera into a `RenderTexture`;
  IMGUI overlays such as `ExperimentHUD` and the camera window chrome are not
  included in `image_payload`

Reward note:
- for the current V0 stationary digging pipeline, `reward` is a Unity-side
  backup success proxy and is not the primary task reward
- Unity currently writes the reset-relative delivered target-mass signal into
  this field:
  `reward = deposited_mass_in_target_box_kg`
- Repo A / the Python testbed currently compute excavation mission reward
  locally from `env_state`
- clients that need the source-of-truth scalar should still prefer
  `env_state[3]` / `deposited_mass_in_target_box_kg`; the `reward` field is a
  convenience mirror of that signal on the wire
- current testbed reward sub-targets are:
  - qualified DigArea good start plus meaningful bucket load acquisition
  - approaching the active target while loaded
  - increasing delivered mass credited to the active target
  - holding delivered target mass above the configured success threshold
- current testbed reward also applies a fixed per-step hard-collision penalty
  when the cumulative `target_hard_collision_count` increases on that step
- current default testbed success signal is
  `deposited_mass_in_target_box_kg >= 100.0 kg` for `25` consecutive steps

Target note:
- `env_state[2]` and `env_state[3]` report the active target selected by Unity runtime target routing
- `env_state[4]` reports the approximate minimum bucket-to-target distance in meters
- `env_state[5]` reports the cumulative episode hard-collision count for monitored excavator-vs-active-target contacts
- `env_state[6]` reports the maximum monitored contact normal force in Newtons for the completed step
- `env_state[7]` reports the minimum distance from the bucket measurement
  volume to the DigArea lower-face reference-plane rectangle in meters
- `env_state[8]` reports the maximum centered bucket measurement-volume extension below the manually placed DigArea Box lower face in meters
- `env_state[9]` reports explicit horizontal bucket-to-dump-area clearance-footprint distance in meters
- `env_state[10]` reports bucket bottom height above the active dump-area rim/top in meters
- `env_state[11]` reports whether the bucket proxy footprint overlaps the active dump-area footprint
- `env_state[12]` reports whether active dump-area clearance is currently satisfied
- `env_state[13]` reports bucket proxy center x in active dump-area local frame
- `env_state[14]` reports bucket proxy center z in active dump-area local frame
- `env_state[15]` reports unsigned distance outside the active dump-area footprint
- `env_state[16]` reports whether the bucket measurement-volume reference point
  is inside the 3x2 Cell Entry grid and geometry is available
- `env_state[17]` reports the DigArea local long axis: `0` for local x, `2` for
  local z
- `env_state[18]` and `env_state[19]` report the fixed Cell Entry grid shape:
  long count `3`, short count `2`
- `env_state[20..22]` report the selected bucket DigArea reference point in
  DigArea local frame: the lowest sampled point of the bucket measurement
  volume in DigArea local `y`, not the volume center.
- `env_state[23]` and `env_state[24]` report signed normalized long/short
  coordinates in `[-1, 1]` when inside the DigArea footprint
- `env_state[25]` and `env_state[26]` report long/short cell indices, or `-1`
  when out of bounds or unavailable
- `env_state[27]` reports `cell_id = long_index * 2 + short_index`, or `-1`
  when out of bounds or unavailable
- `DigAreaMeasurement` no longer creates renderer-backed DigArea fill,
  contour, or cell-grid helpers. The 3x2 Cell Entry grid reported in
  `env_state[16..27]` is computed directly from the assigned DigArea Box
  transform, `HalfExtents`, and fixed `3 x 2` index mapping; it is not read
  from any renderer or scene visual. Legacy runtime children named
  `DigAreaContour*`, `DigAreaCellGridRuntime`, or
  `AGXUnity.Collide.Box_Visual` are removed during reference resolution so they
  cannot mask telemetry/debug mistakes.
- Unity local CSV logs now include `target_name` for debugging
- the binary `STEP_RESP` payload does **not** yet carry `target_name`; clients should treat target identity as scene/runtime configuration for now

Image payload rules:
- layout is row-major
- row order is top-to-bottom
- channel order is RGB
- total byte count should be `image_w * image_h * 3`

## 10. Step-Ack Rules

The required control loop is:
1. Python sends `STEP_REQ(step_id=k, action=..., planner_debug_json=optional)`
2. Unity applies the action
3. Unity performs exactly one logical `DoStep()`
4. Unity samples qpos / qvel / env_state / FPV frame
5. Unity returns `STEP_RESP(step_id=k, ...)`

Hard rules:
- `STEP_RESP.step_id` must equal the request `step_id`
- one `STEP_REQ` must correspond to one exposed simulation step
- image payload must describe the same post-step state as qpos / qvel

Replay realignment rules:
- `REALIGN_POSE_RESP.step_id` must equal the request `step_id`
- `REALIGN_POSE_REQ` is not an action step and should not be counted as a
  policy action in regenerated HDF5 files
- clients that use realignment should record a diagnostic event and keep the
  regenerated observation/action stream self-consistent; post-hoc editing of old
  HDF5 qpos is not a supported recovery path

## 11. Current Implementation Update

Compared with older drafts in this repo, the current Unity implementation has these important updates:
- JSON transport has been removed; transport is now binary framed TCP.
- `qpos` has been expanded from 3D to 4D by adding `swing_position_norm`.
- FPV export now uses raw RGB bytes, not base64-wrapped JSON payloads.
- `GET_INFO_RESP` now advertises camera metadata directly from the running FPV camera.
- `reset_pose` is supported through the current reset path.
- target mass routing can now switch between multiple Unity dump targets while keeping the same env_state layout.
- Unity now exports an approximate distance-to-active-target scalar alongside the existing mass metrics.
- Unity now exports active-target hard-collision summary metrics without changing the meaning of the first five env_state indices.
- Unity now also exports DigArea good-start geometry metrics while keeping the first seven env_state indices stable.
- Unity now exports explicit dump-area geometry metrics while keeping the first nine env_state indices stable.
- `ExperimentHUD` can display the complete 64D `STEP_RESP.env_state` payload in
  wire order so operators can verify Unity-side telemetry before debugging
  Repo A planner/audit consumers.
- `STEP_REQ` accepts an optional trailing `planner_debug_json` string. The
  Unity server caches the latest valid planner debug snapshot, the HUD shows
  mode/cycle/skill/corridor/productivity/stop reason, and a runtime visualizer
  draws the selected `operator_prior_coverage` or `operator_prior_sweep_belief`
  entry point as a thin vertical pointer plus an entry-to-exit direction arrow
  in the DigArea local frame. When Repo A includes current bucket-tip telemetry
  in the same JSON, the visualizer draws it as a magenta cross so a planned
  entry/exit decision can be compared against the actual bucket pose. The server
  now auto-creates the runtime visualizer on itself, so the pointer does not
  depend on an `ExperimentHUD` component being present. When present, pre-dig
  align debug fields are shown in the HUD only; they remain diagnostic and do
  not change Unity control.
- `REALIGN_POSE_REQ` / `REALIGN_POSE_RESP` were added for replay-time salvage of
  old YuLong recordings affected by stochastic swing pose jumps. The endpoint
  uses actuator lock targets plus burn-in and deliberately leaves terrain and
  removed-depth baselines untouched.

## 12. Known Limits

The current Unity implementation still has some limits that clients should know about:
- `protocol_version` string is still `agx-sim/v0`
- the boom position/speed still uses `BoomPrismatics[0]`
- transport has CRC32 and framing; the server now drops stale dead TCP clients, but it is still a single-client sequential protocol
- this document describes Unity-side implementation only; Python client must mirror the same field order exactly
- active target identity is not yet serialized in `GET_INFO_RESP` / `STEP_RESP`; use scene config or Unity-side logs/HUD when switching targets
- Unity does not export a full contact-event stream; only the current active-target hard-collision summary fields are on the wire
