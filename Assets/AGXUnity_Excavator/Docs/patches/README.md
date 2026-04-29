# Local AGXUnity Patches

`Assets/AGXUnity` is an external AGXUnity import and is intentionally ignored by git.
Any project-required AGXUnity source edits must be kept here as reproducible patches.

After importing AGXUnity into a fresh checkout, apply the terrain reset patch from the
repository root:

```bash
git apply Assets/AGXUnity_Excavator/Docs/patches/AGXUnity-DeformableTerrain-reset-native.patch
```

The patch adds `AGXUnity.Model.DeformableTerrain.ResetHeightsAndRecreateNative()`,
which is used by the excavator scene reset path to clear dynamic terrain state by
recreating the native AGX terrain instance while keeping the Unity component stable.
