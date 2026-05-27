# GraphPerceptionPrj

Unity 2022.3.62f3 + AGX Dynamics simulation of a CAT 365 excavator working a
sand pile, used as the **simulation host** for an external Python research
stack (teleoperation recording, ACT imitation learning, future graph-based
dynamics models).

In a three-repo setup this is **Repo B** — it runs the scene and exposes
observations; **Repo A** records datasets and trains policies; **Repo C** owns
the shared wire protocol.

## Open the project

1. Install **Unity 2022.3.62f3** via Unity Hub (exact patch version; the
   project version is committed).
2. Install **AGX Dynamics** + your AGX license per Algoryx documentation.
3. Drop your AGXUnity core distribution into `Assets/AGXUnity/` (this folder
   is gitignored — every developer manages their own copy).
4. Open the project root in Unity. The Algoryx scoped registry pulls
   `com.algoryx.agxunity.machines.*` from npm automatically.
5. The default scene is
   [`Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`](Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity)
   — the simple scene with excavator + sand pool + truck.

## What is in the box

- **TCP binary step-ack server** on port 5057 (`agx-sim/v0`) — see
  [`Assets/AGXUnity_Excavator/Docs/protocol.md`](Assets/AGXUnity_Excavator/Docs/protocol.md).
- **ACT JSON-lines TCP bridge** for inference-time policy integration.
- **4-DOF arm control** (`swing / boom / stick / bucket`) via keyboard,
  gamepad, FarmStick joystick, or ACT backend.
- **FPV camera RGB capture** through `TrackedCameraWindow`.
- **Offline terrain graph observation v0** — JSON snapshots in
  `TerrainGraphSnapshots/`; schema documented in
  [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md).
- **Python visualizers** for graph snapshots under
  [`Tools/TerrainGraph/`](Tools/TerrainGraph).

What is **not** here:

- No ML-Agents (not in `Packages/manifest.json`).
- No HDF5 dataset writer (Repo A owns that).
- No ACT trainer (Repo A).
- No graph payload in the binary step-ack response yet — see protocol
  staging plan in the terrain graph doc.

## Active scenes

| Scene | Purpose |
|---|---|
| [`Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`](Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity) | Simple scene; primary integration target |
| [`Assets/AGXUnity_Excavator/Open-pit mine.unity`](Assets/AGXUnity_Excavator/Open-pit%20mine.unity) | Open-pit mine demo; not yet wired |

## Team documentation

- [`Assets/AGXUnity_Excavator/README.md`](Assets/AGXUnity_Excavator/README.md)
  — subsystem overview, control mappings, reset notes.
- [`Assets/AGXUnity_Excavator/Docs/`](Assets/AGXUnity_Excavator/Docs/) —
  scene contract, step-ack protocol, graph observation schema, AGX patches.

## AI agent documentation

If you're an AI assistant working on this repo:

- Start with [`AGENTS.md`](AGENTS.md) — operating rules.
- Then [`.ai/README.md`](.ai/README.md) — AI documentation map.
- Then [`.ai/handoff/project-context-compact.md`](.ai/handoff/project-context-compact.md)
  — one-page project snapshot.

## License / third-party

- AGX Dynamics is **proprietary**; install per Algoryx's terms.
  License files (`*.lfx*`, `*.lic`, `*.license`) are gitignored.
- AGX core (`Assets/AGXUnity/`) is gitignored; AGX machine packages are
  pulled from a scoped npm registry pinned in `Packages/manifest.json`.
- This repo does not redistribute AGX. It's the integration layer only.
