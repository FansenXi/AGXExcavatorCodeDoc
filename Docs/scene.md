# AGXUnity Excavator Task Scenario — V0 Design Spec
**Goal:** Design and implement a *reproducible, measurable* excavation task in AGXUnity that plugs into our Python testbed pipeline:
> teleop expert demos → HDF5 dataset → train (ACT) → eval (success_rate) → weekly videos

**Current status:**  
- ✅ Joystick teleop excavator control works in AGXUnity  
- ✅ Testbed pipeline (record/train/eval/video) already smoke-tested  
- ✅ AGXUnity bridge supports manual stepping (`DoStep`) and binary step-ack

---

## 0) Why this V0 scenario?
We need a scenario that is:
1) **Easy to implement** in AGXUnity (low art/asset dependency)
2) **Automatically measurable** (no manual labeling)
3) **Reproducible** (stable reset)
4) **Supports weekly iteration** (success_rate increases + better videos)

We choose **Level-2 style “Rigid container truck-box”** as V0:
- Dig from a soil pile
- Dump into a rigid open-top target (static / infinite mass)
- Measure “how much material ends in the currently selected dump target” + basic collision safety

> Note: container mass measurement is now implemented in Unity.
> The remaining uncertain capability for V0 is collision/contact export.

---

## 1) V0 Scenario Overview
### 1.1 Layout (fixed for V0)
- **Excavator start pose:** fixed (no randomization in V0)
- **Soil pile (Dig Zone):** fixed location, fixed pile shape
- **Rigid dump target:** fixed location + fixed height (open top)
  - current scene supports both `ContainerBox` and `BedTruck` bed as selectable targets
- **Safety region:** keep-out area around container (for collision metrics)

### 1.2 Phases (informal; V0 does NOT require explicit phase router)
- DIG/SCOOP: get material into bucket
- SWING/LIFT: move toward container
- DUMP: dump into container

V0 evaluation can be **phase-agnostic**; we only need success metrics.

---

## 2) Success Metric (V0)
Primary metric: **success_rate**.

### 2.1 Preferred success definition (container-based)
Success if:
- `mass_in_container >= M_thresh` for `hold_steps` consecutive steps  
Default:
- `hold_steps = 25`  (0.5s at 50Hz)
- `M_thresh` chosen after we inspect typical values (initially set from a small pilot run)

Implementation note:
- Unity now routes `mass_in_container` / `mass_in_target_box` through the active dump target selector
- For the current main scene, the selectable targets are `ContainerBox` and `TruckBed`

### 2.2 Fallback success definition (bucket-based)
If container mass measurement is not available, we use:
Success if:
- `mass_in_bucket >= M_bucket_thresh` for `hold_steps` steps  
This validates “digging” behavior first, then we upgrade to container-based success once available.

---

## 3) Required Outputs (AGXUnity → Python Testbed)
We standardize obs into:
- `images["fpv"]`: first-person RGB
- `qpos`: swing/boom/stick/bucket normalized position (len=4)
- `qvel`: swing/boom/stick/bucket speed (len=4)
- `env_state`: extra metrics used for success + debugging

### 3.1 Minimum env_state fields for V0
We will export at least:
- `mass_in_bucket` (float)
- `excavated_mass` (float)
- `mass_in_container` (float)
- `deposited_mass_in_container` (float, net deposited mass since latest reset)
- (optional) collision/contact counters:
  - `collisions_with_container`
  - `collisions_total`

If collision export is not available, we will approximate collision risk via distance checks (see §6).

---

## 4) Reset & Reproducibility (V0)
We do NOT require perfect seeded determinism in V0, but we MUST have a stable baseline reset:
- Excavator pose reset
- Truck pose / bed constraint reset
- Terrain reset to baseline pile state
- Bucket emptied / counters reset
- Container reset (static)

**Metadata required in every episode:**
- scenario_id = "agx_excavator_container_v0"
- control_hz = 50, dt = 0.02
- action_semantics = "actuator_speed_cmd"
- camera resolution + image format

---

## 5) Implementation Checklist — Unity/AGXUnity Side (Repo: agxunity-sim)
### 5.1 Scene objects
Create / configure:
- `SoilPile` (dig zone): terrain + initial pile
- `ContainerBox` (dump target option A):
  - open-top rigid collider
  - set to static/kinematic/infinite mass (must not move when hit)
  - define inner volume bounds (for “inside container” test)
- `BedTruck` (dump target option B):
  - use truck `Bed` / trunk volume as the measurement region
  - keep the truck rigid/static for V0 unless truck dynamics are explicitly needed
- `KeepOutZone` around container (optional trigger collider)

### 5.2 FPV camera
- Attach camera to driver position (cab view)
- Output RenderTexture
- Export to bytes (V0 raw RGB)

