# Issue #709 — Minis: eyes lean with the figure, the far eye stays in proportion

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/709
**Branch:** issue/709-minis-eyes-lean-with-the-figure-the-far-eye-stays
**Status:** Completed (merged as [#710](https://github.com/jonatansoderberg/Maui.Spine/pull/710) on 2026-10-28)

A follow-up to [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md) (done), from the owner's review on the iPhone.

## Plan

Make the eyes' own lean and the far eye's narrowing into constants. Render variants side by side for red, purple and brown at rest and looking around, pick the most natural, then regenerate, test and pack.

## Open Questions

(none)

## Changes

- **Comparison of six variants** at 400 px (eye lean as before, none, or a little extra with the figure; far-eye minimum width 22 %, 50 % or 65 %):
  - **Lean as before:** the eyes leaned on their own at rest and against the body when it looked.
  - **No own lean:** the eyes follow the figure exactly, which looks natural.
  - **Far eye at 22 %:** a sliver. At 65 % it still reads the curve and stays in proportion.
  - **Extra lean with the figure:** leans more than the figure.
- **Shader:** `EYE_LEAN` and the eye's own rotation are removed, so the eyes lean with the body. `EYE_FAR = 0.65` (was 0.22).
- **`mini_eyereach.py`:** no lean, `EYE_FAR` 0.65. Every face still has room past its eyes' spread; reach shrank by about 0.02–0.05.
- **`MiniTests`:**
  - `TallEyesLeanWithTheFigure`: upright at rest, parallel when looking, and leaning as much as the body, within 2.5°, against looking ahead in the same state.
  - New `TheEyeTurnedAwayStaysInProportion`, per shape and eye style: the far eye keeps over 55 % of the near eye's pixels.
  - 328 tests pass.
- **Lab on the Mac, with the scheduler and springs:** red, purple, orange and brown at rest and looking left, right and up-right.
- **Package:** `mini.spineavatar` repacked, 33 KB, validates as `shipping`.

## Decisions

- **The eyes have no lean of their own.** The owner asked that they lean like the figure. The body already leans into a look, and eyes on the body lean with it. A lean of their own, in either direction, made them disagree with the outline.
