# Issue #628 — Avatar tests: two flaky tests (a black shader frame, a canvas wrapper allocation)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/628
**Branch:** issue/628-avatar-test-flakes
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), found after C2

## Plan
Catch each failure with its data, then fix the cause where one can be shown, and the measurement where the failure is the measurement's.

## What the failures were
- `AvatarShaderTests.VoiceGrowsTheRibbons` failed twice in about 60 full runs. Instrumented, the quiet frame was one colour, opaque black (`#ff000000`, 40000 of 40000 pixels lit), and the same render repeated straight after was right (1199). So it was not a threshold near its edge, but something drawn from an invalid image or effect. Two stress tests did not reproduce it alone: 8 threads for 25 s with forced collections, and 15 cold processes with 8 threads compiling at once. It came only in the full suite.
- `Spine2dRendererTests.EvaluateAndDrawDoNotAllocate` failed 3 times in 60 runs and once in 35 after holding the canvas, each time with exactly 2168 B. A steady allocation would show in every window; a one-off of the same size is the runtime counting an allocation context when a collection lands in the window. 8080 B was seen the same way in other tests in #607.

## Changes
- `AvatarShaderModel` keeps the checked SkSL source and disposes the effect it compiled to check it. Each `AvatarShaderRenderer` compiles its own effect and disposes it with itself. Before, the model's effect was never disposed, so it was left to the finalizer; the package and the app had the same pattern.
- `AvatarShaderTests` runs in the `Measurements` collection, away from the parallel classes.
- `Measurements.Allocated(work)` measures up to three windows and returns the fewest bytes. The four zero-allocation asserts use it: the level meter, the glTF rig and the two spine2d renders.
- The spine2d and glTF render tests hold their `SKCanvas` instead of reading `surface.Canvas` every frame (SkiaSharp keeps a weak reference to the wrapper).

## Verified
- 100 full runs of the avatar tests with all changes: 100 passed. Before: 5 failures in about 60 runs (2 ribbons, 3 allocation); with the shader change alone, 38 runs had no ribbons failure but one allocation failure.
- Controls.Avatar builds for iOS.

## Decisions
- **The effect's ownership is the renderer's**: one extra SkSL compile per view load (a few ms) instead of a native object only the finalizer frees. That it caused the black frame is likely, not proven: the stand-alone stress tests did not reproduce it, and the full suite stopped failing with this change and the collection change together.
- **The allocation tests take the fewest of three windows**, not a tolerance: zero stays the bar, and real per-frame garbage still fails every try.
- The lab's own copy of the shader renderer (`samples/SpineAvatarLab/Avatar.Skia`) is left alone while #542 is open; B1.3 moves the lab onto the package.
