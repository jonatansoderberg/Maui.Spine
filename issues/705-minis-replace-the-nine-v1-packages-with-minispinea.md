# Issue #705 — Minis: replace the nine v1 packages with mini.spineavatar

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/705
**Branch:** issue/705-minis-replace-the-nine-v1-packages-with-minispinea
**Status:** In Progress

Step M2.5 of [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md).

## Plan

- Remove `samples/Avatars/mini-<colour>.spineavatar` (nine files). `mini.spineavatar` holds the same figures as presets.
- The lab lists "Mini" only.
- The reference-avatar tests list `mini`.
- The v1 shader tests go; `MiniTests` covers them per preset and shape.
- The options tests that edited mini-red move to Aurora Flow.

## Open Questions

(none)

## Changes

- The nine v1 packages are removed. They had not been released, and nothing outside the lab and the tests used them (not the Showcase, not the sample server's list).
- `LabPage.Bundled`: the nine v1 entries are gone, and "Mini" stays.
- `AvatarArchiveTests.ReferenceAvatars`: `mini` replaces the nine.
- `AvatarShaderTests`: the v1 Mini tests are removed (blink and talk, eyes far away, gestures in frame, nod and shake, jump) along with their helpers. `MiniTests` has the same checks for v2, per preset and per shape.
- `AvatarOptionsTests`: the format 1.1 test package is now built from Aurora Flow. Its option uniform `figure` paints the index into the red channel, the presets colour the `cyan` slot, and the runtime-filled uniform in the conflict test is `dark` (Aurora Flow declares no `blink`).
- 314 tests pass. The lab builds.

## Decisions

- **Aurora Flow carries the options tests.** `mini` already has options and presets, and a test that replaced them would leave its own `shape` and `eyeStyle` uniforms unbound.
