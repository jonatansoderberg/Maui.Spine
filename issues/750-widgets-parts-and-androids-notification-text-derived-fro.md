# Issue #750 — Widgets: parts, and Android's notification text derived from them

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/750
**Branch:** issue/750-widgets-parts-and-androids-notification-text-derived-fro
**Status:** Completed

Step W7 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D6.

## Changes

- `ActivityPart` (`subject`, `value`, `symbol`, `detail`, `time`, `graph`, `progress`), `WidgetNode.Part`, `.Part(…)`; `IconNode.AltText`, `.AltText(…)`.
- Android `LiveUpdateNotifications`: without the layout's `Android` text, the title is subject, value and symbol; the text the detail; the chip value and symbol; the chronometer the time part first; the large icon the graph part first. `Spoken` reads a text, an icon's alternative or a stack's texts. Without parts the old guess stays.
- `Explain`: two nodes of one part in a region; on Android, no notification text and no subject or value.
- **Fix in W6's `Explain`:** a tree with no clock threw (a default tuple from `FirstOrDefault` matched `is { }`).
- Tests: `WidgetPartTests`. Docs: `widgets.md` "Parts"; the plan's W7 row, W8 Ready.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 306 passed; the Android library builds. Drawn on devices with W8.
