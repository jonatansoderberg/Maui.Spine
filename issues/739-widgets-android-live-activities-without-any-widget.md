# Issue #739 — Widgets (Android): Live Activities without any widget

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/739
**Branch:** issue/739-widgets-android-live-activities-without-any-widget
**Status:** In Progress

Found in Glukos on an Android 16 emulator (2026-11-03): no Live Activity button. The build already supports a Live Activity without widgets on Android (the permissions are added for `SpineWidgetsLiveActivities` alone, as `widgets.md` says), but at run time every activity call was gated on `IWidgetPlatform.IsSupported`, which on Android means "has a widget receiver".

## Plan

- `IWidgetPlatform.AreActivitiesSupported`, apart from the widgets' `IsSupported`.
- Android: true on Android 16 when `POST_PROMOTED_NOTIFICATIONS` is granted, which for a normal permission means declared, i.e. the build asked for Live Activities.
- `LiveActivityService` and the icon store use it.

## Changes

- `IWidgetPlatform.AreActivitiesSupported`; `NoOpWidgetPlatform` false; Apple: `IsSupported` (the extension carries the activity there).
- `WidgetPlatform.Android`: `_live` is created only on Android 16 with the permission; `AreActivitiesSupported => _live is not null`, which `AreActivitiesEnabled` returns; `StartActivityAsync` logs why when it cannot start; `Update`/`End`/`Active` tolerate no `_live`; `StoreAssetAsync` stores icons for activities too.
- `LiveActivityService`: `IsSupported` and its four gates use `AreActivitiesSupported`.
- `WidgetIconAssets`: rasterizes icons when only activities are supported.
- `docs/wiki/widgets.md`: the property must reach the Android build.

## Decisions

- **The permission as the build's signal**, rather than a new resource: the targets already write it exactly when Live Activities are asked for, and `CheckSelfPermission` answers for a normal permission without a prompt.
- **`ILiveActivityService.IsSupported` changes meaning on Android** from "has widgets" to "can show a Live Update". An app with widgets but `SpineWidgetsLiveActivities=false` now reports false, which is what it always was in practice: without the permission the notification is not promoted.

## Verification

- `Plugin.Maui.Spine.Widgets` builds for net10.0-android and net10.0-ios (`/usr/local/share/dotnet`, which has the MAUI workloads).
