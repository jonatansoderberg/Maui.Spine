# Issue #293 — A stack cannot be told to share the width, so columns differ between iOS and Android

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/293
**Branch:** issue/293-stack-fill
**Status:** Completed

## Plan

Three columns side by side came out equal on iOS and packed to the left on Android. On iOS a stack whose rows hold spacers is greedy; on Android an inline stack is `wrap_content`, so the spacers inside it have nothing to distribute. Puckkoll's widget showed it: logos close together, the score left of centre, where the same tree is symmetric on iOS.

1. `StackNode.Fill` (`fill` in JSON) with `.Fill()`.
2. Android: layouts of their own with `layout_weight`, since a weight cannot be set after inflation — `0dp` along the parent's axis, one pair per orientation.
3. iOS: `frame(maxWidth: .infinity)` in a row, `maxHeight` in a column, with the box (padding, background, corners) applied inside the frame so a filled box still draws its background.
4. A round-trip test, the wiki, and Puckkoll's widget as the proof.

## Changes

- `StackNode.Fill` and `WidgetNodeStyling.Fill()`.
- Android: `spine_widget_{v,h}stack_fill_{width,height}.xml`, chosen by the parent's axis in `RemoteViewsRenderer.Render`.
- iOS: `BoxModifier` fills along the parent's axis when `fill` is set, and draws its box inside that frame.
- `WidgetLayoutRoundTripTests.A_filled_stack_says_so_and_a_plain_one_does_not`.
- docs/wiki/widgets.md: what `.Fill()` is for, next to spacers.

## Decisions

- Four layout files rather than one with a runtime orientation: `RemoteViews` has no reliable way to set orientation or weight after inflation, and the renderer already knows the parent's axis.
- `.Fill()` is on stacks only. A text that fills its row is `.Centered()`, and an image has its own size; the ambiguity is not worth the surface.
