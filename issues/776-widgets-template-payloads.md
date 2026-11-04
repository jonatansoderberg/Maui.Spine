# Issue #776 — Widgets: template and data payloads for Live Activities

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/776
**Branch:** issue/776-widgets-template-payloads
**Status:** Completed

Step W15 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), §5 payload modes. The owner chose to build it on 2026-11-04.

## Plan

A push carries the values of a template the device installed, instead of the whole layout. The template and the values are written by the layout file's generator; the device only puts values in place and picks branches, in Swift in the widget extension and in C# on Android, so the two interpreters stay small and alike.

## Changes

- Common: `LiveActivityTemplate` (`Id`, `Json`, `Icons`, `Render`, `RenderLayout`, `Parse`), `LiveActivityTemplateData`, `LiveActivityTemplateBuilder` and `LiveActivityTemplateValues` for the generated code, the `$if` / `$each` / `$` nodes, `LayoutTemplates` (the C# interpreter); `SpineWidgetsOptions.Templates`, `ILiveActivityService.Templates`, `LiveActivityTokens.Templates`, `PushKeys.Template` and `TemplateData`.
- Generator: `Payload="Template"` writes `Template` and `Data(model)` from a second pass: placeholders for bound values, `$if` for conditions and `Choose`, `$each` for `ForEach`, template uses as sites with their own keys, values the factories transform collected after them; the same value in a branch sent once; `SPL016` when the layout cannot be a template, and why.
- Widgets: `SpineLayoutTemplates.swift` (the Swift interpreter, and `JSONValue`, pure Foundation), `ContentState` reads `t` and `d` from a push and fills the template in from the App Group (`InstalledTemplates`); `IWidgetPlatform.InstallTemplates` writes them there at start and removes old ones; their icons are drawn at start.
- PushNotifications: the app reports its templates with the Live Activity tokens; on Android a pushed template is filled in from `SpineWidgetsOptions.Templates`.
- Server: `PushPayloads.ApnsLiveActivity` and `FcmLiveActivity` from template data; `IPushSender.StartLiveActivityAsync` / `UpdateLiveActivityAsync` with both the layout and the data, `PushSender` sending each device the one it can draw.
- Sample: `Reading.layout.xml` as a template, installed by the Showcase; the Layout preview page starts it as a Live Activity and writes the push that updates it with values alone.
- Tests: `TemplateTests` (the template filled in is `Build`'s layout for every Glukos case and a list with and without items; the id; the size), `SwiftTemplateTests` (the Swift interpreter, compiled on the Mac, gives the same), `PushSenderTests` (values to the device with the template, the layout to the others, on both platforms).
- Docs: `widget-layout-files.md` "Sending only the values" and SPL016; the plan's W15 row, note and §9.

## Decisions

- **Values formatted by the sender**, not expressions for the device to evaluate: no formatting, cultures or arithmetic in Swift, and the same text on every platform.
- **The template is built at run time from the generated code**, with placeholders the JSON writes like any value, rather than written as JSON by the generator: it cannot drift from `Build`.
- **Opt-in per file** (`Payload="Template"`), with a warning when the layout cannot be one, rather than a silent tree.
- **Only the current template** is produced on the server; a device that reported another gets the layout, so an app update needs no coordination.

## Verification

- Layouts.Tests 68 passed (with the Swift interpreter compiled and run on the Mac), Server.Tests 309 passed.
- iPhone 17 Pro simulator, iOS 26.4: the Showcase installed `reading@f8ceafbd` in the App Group at start; the reading's activity, updated with its 626 bytes of values through the bridge, drew 12,1 ↗ · Very high with its graph (`docs/plans/images/w15-template-push.png`).
- `xcrun simctl push` cannot deliver to a Live Activity, so a real APNs push of values was not tried; Android was not run.
