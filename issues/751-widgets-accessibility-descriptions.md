# Issue #751 — Widgets: accessibility descriptions

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/751
**Branch:** issue/751-widgets-accessibility-descriptions
**Status:** Completed

Step W8 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D10.

## Changes

- `WidgetNode.Description`, `WidgetNode.Decorative`; `.Description(…)`, `.Decorative()`.
- iOS: a node's description is its label (its children ignored), a decorative node is hidden; each Live Activity region and each widget is one combined element, except one with a button (`Node.hasButton`), which contains its children so VoiceOver reaches the button.
- Android: `RemoteViewsRenderer.Sentence`: the root's description, or the descriptions and texts in order (decorative left out), as the root's content description; the per-node layouts keep icons and pictures out of the accessibility tree.
- `Explain`: an icon, picture, sparkline or ring with neither a description nor `Decorative` "says nothing to a screen reader".
- Tests: `WidgetAccessibilityTests`. Docs: `widgets.md` "Accessibility"; the plan's W8 row.

## Verification (with W7)

Glukos with its Android text taken out and parts and a description added:

- Pixel 10 Pro emulator, Android 17, template: title "Alex 4,7 ↑" (subject, value, the arrow's alt text), text "mmol/L" (detail), the age as the chronometer, the graph as the large icon, chip "4,7↑". `docs/plans/images/w7-android-template.png`, `w7-android-chip.png`.
- iPhone 17 Pro simulator, iOS 26.4: the Lock Screen draws as before with the regions combined.
- Not listened to with VoiceOver or TalkBack.
- `Plugin.Maui.Spine.Server.Tests`: 308 passed.
