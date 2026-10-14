# Issue #547 — Shimmer draws nothing when its content is a single empty block

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/547
**Branch:** issue/547-shimmer-draws-nothing-when-its-content-is-a-single
**Status:** Completed

## Plan
Reproduce on the iPhone 17 Pro simulator with a scratch page injected at build time (ModuleInitializer + `CustomBeforeMicrosoftCommonTargets`), with five variants: Grid + transparent Image, Grid only, the block alone, the block wrapped in a Grid, and the `BindableLayout` template from #543. Read the overlay's placeholder list by reflection to tell which one breaks it, then fix the scan.

## Changes
- `SkeletonOverlay`: the placeholder layout itself is checked as a block before its children are walked (`CollectPlaceholder` for one element, `CollectPlaceholders` for its children).
- `Placeholder.Radius` is a `CornerRadius`; `SkeletonDrawable` draws each corner with its own radius, so `RoundRectangle 12,12,0,0` keeps a square bottom.
- `Shimmer` remarks and `docs/wiki/shimmer.md` say both.

## Decisions
- Before the fix, only the wrapped variant found a block (1 placeholder); the other four found 0, so the template, the Grid cell and the Image were ruled out. After it, all five find one block and the wave runs.
- The fallback wave over an empty skeleton is kept: it was not wrong, only invisible over a light card.
- Verified on the iOS simulator only (Showcase Shimmer page unchanged); CI is paused.
