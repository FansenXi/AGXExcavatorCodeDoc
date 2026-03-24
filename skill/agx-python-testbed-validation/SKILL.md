---
name: agx-python-testbed-validation
description: Run standard Python validation for the linked AGX Repo A testbed after protocol or mission changes. Use when the user mentions conda env aloha, test_agx_protocol, test_agx_repoa_integration, py_compile, or validating Python-side reward/success/episodes. Triggers on Repo A, testbed unittest, AGX mission Python, episode_len, teleop max_steps.
---

# Python testbed validation (Repo A)

## Preconditions

- Repo A is a **separate** repository from AGXExcavatorCodeDoc; obtain its root path from the user or workspace if not already open.
- Default env name in project docs is often `aloha`; confirm active env if commands fail.

## Commands (run from Repo A root)

Compile changed modules (adjust paths to what was edited):

```bash
python -m py_compile <paths/to/changed/files.py>
```

Run focused protocol/integration tests:

```bash
conda run -n aloha python -m unittest tests.test_agx_protocol tests.test_agx_repoa_integration
```

If `conda` is unavailable, use the project's venv Python the same way.

## After a Unity wire change

- Re-run tests above after updating Repo A parsers/constants to match `Docs/protocol.md`.
- If tests do not exist for a new field, note the gap in the report instead of skipping validation entirely.

## Reporting

- State repo path, exact command lines, and `OK` / failure output summary.
- Do not claim tests passed without executing them in the environment the user uses.

If Repo A is not on disk, tell the user what to run and what files in Unity/docs must stay in sync until the testbed is available.
