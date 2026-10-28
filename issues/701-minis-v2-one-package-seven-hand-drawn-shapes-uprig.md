# Issue #701 — Minis v2: one package, seven hand-drawn shapes, upright, shaded, livelier jump

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/701
**Branch:** issue/701-minis-v2-one-package-seven-hand-drawn-shapes-uprig
**Status:** In Progress

Step M2.3 of [docs/plans/spine-avatar-mini-v2.md](../docs/plans/spine-avatar-mini-v2.md). The decisions (M-D1–M-D9) are in the plan.

## Plan

One `mini.spineavatar` replaces the nine v1 packages (they are removed in M2.5):
- Options `shape` (ball, cube, drop, bowl, bean, inverted-drop, marshmallow) and `eyes` (round, tall).
- The nine v1 figures as presets.
- One shader template, `Tools/mini_avatars.sksl`. The generator, `Tools/mini_avatars.py`, fills in each shape's outline and face.
- `Tools/mini_eyereach.py` measures each face's eye reach and foot height when the package is generated.

## Open Questions

(none)

## Changes

- **Shapes:**
  - Seven outlines, each with a fixed hand-drawn deviation: harmonics 2–5 of the angle and one side a little fuller, about ±0.012–0.020 (under 3 % of the size).
  - The cube's corners differ and its sides bow.
  - The second harmonic's phase is ±π/2, so the deviation never turns an outline into a leaning oval.
  - Every outline leans under 1° (measured as the line between its halves' centres).
- **Upright:** the per-figure tilt is gone. The body leans up to about 4.6° toward an active look (the scheduler's gaze, or a held look) and shifts toward it. A soft highlight slides the other way.
- **Eyes:**
  - They lean with the body's curve, up to 0.2 rad, with the top toward the middle, so tall eyes stand upright only when looking straight ahead. This was added from the owner's note mid-step: "tall eyes do not have to be 180 degrees".
  - Each face's eye reach (round and tall, at 1× and 1.3×) and foot are measured on the irregular, leaning outline. The generator refuses a face whose reach leaves no room past its eyes' spread; the bowl's tall eyes were made smaller after the first run.
- **Shading:**
  - lighter toward the top;
  - a soft highlight;
  - a slightly darker rim and foot;
  - the body's luminance spread stays between 4 % and 30 % (tested).
- **Dark mode:** one colour serves both modes; the shader lifts it 10 % toward white in dark mode (M-D2). The presets give one colour.
- **Talking eyes:** per syllable they grow by 4 % and 12 % taller (v1: 55 % taller), lift by 0.065, and move slightly from side to side.
- **Jump:**
  - a deeper crouch with the foot on the ground and the eyes squinting;
  - a launch stretched 20 % and narrowed;
  - a flight of 0.17;
  - a landing that squashes, springs past rest and settles by the end.
  - The hop is the same, smaller. The figure at rest is 9 % smaller than v1 (`BODY_SCALE` 1.0, M-D4).
- **Package:** `mini.spineavatar`, 33 KB packed with 192 px posters (the 256 px ones were 47 KB; the shading compresses less than v1's flat colour). `assetVersion` 2.0.0, `minRuntimeVersion` 1.1.0.
- **Lab:** "Mini" in the avatar list.
- **Tests:** `MiniTests` adds 68, all passing:
  - the package and its choices;
  - per preset: blink and talk, subtle shading and the dark lift;
  - per shape and eye style: both eyes far away;
  - per shape: upright at rest and leaning into a look (the rotation that best fits the outline; the ball's outline cannot show a turn), tall eyes on the body's curve, gestures inside the frame, nod and shake with the eyes, and the jump's crouch, launch, flight and rest.
  - The v1 tests still run on the v1 packages until M2.5. 372 pass.

## Decisions

- **The outline's turn is measured by fitting a rotation**, not by the line between its halves' centres. On a wide shape, such as the bean, the far end swings into the other half as it turns, and the centroid line reported the opposite sign.
- **The eye lean follows the body's curve**, with the top toward the middle, rather than leaning into the look. On a turning solid that is how a vertical mark on its side looks. The body's own lean toward the look partly cancels it for the outer eye, which keeps it subtle (about 2–3° on screen).
- **192 px posters**, to meet the plan's size target. They are only the gallery thumbnail; apps draw the avatar live.
