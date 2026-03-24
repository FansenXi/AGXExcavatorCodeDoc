# AGXUnity Excavator Scene Report - 2026-03-24

**Audience:** Unity / AGX / testbed teammates  
**Base document:** `Docs/scene.md` remains the English source of truth  
**Purpose:** summarize what was completed today and the current working status

This report is a teammate-facing progress note. It does not replace
`Docs/scene.md` or `Docs/protocol.md`. If wording conflicts with those files,
the source-of-truth documents win.

## 1. Executive Summary

Today the scene/task line was moved from an implementation-plan state into a
working V0 reference plus a usable testbed mission.

The main outcome is:

- the Unity scene contract is now documented as a current implemented state
- the Python testbed now uses a target-based excavation mission reward instead
  of Unity's placeholder `reward = 0.0`
- AGX success is now defined by retained mass in the active target, not by
  bucket mass
- the default AGX episode budget was increased from `500` to `1000` steps

## 2. Scene / Protocol Status

The current V0 scene, as reflected in `Docs/scene.md`, now assumes:

- fixed-reset excavator digging
- one fixed soil pile
- one runtime-selected dump target
- 4D arm control only: `swing / boom / stick / bucket`
- FPV export
- target-mass export
- distance export

The current Unity `env_state` contract is:

`[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m]`

Important current meanings:

- `mass_in_target_box_kg` always means the **currently active target**
- `deposited_mass_in_target_box_kg` is reset-relative net retained mass
- `min_distance_to_target_m` is an approximate bucket-to-active-target distance
- Unity `STEP_RESP.reward` is still a placeholder transport field and remains
  `0.0` on the wire

## 3. What Was Completed Today

### 3.1 Scene Documentation Cleanup

`Docs/scene.md` was rewritten as the current English source of truth instead of
an unfinished implementation plan.

Completed items were moved out of plan/checklist language and rewritten as
implemented behavior, including:

- target routing between `ContainerBox` and `TruckBed`
- reset-relative target retained mass export
- distance export
- testbed-side mission/reward integration

A Chinese reading mirror was kept in `Docs/scene.zh-CN.md`, but decision
authority stays with the English `Docs/scene.md`.

### 3.2 AGX Mission Definition in the Testbed

The AGX excavation task in Repo A was redefined as one continuous mission:

`scoop -> transport -> dump -> retain`

The mission is **not** exported as hard stage IDs. Instead, reward is attached
to observable sub-targets inside the same mission:

1. `loading`
2. `approaching_target`
3. `depositing`
4. `retained_success`

Optional per-step logs are now available on the Python side, including:

- `load_progress`
- `approach_progress`
- `deposit_progress`
- `spill_before_target`
- `unsafe_target_distance`

These logs are for debugging and analysis only. They are not part of the Unity
wire protocol.

### 3.3 Success Definition Change

AGX success is no longer defined from bucket mass.

The current default success rule is:

- signal: `deposited_mass_in_target_box_kg`
- threshold: `100.0 kg`
- hold time: `25` control steps

This means success is now target-centric and aligned with the actual mission:
material must be retained in the currently active target.

### 3.4 Episode Budget Increase

The default AGX episode budget was increased from `500` to `1000` steps.

This was applied to:

- teleop default `max_steps`
- AGX eval default `episode_len`
- AGX ACT training configs so training does not truncate the longer recordings

At `50 Hz`, the new default budget is `20 seconds`.

## 4. Validation Run on Fresh Episodes

After the data directory was cleaned and recording restarted from index `0`,
episodes `0` through `4` were checked.

Summary:

| Episode | Steps | Max Reward | Success | Final Deposited Mass (kg) |
| --- | ---: | ---: | ---: | ---: |
| `0` | `1000` | `4.0` | `1` | `962.66` |
| `1` | `1000` | `4.0` | `1` | `2360.98` |
| `2` | `1000` | `4.0` | `1` | `2391.76` |
| `3` | `1000` | `4.0` | `1` | `2055.48` |
| `4` | `1000` | `4.0` | `1` | `1997.02` |

Interpretation:

- the new `1000`-step default is active
- all five checked episodes reached retained-mass success
- final retained target mass is far above the current default threshold

## 5. Verification

The following Python-side checks passed after the mission/reward update:

- `python -m py_compile` on the modified AGX mission/backend/eval files
- `conda run -n aloha python -m unittest tests.test_agx_protocol tests.test_agx_repoa_integration`

Result:

- `11 tests OK`

## 6. Remaining Open Items

The following are still open and were **not** solved by today's work:

- collision/contact export is still not a required V0 signal
- Unity wire `reward` is still a placeholder and is not yet the simulator truth
- reward thresholds still need pilot-data tuning
- exact geometric collision-risk export is still absent; only approximate
  distance is exported

## 7. Suggested Next Steps

Short-term practical next steps:

1. collect more `1000`-step teleop episodes under the new defaults
2. inspect reward curves and retained-mass curves to tune the current
   `100 kg / 25 steps` default
3. decide whether the current testbed-side reward is good enough to keep, or
   whether a future Unity-side reward should be added in parallel for
   comparison

For now, the recommended working rule remains:

- scene/task decisions go into `Docs/scene.md`
- teammate-readable status updates can go into report files like this one
