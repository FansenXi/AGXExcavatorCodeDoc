---
name: agx-unity-env-state-bridge
description: Change or debug Unity step-ack bridge and exported observations for the AGX excavator sim. Use when editing AgxSimStepAckServer, AgxSimProtocol, ActProtocol, or ActObservationCollector; or when fixing env_state, qpos/qvel dimensions, FPV, reward on STEP_RESP, or binary TCP framing. Triggers on step-ack, DoStep, env_state, observation collector, simulation bridge, TCP framed protocol.
---

# Unity env_state and step-ack bridge

## Workflow

1. Read `Docs/protocol.md` for the current frame layout, field order, and types.
2. Read `Docs/scene.md` for semantic meaning of each `env_state` element (active target, distance `-1.0` sentinel, etc.).
3. Change types and constants in `ActProtocol.cs` first so names and dimensions stay single-source.
4. Implement collection in `ActObservationCollector.cs` (mass sensors, distance utility, trackers).
5. Wire serialization and stepping in `AgxSimStepAckServer.cs` (and `AgxSimProtocol.cs` if framing/helpers live there).

## Invariants to preserve

- `env_state` length and order must match `Docs/protocol.md` and any Python testbed parsers (Repo A).
- Arm control space remains 4D (`swing`, `boom`, `stick`, `bucket`) unless protocol and docs change together.
- Document any change to `reward` on the wire in `Docs/scene.md` and `Docs/protocol.md` in the same change set when behavior changes.

## Common pitfalls

- Updating HUD or logger without updating the step-ack payload (or vice versa).
- Distance unavailable: use the project convention for invalid distance (see `scene.md` / `BucketTargetDistanceMeasurementUtility.cs`).

Keep diffs focused on bridge and observation paths; do not refactor unrelated control or presentation code.
