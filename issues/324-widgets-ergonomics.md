# Issue #324 — Spine.Widgets ergonomics: package images, default open target, daily timelines

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/324
**Branch:** issue/324-widgets-ergonomics
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
- Bundled pictures: the push sample copies a `MauiAsset` to the cache by hand for a notification and calls `StoreAssetAsync` on every widget build.
- Opening: Almanacka has the same `OnWidgetOpenedAsync(...) => SetRootAsync<AlmanacPage>()` in four widgets.
- Daily timelines: Almanacka's `WidgetDays` builds 60 midnight-aligned entries and rotates pictures through 64 asset slots in every widget.

### Design
1. `IWidgetService.StorePackageAssetAsync(fileName, assetId?)`: opens the package file and stores it under `assetId ?? fileName`, skipping when the stored copy's recorded length (kept in `Preferences`) matches the package file, so a fresh install copies once and a new build copies again.
2. `PackageFiles.CachedPathAsync(fileName)` in `Plugin.Maui.Spine.PushNotifications`: the cache path of a package file, copied on first use and when its size changed; what `LocalNotification.Image` needs.
3. `SpineWidgetsOptions.OpenPage` set through `options.OpenWith<TPage>()` (extension in the Widgets package, so the `INavigable` constraint stays out of Common): when a tapped widget's provider has no `IWidgetLinkHandler`, Spine calls `SetRootAsync<TPage>` on the main thread. A handler on the provider wins.
4. `WidgetTimeline.Daily(days, build, zone?)` / `AddDaily` (tree or trees-per-family): one entry per day, the first dated now, the rest at local midnight; `WidgetTimeline.DaysAhead` exposes the dates for providers that store pictures per day.
5. `WidgetAsset.Rolling(prefix, date, slots = 64)`: the rotating asset id.

Not included: an `SKImage` overload of `StoreAssetAsync`. The Widgets package does not depend on SkiaSharp and the helper is five lines in the app.

### Steps
1. Common: interface method, options property, `Daily`/`AddDaily`/`DaysAhead`, `WidgetAsset`.
2. Widgets: `StorePackageAssetAsync`, `OpenWith<TPage>()`, fallback in `TryHandleLink`.
3. PushNotifications: `PackageFiles`.
4. Push sample: `PictureWidget` and `LocalPage` use the helpers; `MauiProgram` uses `OpenWith<HomePage>()`.
5. Docs: `widgets.md` (timeline, opening, images), `push-notifications.md`; `/spine-widgets` skill note.
6. Build Common, Widgets, PushNotifications and the push sample for iOS and Android.

## Open Questions

None.

## Changes

- Common: `IWidgetService.StorePackageAssetAsync`, `SpineWidgetsOptions.OpenPage`, `WidgetTimeline.Daily` / `AddDaily` / `DaysAhead`, new `WidgetAsset.Rolling`.
- Widgets: `WidgetService.StorePackageAssetAsync` (copies once per package-file length, remembered in `Preferences`), `OpenWith<TPage>()` extension, `TryHandleLink` falls back to `SetRootAsync<OpenPage>` when the provider has no link handler.
- PushNotifications: `PackageFiles.CachedPathAsync` for bundled pictures in local notifications.
- Push sample: `PictureWidget` stores its picture with one call, `LocalPage` uses `PackageFiles`, `MauiProgram` opens Home from the picture widget through `OpenWith<HomePage>()`.
- Docs: `widgets.md` (daily timelines and rolling ids, `OpenWith`, package images), `push-notifications.md`.
- Built: Common, Widgets and PushNotifications for iOS and Android, the push sample for iOS and Android. Not run on a device: the behaviour is a refactor of what the samples already did by hand, and the widget extension needs the device setup in `ios-build-environment`.

## Decisions

- Package assets are re-copied when the file's length changes rather than never: a new build can ship a new picture under the same name, and length is the cheapest signal that survives without hashing.
- `OpenWith` lives in the Widgets package as an extension on the Common options type, so Common stays free of the navigation types.
