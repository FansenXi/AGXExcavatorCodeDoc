---
name: agx-daily-progress-report
description: Write a concise dated progress report for AGXUnity excavator work (teammate-facing, not replacing protocol docs). Use when the user asks for a daily summary, scene report, sprint note, or handoff after Unity/testbed/doc changes. Triggers on scene_report, what we did today, progress note, teammate update, validation summary.
---

# Daily progress report (scene report style)

## Output target

- Prefer a new or updated markdown file such as `Docs/scene_report.md` with **today's date** in the title (or a dated filename if the user prefers).
- Explicitly state this does **not** replace `Docs/scene.md` or `Docs/protocol.md`; on conflict, those win.

## Structure to follow

1. **Executive summary** — 3–6 bullets: main outcome (scene contract, testbed reward/success, episode budget, etc.).
2. **Contract / status** — Current `env_state` order and meanings; note Unity `reward` placeholder vs mirrored value if relevant.
3. **Completed today** — Grouped subsections (docs, testbed, Unity, validation).
4. **Validation** — Commands actually run (e.g. `python -m py_compile`, `unittest` modules) and pass/fail counts; optional episode table if data was checked.
5. **Open items** — What was explicitly not done.
6. **Next steps** — Short numbered list.

## How to gather facts

- Use `git log` / `git show` for the date range to list touched files and commit themes.
- If Python testbed (Repo A) is outside the repo, say so and list only what was verified in-repo; ask for Repo A path to complete testbed file lists.

## Tone and language

- Technical, complete sentences; tables for episode checks when applicable.
- Match the team's language preference for the report body (e.g. English for `scene_report.md` if that is the team norm).

Do not invent validation results or file changes not supported by git or artifacts the user provided.
