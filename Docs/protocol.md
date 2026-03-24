# AGXUnity Step-Ack Binary Protocol

**Status:** current implementation truth source for Unity side  
**Last updated:** 2026-03-24  
**Implementation files:**
- `AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimProtocol.cs`
- `AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs`
- `AGXUnity_Excavator_Assets/Scripts/Presentation/TrackedCameraWindow.cs`
- `AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs`
- `AGXUnity_Excavator_Assets/Scripts/Experiment/SwitchableTargetMassSensor.cs`
- `AGXUnity_Excavator_Assets/Scripts/Experiment/TruckBedMassSensor.cs`

This document describes the protocol that is currently implemented in the Unity repo.
If older draft documents conflict with this file, this file and the code win.

## 1. Scope

The protocol is used by the Unity `AgxSimStepAckServer` for:
- `GET_INFO`
- `RESET`
- `STEP`

It is a TCP binary protocol with:
- fixed-size frame header
- binary payloads
- CRC32 over payload bytes
- raw RGB image transport for V0

Current control semantics:
- action semantics: `actuator_speed_cmd`
- action order: `[swing_speed_cmd, boom_speed_cmd, stick_speed_cmd, bucket_speed_cmd]`
- V0 task scope is fixed-position / stationary digging; drive / steer / track
  motion are intentionally excluded from the current step-ack action space

Current observation semantics:
- qpos order: `[swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]`
- qvel order: `[swing_speed, boom_speed, stick_speed, bucket_speed]`
- env_state order:
  `[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m]`

`mass_in_target_box_kg` semantics:
- this field always refers to the **currently active Unity dump target**
- the current main scene can switch between `ContainerBox` and `TruckBed`
- field names stay stable for V0 compatibility even when the active target changes

`deposited_mass_in_target_box_kg` semantics:
- this field is the net retained mass inside the active target since the latest reset
- Unity computes it as current measured target mass minus the reset baseline, clamped to zero

`min_distance_to_target_m` semantics:
- this field is the approximate minimum distance between the current bucket body volume and the currently active Unity target measurement volume
- the current implementation is distance-based and does not require collision/contact export
- Unity appends this field after the four existing mass fields to preserve V0 mass index compatibility
- `-1.0` means the distance could not be evaluated for the current frame

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

Constraints:
- action length must be at least `4`
- Unity currently consumes the first four action values in this order:
  `[swing, boom, stick, bucket]`

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

## 8. RESET_RESP Payload

After the common response prefix, fields are written in this order:
1. `reset_applied: bool`
2. `dt: float32`
3. `control_hz: float32`
4. `warnings: string[]`

Current behavior:
- `reset_applied = true` when `reset_terrain || reset_pose`
- when `reset_pose = true` and `reset_terrain = false`, Unity resets pose / counters without forcing a terrain height reset
- when both flags are true, Unity performs the full scene reset path, including truck rigid bodies and truck bed/drivetrain constraints
- when `reset_terrain = true`, Unity rebuilds the deformable terrain native instance so dynamic soil mass/particles are cleared as part of reset, including particles that were still trapped in the bucket
- for step-ack serving, a successful reset also re-arms the machine controller engine so subsequent `STEP_REQ` actions take effect immediately
- Unity reset path prefers `SceneResetService.ResetScene(resetTerrain, resetPose)` and only falls back to `EpisodeManager.ResetEpisode(...)` for full resets
- terrain reset is handled by `ResetTerrain` / `SceneResetService`; the excavation metrics component no longer mutates terrain heights during reset
- pending step-ack requests are consumed on Unity `FixedUpdate`, so external step-ack teleop stays aligned with `Time.fixedDeltaTime` instead of Editor render-frame jitter

## 9. STEP_RESP Payload

After the common response prefix, fields are written in this order:
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
- `env_state.len = 5`
- `reward = 0.0`
- `image_format = "raw_rgb"` when FPV capture succeeds
- `image_w = 0`, `image_h = 0`, `image_payload = empty` when no FPV frame is available

Reward note:
- for the current V0 stationary digging pipeline, `reward` is a placeholder
  transport field and is not the primary task signal
- Repo A / the Python testbed currently compute excavation mission reward
  locally from `env_state`
- current testbed reward sub-targets are:
  - meaningful bucket load acquisition
  - approaching the active target while loaded
  - increasing retained mass inside the active target
  - holding retained target mass above the configured success threshold
- current default testbed success signal is
  `deposited_mass_in_target_box_kg >= 100.0 kg` for `25` consecutive steps

Target note:
- `env_state[2]` and `env_state[3]` report the active target selected by Unity runtime target routing
- `env_state[4]` reports the approximate minimum bucket-to-target distance in meters
- Unity local CSV logs now include `target_name` for debugging
- the binary `STEP_RESP` payload does **not** yet carry `target_name`; clients should treat target identity as scene/runtime configuration for now

Image payload rules:
- layout is row-major
- row order is top-to-bottom
- channel order is RGB
- total byte count should be `image_w * image_h * 3`

## 10. Step-Ack Rules

The required control loop is:
1. Python sends `STEP_REQ(step_id=k, action=...)`
2. Unity applies the action
3. Unity performs exactly one logical `DoStep()`
4. Unity samples qpos / qvel / env_state / FPV frame
5. Unity returns `STEP_RESP(step_id=k, ...)`

Hard rules:
- `STEP_RESP.step_id` must equal the request `step_id`
- one `STEP_REQ` must correspond to one exposed simulation step
- image payload must describe the same post-step state as qpos / qvel

## 11. Current Implementation Update

Compared with older drafts in this repo, the current Unity implementation has these important updates:
- JSON transport has been removed; transport is now binary framed TCP.
- `qpos` has been expanded from 3D to 4D by adding `swing_position_norm`.
- FPV export now uses raw RGB bytes, not base64-wrapped JSON payloads.
- `GET_INFO_RESP` now advertises camera metadata directly from the running FPV camera.
- `reset_pose` is supported through the current reset path.
- target mass routing can now switch between multiple Unity dump targets while keeping the same env_state layout.
- Unity now exports an approximate distance-to-active-target scalar alongside the existing mass metrics.

## 12. Known Limits

The current Unity implementation still has some limits that clients should know about:
- `protocol_version` string is still `agx-sim/v0`
- the boom position/speed still uses `BoomPrismatics[0]`
- transport has CRC32 and framing; the server now drops stale dead TCP clients, but it is still a single-client sequential protocol
- this document describes Unity-side implementation only; Python client must mirror the same field order exactly
- active target identity is not yet serialized in `GET_INFO_RESP` / `STEP_RESP`; use scene config or Unity-side logs/HUD when switching targets
