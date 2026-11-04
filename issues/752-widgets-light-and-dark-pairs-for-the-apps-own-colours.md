# Issue #752 — Widgets: light and dark pairs for the app's own colours

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/752
**Branch:** issue/752-widgets-light-and-dark-pairs-for-the-apps-own-colours
**Status:** Completed

Step W9 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D11 (the colour half; the resource files belong to W12).

## Changes

- `WidgetColor.Pair(light, dark)`, written `"#RRGGBB/#RRGGBB"`; `Parse` reads it back.
- iOS `Palette.pair`: a dynamic `UIColor` resolved in the environment's appearance, so the dark Lock Screen's `darkScheme` takes the dark one.
- Android: `WidgetPalette.Pair`; `Color()` sets both through `SetColorInt`/`SetColorStateList` from 12, the current appearance's below; `Argb()` (gradients, rings, sparklines) and `Concrete()` (notification tints) take the current or the light one.
- Tests: `WidgetColorPairTests`. Docs: `widgets.md`; the plan's W9 row.

## Decisions

- **One string, not an object:** iOS decodes `color` as a string, so an object would make an older app fail on the whole layout; an older app reads the pair as an unknown color and draws `Primary`.
- **A pair as a Lock Screen `Background` does not count as dark** for `darkScheme`: it follows the system, as its name says.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 297 passed. Swift typecheck; the Android library builds. Drawn on devices with W10.
