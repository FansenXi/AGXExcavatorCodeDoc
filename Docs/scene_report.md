# AGXUnity Excavator Scene Report - 2026-03-25

**Base document:** `Docs/scene.md`  
**Purpose:** summarize today's implementation progress, record the current reward and penalty settings, and answer the latest task-design questions.

This report is a teammate-facing progress note. If this file conflicts with the main task specification, `Docs/scene.md` wins.

## 1. Executive Summary

Today the Unity/AGX side and the Python testbed were moved to a more complete
V0 task loop:

- the scene/task contract is now documented as current implemented behavior
- AGX success remains target-centric:
`deposited_mass_in_target_box_kg >= 100.0 kg` for `25` consecutive steps
- Unity now exports active-target hard-collision summary signals
- the scene `DigArea` is now highlighted with a transparent fill and colored contour
- the Unity HUD now shows the DigArea good-start latch and live DigArea geometry checks
- for hard-collision monitoring, it now covers the full `BedTruck`  
hard body
- collision counting is now designed as event-based :  
one continuous contact session counts at most once, and the count can grow  
again only after the excavator leaves the target and touches it again
- Repo A continues to compute the main mission reward locally from `env_state`
while Unity keeps mirroring retained target mass into `STEP_RESP.reward` as a
backup scalar

## 2. What Was Completed Today

### 2.1 Scene / Protocol Side

The current Unity `env_state` contract is now:

`[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m]`

Current meanings:

- `mass_in_target_box_kg` always refers to the currently active target
- `deposited_mass_in_target_box_kg` is reset-relative net retained target mass
- `min_distance_to_target_m` is back on the older approximate bucket-to-target measurement-volume distance path; the attempted hard-surface / full-excavator fix was rolled back because it did not solve the bug and caused frame-rate drop
- `target_hard_collision_count` is the cumulative hard-collision event count within the current episode
- `target_contact_max_normal_force_n` is the current-step maximum monitored normal force
- `min_distance_to_dig_area_m` is the approximate minimum bucket-to-DigArea distance
- `bucket_depth_below_dig_area_plane_m` is the current bucket depth below the DigArea plane

### 2.2 Hard-Collision Monitoring Upgrade

The hard-collision path was extended in two important ways.

First, target coverage is broader:

- `ContainerBox` monitors its own hard body
- `BedTruck` now monitors the full `BedTruck` hard body

Second, counting semantics were fixed:

- the current behavior counts collision **events**
- one continuous excavator-vs-target contact session increments the cumulative
count at most once
- the cumulative count can increase again only after the excavator leaves the
target and later touches it again

### 2.3 Testbed Mission / Reward Integration

Repo A continues to use one continuous mission:

`scoop -> transport -> dump -> retain`

Reward is still shaped from observable sub-targets rather than hard stage IDs:

1. `loading` after a qualified DigArea good start
2. `approaching_target`
3. `depositing`
4. `retained_success`

Optional step logs currently include:

- `good_dig_start`
- `load_progress`
- `approach_progress`
- `deposit_progress`
- `load_outside_dig_area`
- `spill_before_target`
- `unsafe_target_distance`
- `hard_target_collision`

These are testbed-side diagnostics, not Unity wire fields.

### 2.4 Episode Budget

The default AGX episode budget remains:

- `1000` steps
- `50 Hz`
- `20` seconds maximum per episode

This is already reflected in teleop/eval defaults.

### 2.5 Runtime Visual / HUD Feedback

The current Unity scene now also provides direct operator-facing feedback for
DigArea alignment:

- the existing scene `DigArea` thin box is reused as a semi-transparent fill
- a colored contour is drawn around the DigArea footprint at runtime
- `ExperimentHUD` now shows whether good dig start has latched
- the HUD also shows live DigArea touch status and the current bucket depth
below the DigArea plane

## 3. Current Reward And Penalty Settings

## 3.1 Success Rule

Current default success rule:

- signal: `deposited_mass_in_target_box_kg`
- threshold: `100.0 kg`
- hold window: `25` control steps
- episode limit: `1000` steps

This means the task is currently evaluated by **retained mass in the active
target**, not by bucket mass and not by Unity wire `reward`.

## 3.2 Reward Range

Current mission reward max:

- `max_reward = 4.0`

Current shaped components are:

- `loading`
- `approaching_target`
- `depositing`
- `retained_success`

## 3.3 Penalty Settings With Numbers

Current penalty values in Repo A:

- `spill_penalty = 0.25`
- `unsafe_distance_penalty = 0.25`
- `hard_collision_penalty = 0.75`

Current thresholds:

