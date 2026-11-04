# Issue #744 — Widgets: padding per side and margin

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/744
**Branch:** issue/744-widgets-padding-per-side-and-margin
**Status:** Completed

Step W1 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D1.

## Plan

- `WidgetThickness` (left, top, right, bottom) as MAUI's `Thickness`, written as `[l, t, r, b]`.
- `.Padding(all)`, `.Padding(h, v)`, `.Padding(l, t, r, b)`, `.Padding(thickness)` on stacks; `.Margin(…)` with the same overloads on every node.
- JSON that an older app still reads: uniform padding stays `"padding":n`; uneven sides go in `"insets"` with `"padding"` the smallest side; margin in `"margin"`.
- iOS: `EdgeInsets` (leading and trailing, so right-to-left mirrors); margin as padding outside the node, after its box.
- Android: `setViewPadding` per side, start and end swapped in right-to-left; margin through `setViewLayoutMargin` from Android 12, a padded frame below; a stack's spacing adds to a child's leading margin instead of replacing it.

## Changes

- `Core/WidgetThickness.cs`: the type and its converter (reads a number or a list of four).
- `WidgetNode.Margin`, `StackNode.Insets`; the padding and margin overloads in `W.cs`.
- `SpineWidgetRenderer.swift`: `insets`, `margin` on `Node`; `Insets.edges`/`Insets.padding`; `BoxModifier` pads by the edges; `NodeView` pads outside by the margin.
- `RemoteViewsRenderer.cs`: `Render` applies the margin around `RenderNode`; `Box` pads from `insets` or `padding` through `Pad` (right-to-left aware); the stack's spacing adds the child's own margin; `spine_widget_margin.xml` for Android below 12.
- `LiveUpdateNotifications.Banner` drops `insets` with `padding` from the drawn notification's root.
- Tests: `WidgetThicknessTests` (uniform as before, uneven in insets with the smallest as padding, horizontal and vertical, a uniform replacing insets, margin on any node, negative sides refused).
- Docs: `widgets.md` (Backgrounds and boxes, both mapping tables); the plan's W1 row.

## Decisions

- **`padding` keeps the smallest side** when `insets` is written, so an app with an older Spine draws a tighter box rather than a looser one.
- **Below Android 12 a margin is a frame**, which loses a filled stack's share of a row; RemoteViews has no way to set layout margins before API 31. Documented rather than worked around.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 281 passed.
- `swiftc -typecheck` of the extension sources; the Android library builds.
- Pixel 10 Pro emulator (Android 17), Glukos in the drawn presentation with a temporary `Padding(40, 4, 4, 30)` on the reading row and `Margin(0, 16, 0, 0)` on the unit line: both drawn as given (screenshot in the PR). iOS not run: the change is `EdgeInsets` in place of one number.
