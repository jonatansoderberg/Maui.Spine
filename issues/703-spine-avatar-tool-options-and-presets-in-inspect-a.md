# Issue #703 — spine-avatar tool: options and presets in inspect and render-sheet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/703
**Branch:** issue/703-spine-avatar-tool-options-and-presets-in-inspect-a
**Status:** Completed (merged as [#704](https://github.com/jonatansoderberg/Maui.Spine/pull/704) on 2026-10-28)

Step M2.4 of [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md).

## Plan

- **`inspect`** lists options (values, default, bindings) and presets (option values, colours).
- **`render-sheet`**:
  - `--preset <name>` draws that figure's posters and sheets;
  - a package with presets also gets an overview of them, light and dark;
  - a package with options also gets their combinations: the first option across, the second down, others at default.
- **`pack`:** a test proves it keeps both sections (it already did, through `AvatarSlimming`).

## Open Questions

(none)

## Changes

- **`AvatarPainter.Choose(preset, options, dark)`**: sets the renderer's options and a preset's colours. It parses `#RRGGBBAA` itself, since `SKColor.Parse` reads eight digits as `#AARRGGBB`.
- **`AvatarPainter.Figures(...)`**: a grid of figures at rest. `Draw` re-applies the preset's colours for the mode.
- **`render-sheet`:**
  - `--preset` names its files `<id>-<preset>-*`;
  - an unknown preset fails with the presets the package has;
  - new files `<id>-presets-light.png`, `<id>-presets-dark.png` and `<id>-options.png`.
- **`inspect`** prints `option` and `preset` lines.
- **Tests:** `AvatarToolTests` adds 3:
  - render-sheet's grids and a chosen preset's colour;
  - the unknown preset's message;
  - pack keeping options and presets.
- **Follow-ups to #701 from the owner's review of the sheets and mid-step notes**, all in `Tools/mini_avatars.*`, repacked:
  - **Upright outlines.** Harmonics 2–4 of the hand-drawn deviation are now mirror-symmetric: 2 and 4 at phase ±π/2, 3 at 0 or π. The irregularity is in the fifth harmonic and the fuller side. The cube's unequal corners differ top from bottom, no longer diagonally (big corners at the ends of a diagonal read as a turned box). Every outline's mirror axis leans at most 0.5° (the ball cannot be measured this way).
  - **Eyes lean together.** The owner said: "rotate both equally, otherwise it looks strange". Both eyes now lean alike, by `EYE_LEAN · sin(turn)` of the face's turn, instead of each by its own position. `mini_eyereach.py` measures with the shared lean.
  - **Talking eyes grow less still.** Per syllable they are 8 % taller and 3 % larger. The tall eyes had reached 30 % more area.
  - **The drop is widened, not heightened**, by its second harmonic, and the hop narrows as it launches, so both stay in the frame.
  - `MiniTests`: the tall-eye test now checks that the two eyes are parallel (within 2.5°) and that they lean on the body, against its fitted turn.
- **375 tests pass.**

## Decisions

- **The option grid takes the first two options** and leaves any others at their defaults. A grid of every combination grows past what a sheet can show. Two options cover the Minis and are what a reviewer compares.
- **A lab capture can freeze when the lab window is behind others.** macOS stops drawing an occluded Metal view, so `shotview` returns the last frame drawn. Seen while checking the eyes. Restarting the lab (`Tools/lab/start.sh`) brings the window to the front.