- `load_mass_threshold_kg = 100.0`
- `bucket_mass_delta_tol_kg = 5.0`
- `target_mass_delta_tol_kg = 2.0`
- `unsafe_distance_m = 0.20`
- Unity hard-collision threshold:
`hard_collision_normal_force_thresh_n = 5000.0`

Practical interpretation:

- a new hard collision event costs `0.75 / 4.0 = 18.75%` of the full step reward range
- unsafe target approach costs `0.25 / 4.0 = 6.25%`
- spill costs `0.25 / 4.0 = 6.25%`

## 3.4 Exact Current Trigger Logic

### Spill

`spill_penalty` is applied when all of the following are true:

- previous bucket mass is at least `50.0 kg`
because the code uses `0.5 * load_mass_threshold_kg`
- current bucket mass drops by at least `5.0 kg`
- target mass does **not** show meaningful increase:
`delta_mass_in_target_box_kg < 2.0 kg`
and `delta_deposited_mass_in_target_box_kg < 2.0 kg`

This is the current operational definition of:

- material was lost from the bucket
- but it was not deposited into the target

### Unsafe Distance

`unsafe_distance_penalty` is applied when:

- `min_distance_to_target_m <= 0.20 m`

This is a near-collision / risky-approach penalty.
It does **not** require actual contact.

### Hard Collision

`hard_collision_penalty` is applied when:

- `target_hard_collision_count` increases on the current step

That increase only happens when:

- the excavator starts a new continuous contact session with the active target
- the session reaches at least `5000.0 N` monitored normal force

While the excavator remains in continuous contact with the target:

- the count does not keep increasing every frame
- the penalty does not keep re-triggering every frame

## 4. Response To The Latest Questions

### 4.1 Should Collision Be A Penalty, Or Should Collision Force Reward Very Low?

The current answer is:

- yes, collision should affect reward
- we already implemented that path
- but the current rule is still a **soft penalty**, not a hard fail rule

Current strength:

- one new hard-collision event costs `0.75`
- on a reward scale capped at `4.0`

So the present behavior is:

- collision clearly hurts reward
- but collision does **not** yet force reward near zero
- collision does **not** yet terminate the episode
- collision does **not** yet block success directly

This means the current system says:

- collision is bad
- but collision is not yet treated as absolutely forbidden

That is a reasonable first implementation because it lets us verify the signal
path and inspect behavior. But if the team agrees that target collision must be
strictly avoided, then the next design discussion should consider one of these
stronger rules:

1. On hard collision, set step reward close to `0`
2. On hard collision, terminate the episode
3. Keep the current mass-based success rule, but add a no-collision constraint
4. Scale penalty by force magnitude instead of using a fixed `0.75`

Current recommendation for discussion:

- keep success mass-based
- decide whether collision should remain a soft penalty or be upgraded to a
hard safety constraint

### 4.2 Should Time / Efficiency Also Affect Reward?

Yes, this concern is valid.

Right now, the task mainly rewards:

- getting material into the bucket
- moving toward the target
- depositing retained mass into the target
- holding retained mass above the success threshold

What it does **not** yet explicitly reward is:

- finishing the loop quickly
- maintaining high throughput
- minimizing cycle time

So a strategy that is correct but very slow can still look acceptable under the
current success rule.

Current conclusion:

- success should probably stay mass-based
- but reward and evaluation should start accounting for efficiency

The most practical ways to add efficiency later are:

1. Add a small per-step time penalty
2. Add an early-finish bonus once success is reached
3. Add cycle-time metrics for `dig -> rotate -> dump -> return`
4. Evaluate throughput, for example retained target mass per unit time

Current recommendation for discussion:

- do not overload the success definition first
- add efficiency through reward shaping or eval metrics

## 5. Validation Status

The following checks passed after today's reward / collision update:

- `dotnet build /home/pingfan/AGXexcavator/Assembly-CSharp.csproj -nologo`
- `python -m py_compile` on the modified reward/test files
- `conda run -n aloha python -m unittest tests.test_agx_protocol tests.test_agx_repoa_integration`

Result:

- Unity `Assembly-CSharp` build passed with only the two existing warnings
- `14 tests OK`

## 6. Suggested Follow-Up For The Next Meeting

The most useful questions to settle next are:

1. Should hard collision remain a soft penalty (`0.75`) or become a hard safety rule?
2. Should efficiency enter the reward through time penalty, early-finish bonus, or both?
3. Should we start reporting cycle-time / throughput metrics alongside retained-mass success?

For now, the current working rule remains:

- task success is still defined by retained target mass
- collision already affects reward
- efficiency has not yet been explicitly priced into reward, but it should be discussed next
