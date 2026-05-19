# AGXUnityE85ExcavatorSim

This repository is the Unity-side E85 baseline for the excavator simulation.
The intended upstream branch is `E85baseline` in:

```text
https://github.com/FansenXi/AGXExcavatorCodeDoc
```

## Current Project Identity

- Unity project root: `/home/pingfan/AGXUnityE85ExcavatorSim`
- Main scene: `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`
- Main machine rig: Bobcat E85 (`Excavator_BobcatE85 Variant` / `Excavator_BobcatE85`)
- Historical asset folder and C# namespace: `AGXUnity_Excavator`

The historical asset folder name is kept for Unity scene/reference
compatibility. Project-level docs and integration records should use
`AGXUnityE85ExcavatorSim`.

## Scene Scale

The scene environment scale source of truth is:

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Editor/CodexSceneScaleConfig.cs`

`DefaultEnvironmentScale` is `1.5`. The old `1.25` value is preserved only as
`LegacyEnvironmentScale` for migration/detection.

Use the Unity menu item `Tools/AGX Excavator/Codex/Scale Scene Environment To Default 1.5x`
to rescale non-excavator scene content. The tool scans scene elements
automatically and excludes the excavator hierarchy.

## Runtime HUD

`ExperimentHUD` is the runtime operator-facing panel in the main scene. It uses
a larger draggable IMGUI window for 4K displays and can be minimized from the
HUD header.

When Runtime Config is visible, the HUD exposes machine-level response sliders
for `Swing`, `Boom`, `Stick`, and `Bucket`. `max speed` defines what normalized
full command (`action = 1.0`) means for each machine joint, and `accel` defines
how quickly the target-speed actuator may approach that speed. These settings
live below the command source, so they affect manual control, ACT control, and
step-ack actions consistently. The default max-speed values match the previous
manual-control magnitudes: swing `0.40`, boom `0.20`, stick `0.45`, and bucket
`0.45`.

## Tracking Policy

The baseline keeps the current Unity project layout, but it intentionally does
not version heavy or generated assets such as material textures, meshes,
packages, native plugin binaries, logs, `Library/`, `Temp/`, and local
experiment output.

Git is limited to source-like files:

- C# and other source/shader files
- Markdown documentation
- JSON/YAML and similar text configuration
- Unity scene files (`.unity`)
- Unity `.meta` files that correspond to the tracked source-like files
- Unity `Packages/` and `ProjectSettings/` text configuration

The result is a lightweight baseline branch for code review and future work.
Runtime machines that need full visual fidelity or native AGX binaries should
restore those external assets separately.
