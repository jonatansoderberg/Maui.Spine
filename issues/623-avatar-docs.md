# Issue #623 — Avatar docs: the tool in the skill, gallery texts, package docs

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/623
**Branch:** issue/623-avatar-docs
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step C2

## Plan
Bring the docs in line with what B, S and C1 built: the `spine-avatar` skill's Finish section on the tool, the Showcase gallery's texts, `packages.md`, the READMEs, the authoring profile; look for statements the spikes made stale.

## Changes
- `spine-avatar` skill, Finish step 2: ship with `spine-avatar pack --strip` (Skia posters; SceneKit captures for alpha-masked models), `validate --profile shipping`, `render-sheet`; apps get the packed file, the lab keeps its source. Only that step changed, away from the lines #546 edits.
- `spine-controls` skill: ship the packed file.
- Showcase gallery: the bundled avatars are said to be packed by the tool; Kairo's renderer chip says "glTF · poster" off Apple (its alpha-masked hair needs SceneKit), and the download section says so.
- `packages.md`: rows for a server voice (`ServerSpeech` with a `Kind: Speech` feature) and for the tool.
- Controls.Avatar README: the tool.
- Authoring profile: `native3d` on Android and Windows is Skia on the CPU for models without alpha-masked materials (was "follow S2").

## Decisions
- The skill's "Where things live" table still describes the lab's own copies of the core; it changes with B1.3, after #542, so the two PRs do not collide in the same lines.
