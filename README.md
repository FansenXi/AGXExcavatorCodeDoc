# AGXUnity_Excavator.unity
---------------------------
This scene demonstrates an excavator in an earth moving scenario.

The Excavator is created in Algoryx Momentum, a Dynamics plugin for SpaceClaim: https://www.algoryx.se/momentum/
After exporting the dynamics model, it is imported into AGXUnity as a prefab.

The Tracks (left/right) is added to a parent GameComponent to the Excavator prefab.

Excavator.cs and ExcavatorInputController.cs is added to model the drivetrain and to control the various actuators on the Excavator.
The drivetrain consists of a combustion engine, gears, clutch and brakes. For more details see Excavator.cs

The editor exposes various parameters for the engine and the drivetrain.

## Control the excavator

### Gamepad (XBox360)
- Right Stick X     - Boom up/down
- Right Stick Y     - Move bucket
- Left Stick X      - Swing left/right
- Left Stick Y      - Stick up/down
- D-Pad (X/Y)       - Drive forward/backward/left/right


### Keyboard
- PageUp/Down       - Boom up/down
- Insert/Delete     - Move bucket
- T/U               - Swing left/right
- Home/End          - Stick up/down
- Up/Down           - Forward/Backward
- Left/Right        - Turn Left/Right


# AGXUnity_Excavator_small.unity
---------------------------------
This scene demonstrates an excavator (using the same controls as the one in the previous scene) that can dig in a small limited terrain area. 
The undercarriage is locked to the world.

When material leaves the bucket and collides with the large (blueish) box, the amount of digged material will be counted in volume and mass.

Excavation mass is tracked by `ExcavationMassTracker.cs`.
The terrain reset path is handled separately by `ResetTerrain.cs` and can be triggered with the key `r`.

## ROI Runtime

Runtime ROI visualization now runs outside Unity.

- Unity keeps exporting FPV frames through `AgxSimStepAckServer` as `raw_rgb`.
- `tools/roi_overlay_runtime.py` consumes those frames, runs ONNX inference, draws ROI boxes, and writes H.264 video plus CSV logs.
- `RoiDetectionPipeline` on `RoiDetectRig` is configured to launch that sidecar automatically in `ExternalOverlayRuntime` mode.

Quick test:

1. Open `AGXUnity_Excavator.unity`.
2. Press Play.
3. Wait for the external ROI preview window to appear.

Artifacts are written to:

- `ExperimentLogs/roi_overlay_runtime/roi_overlay_runtime.h264`
- `ExperimentLogs/roi_overlay_runtime/roi_overlay_runtime.csv`

Smoke test without Unity:

```powershell
tools/run_roi_overlay_runtime.ps1 -SelfTest
```
