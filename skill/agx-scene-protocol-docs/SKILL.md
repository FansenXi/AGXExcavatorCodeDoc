---
name: agx-scene-protocol-docs
description: Maintain AGXUnity excavator scene and wire-protocol documentation consistently. Use when updating or reviewing Docs/scene.md, Docs/scene.zh-CN.md, Docs/protocol.md, Docs/scene_report.md, or answering questions about env_state, step-ack contract, targets, or testbed alignment. Triggers on requests like scene contract, protocol doc, V0 export, mass_in_target, deposited_mass, min_distance_to_target, Chinese mirror, or documentation source of truth.
---

# AGX scene and protocol documentation

## Authority order

1. `Docs/scene.md` — English source of truth for scene/task behavior.
2. `Docs/protocol.md` — wire format, framing, field order, reset flags.
3. Current code under `AGXUnity_Excavator_Assets/Scripts/` and the live Unity scene.

If `Docs/scene.zh-CN.md` exists, it is a reading mirror only; English wins on conflict.

## Before editing

- Read the sections you will change in both `Docs/scene.md` and `Docs/protocol.md` so `env_state` order and semantics stay aligned.
- If the change affects shipped behavior, note whether `STEP_RESP.reward` or success rules changed and update every doc that mentions them (avoid leaving `scene_report.md` stale).

## After editing

- Sync `Docs/scene.zh-CN.md` with the same factual claims as `Docs/scene.md` when that file is in scope.
- For teammate status snapshots, use `Docs/scene_report.md` style: date, summary, validation commands run, open items — without replacing `scene.md`/`protocol.md`.

## Current `env_state` contract (verify in protocol if unsure)

Five floats, in order: `mass_in_bucket_kg`, `excavated_mass_kg`, `mass_in_target_box_kg`, `deposited_mass_in_target_box_kg`, `min_distance_to_target_m`.

## Code touchpoints when docs change

- `AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActProtocol.cs` — names/order of observation fields.
- `AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs` — assembly of `env_state`.
- `AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs` — step-ack payload and reward field if documented.

Do not expand scope into unrelated docs or refactors.
