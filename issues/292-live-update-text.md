# Issue #292 — A Live Update's title, text, chip and icon should be the app's to choose

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/292
**Branch:** issue/292-live-update-text
**Status:** Completed

## Plan

Android built its Live Update by reading the iOS tree: the first `Headline`/`Title` text as the title, the next text as the body, the first `W.Icon` as the small icon, the first text of `CompactTrailing` as the status-bar chip. For a layout built around the Dynamic Island that guesses badly — Puckkoll showed "1–1" and "Brynäs" with a chip reading "0" (the away goals, which mean something only beside the away logo) and the app icon as a blue circle.

1. `LiveActivityLayout.Android` (`LiveUpdateText`: title, body, chip, icon), serialized as `android`.
2. `LiveUpdateNotifications.Post` prefers it, field by field, and keeps today's derivation for anything left out.
3. `Icon` names a stored asset, drawn as the notification's large icon — the one picture the promoted template has room for.
4. A round-trip test and the wiki.

## Changes

- `LiveUpdateText` and `LiveActivityLayout.Android`.
- `LiveUpdateNotifications.Post` reads title, body and chip from it, and sets a large icon from the named asset.
- `WidgetLayoutRoundTripTests.What_android_should_say_survives_the_trip`.
- docs/wiki/widgets.md, under Live Updates.

## Decisions

- Field by field rather than all or nothing: an app that only wants a better title should not have to spell out the rest.
- The icon is a stored asset, not an SVG icon name: a club badge is a picture, and the assets are already there for the widget.
- The small icon keeps coming from the tree's first `W.Icon`. It is a monochrome status-bar glyph, which a logo cannot be.
