# Issue #749 — Widgets: overrides by platform, surface and size, and Explain()

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/749
**Branch:** issue/749-widgets-overrides-by-platform-surface-and-size-and-explai
**Status:** Completed

Step W6 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D5.

## Changes

- `Core/WidgetOverride.cs`: `WidgetPlatform` and `WidgetPlace` (flags), `WidgetOverride` (`when`, `set`), `WidgetOverrideWhen`.
- `WidgetNode.Hidden` and `WidgetNode.On`; `.Hidden()`, `.On(platform | place | size | all four, change)`, `.Italic()`. `.On` serializes the node before and after `change` and writes the difference as a merge patch (`WidgetJson.Patch`); a change to the type or the children is refused.
- `Serialization/WidgetOverrides.cs` (`DrawPlace`, `Apply`, `Matches`, `Sets`): the C# applier Android and `Explain` share.
- `Core/WidgetExplain.cs`: `layout.Explain(platform)` and `tree.Explain(platform, place, size)`.
- Android: the notification applies its regions' overrides for `notification` before both presentations; Spine's own rules (the root's padding, the header's clock) give way to an explicit override (`Hidden(false)`, an override setting padding). Widgets apply theirs per family on `homeScreen`.
- iOS: `Overrides` in `SpineWidgetRenderer.swift`, the same rules over `JSONValue`, after shared nodes are resolved: per Live Activity region (`lockScreen`, `island`, `carPlay`/`watch` for `small`) and per timeline family (`lockScreen` for accessory sizes, `homeScreen` otherwise), also for a remote source.
- `LiveActivityService` logs `Explain` at Debug level when an activity starts.
- The internal `Widgets.Services.WidgetPlatform` class is now `NativeWidgetPlatform`, so the public enum's name is free in Spine's own files.
- Tests: `WidgetOverrideTests`. Docs: `widgets.md` "Overrides by platform, place and size"; the plan's W6 row and a refinement of D5.

## Decisions

- **`WidgetPlace`, not `Surface`, in C#.** Found on the device run: `Surface.Notification` did not compile in Glukos, whose `GlucoseActivity.Surface` is a colour; Puckkoll has the same, and `WidgetColor.Surface` exists. The file format keeps `Surface="…"`.
- **No `W.OnPlatform`/`W.OnSize` in C#.** A typed value per platform would need a wrapper on every property; `.On(…)` already covers node properties, and the file format's markup compiles to it.
- **Overrides are applied on the JSON**, after shared nodes are resolved, on both platforms: each region is known there, and a shared node's override then applies per region it is used in.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 304 passed.
- Glukos with a temporary probe ("bara iOS" hidden on Android, "bara Android, 18 sp" hidden on iOS and 18 sp on Android, the age `.On(WidgetPlace.Notification, n => n.Hidden(false))`):
  - Pixel 10 Pro emulator, Android 17, drawn notification: "bara Android, 18 sp" at 18 sp, no "bara iOS", the age kept in the tree. `docs/plans/images/w6-android.png`.
  - iPhone 17 Pro simulator, iOS 26.4, Lock Screen: "bara iOS", no "bara Android". `docs/plans/images/w6-ios.png`.
- Widgets' per-family path builds but was not drawn on a device (Glukos has no widget).