### 5.3 Measurement instrumentation

**A) Container mass measurement (preferred)**
Goal: compute `mass_in_container` per step.
Current Unity implementation:
- count deformable-terrain particles whose position falls inside the active dump-target measurement volume
- aggregate across all active `DeformableTerrainBase` instances, including `MovableTerrain`, so particles are still counted if AGX splits/supports them onto a secondary terrain (for example the truck bed terrain)
- also include `HandleAsParticle` dynamic rigid bodies such as `Dynamic Rock`
- expose both current target mass and reset-relative net deposited target mass
- allow runtime switching between target volumes without changing protocol field names
- for the truck target specifically, disable the bed `MovableTerrain` object before AGX initialization so dumped soil remains dynamic particles; re-enable the existing bed support `Box` collisions; and derive the measurement bounds from the bed collision `Box` geometry plus top headroom so piled material above the floor is still counted

**B) Collision/contact events**
Goal: export contact counts or impulses per step.
If AGX provides contact callbacks:
- count contacts between excavator links and container colliders

### 5.4 Bridge output (step-ack)
Each `STEP_RESP(step_id)` returns:
- qpos (4), qvel (4)
- fpv RGB bytes
- env_state:
  - `mass_in_bucket`
  - `excavated_mass`
  - `mass_in_container`
  - `deposited_mass_in_container`
  - optional collisions later

---

## 6) Implementation Checklist — Python/Testbed Side (Repo: excavator_testbed)
### 6.1 Backend
- `AGXSimBackend` uses step-ack socket calls:
  - STEP_REQ(step_id, action)
  - STEP_RESP(step_id, obs)
- Assemble `raw_obs = {qpos, qvel, images, env_state}`

### 6.2 StateConverter
- Map raw_obs → StructuredState
- Ensure ordering matches protocol constants

### 6.3 Recorder / HDF5 schema v1.1
Record:
- images/fpv (raw RGB)
- qpos/qvel
- action (4D)
- env_state
- timestamps/step_id
- metadata (scenario_id, control_hz, dt, action_semantics)

### 6.4 Evaluator (V0)
Compute:
- success_rate using:
  - container-based success if env_state has mass_in_container
  - else bucket-based success (fallback)
Also output:
- metrics.json
- summary.csv
- videos/

### 6.5 Optional collision risk fallback
If collision events are missing, approximate risk:
- use min distance between bucket tip (or key points) and container
- define `min_distance_to_container` and threshold for “near collision”
(This requires Unity to export container pose or key points, OR Python to know fixed container pose in scenario config.)

---

## 7) Verification Plan (What we must test first)
Before full implementation, run these short tests:

### Test A — Can we compute container mass?
1) Dump soil into container via manual teleop
2) Observe `mass_in_container` and `deposited_mass_in_container` time series
3) Print time series (per step) and validate it tracks retained material in the target after dumping

Pass criteria:
- metric exists and drops back down if material leaves the target volume
- stable enough to threshold

### Test B — Can we export contact/collision info?
1) Drive bucket into container wall intentionally
2) Check if AGX contact callbacks or Unity collision events fire
3) Export a counter `collisions_with_container`

Pass criteria:
- collision counter increases when contact occurs

### Decision after tests:
- A is already implemented; tune threshold stability from pilot runs
- If B fails → use distance-based “near collision” metric for V0

---

## 8) V0 Parameter Defaults (To be tuned after pilot)
- control_hz = 50 (dt=0.02)
- hold_steps = 25
- M_thresh (container): TBD after pilot stats
- M_bucket_thresh: TBD after pilot stats
- episode_len = 500 to 1500 steps (10–30s) depending on teleop duration
- fpv resolution: start 640×480 (reduce bandwidth), later increase

---

## 9) Deliverables (What “done” looks like)
### Unity/AGX side
- Scene `agx_excavator_container_v0`
- Exports: fpv RGB, qpos/qvel, env_state (mass_in_bucket + excavated_mass + mass_in_container + deposited_mass_in_container + optional collisions)
- Reset to baseline pose/state
- Main-scene target selection currently supports `ContainerBox` and `TruckBed`

### Python/testbed side
- record_teleop produces HDF5 v1.1 episodes for this scenario
- replay works
- eval produces success_rate + videos
- first ACT training run completes end-to-end

---

## 10) Open Items (Track explicitly)
- [ ] Confirm collision/contact export method
- [ ] Confirm fpv export performance (raw now, H.264 later)
- [ ] Confirm reset baseline consistency

---

## 11) Notes
- V0 goal is **measurable progress**, not perfect realism.
- Start fixed, then add randomization in V1:
  - container pose noise
  - pile pose noise
  - lighting/camera noise
- Keep protocol/schema as single source of truth (sim-protocol repo).
