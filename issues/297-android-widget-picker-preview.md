# Issue #297 — Android: the widget picker shows only the app icon, no preview

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/297
**Branch:** issue/297-android-widget-picker-preview
**Status:** Completed

## Plan

Almanacka's store preflight (D2): the picker shows the app icon in an empty tile for every Spine widget. The `appwidget-provider` XML that `SpineWidgetsAndroidGenerate` writes has neither `android:previewImage` nor `android:previewLayout`, so the launcher has nothing else to show. This was reproduced with the main sample on the Pixel 10 Pro emulator (Android 17).

### a) Generated previews, Android 15+ (API 35)

In `SpineAppWidget.Update`, after the entry that applies now has been chosen, call `AppWidgetManager.SetWidgetPreview(component, AppWidgetProviderInfo.WidgetCategoryHomeScreen, views)`:

- `views` is the current entry drawn by `RemoteViewsRenderer.Root` for the smallest declared family, with no tap intent. The family comes from the provider info's `targetCellWidth`/`targetCellHeight`, which the build sets from the smallest declared family: 2×2 small, 4×2 medium, 4×4 large, 5×4 extraLarge. Its tree is picked the same way `Views` picks one: the family's own tree, else the shared one, else the first.
- `Update` also runs when no widget is placed yet. This is the case that matters, because the picker is where a user first sees the widget. Today it returns early when there are no ids. It will still skip `UpdateAppWidget`, the placeholder and the alarms then, but it will publish the preview.
- The system allows a few calls per provider per hour and returns `false` above that. To keep within that budget, a hash of what the preview shows is written to `spine-widgets/<kind>.preview`: the family, the tree's JSON and the surface's three fields. A preview whose hash has not changed is not sent again. On `false` the hash is not written, so the next `Update` tries again. The rate limit is logged once per kind and process, at info level, not as an error.
- If `SetWidgetPreview` throws (for example, a preview too large for the host), a warning is logged. The widget itself is unaffected.

Out of reach: a picture replaced under the same asset id does not change the hash, so the preview keeps the old picture until the tree or the surface changes. This goes in the docs.

`android:previewLayout="@layout/spine_widget_root"` as a fallback: rejected. That layout inflates to the empty rounded surface. On Android 12–14, `previewLayout` also takes precedence over `previewImage`, so it would hide the picture from b). On 15+ the generated preview takes precedence over both.

### b) `PreviewImage` metadata, for Android below 15

`<SpineWidget PreviewImage="Widgets/Previews/sample.png" />` is a path relative to the project. The task copies the file to `obj/spinewidgets/android/res/drawable-nodpi/spine_widget_<i>_preview<ext>`, adds it as an `AndroidResource`, and writes `android:previewImage="@drawable/spine_widget_<i>_preview"`. A missing file is a build error that names the path. The file is copied only when its bytes have changed. On Android 15+ a generated preview takes its place once the app has built the timeline; until then this picture shows.

### Docs and sample

- docs/wiki/widgets.md: `PreviewImage` in the metadata table; a *Picker preview* part under *Android*; the `<SpineWidget>` row in *How it maps*.
- `samples/MauiSpineSampleApp`: `PreviewImage` on the `sample` widget, with a screenshot of the widget taken from the emulator.
- The `spine-widgets` skill, if it lists the metadata.

### Verification

- Pixel 10 Pro emulator (Android 17) and Pixel Tablet (Android 16): the picker shows the generated preview once the app has run, including before any widget is placed.
- The generated `spine_widget_<i>.xml` carries `previewImage`, and the drawable is in the APK (`aapt2 dump`). There is no Android 12–14 image on this machine, so the picture itself cannot be seen in a picker below 15.

## Open Questions

## Changes

- `SpineAppWidget.Update` also runs when no widget is placed, on API 35+. It skips the placeholder, the remote fetch request and the alarms then, and publishes the preview.
- `SpineAppWidget.Preview` (API 35): chooses the smallest declared family from the provider info's target cells, draws that family's tree with no tap, and calls `SetWidgetPreview(component, AppWidgetCategory.HomeScreen, views)`. What the preview shows is hashed (SHA-256 of the family, tree JSON and surface fields) into `spine-widgets/<kind>.preview` (`WidgetStore.PreviewHashPath`). The hash is written only when the call succeeds. A `false` is logged once per kind and process at info level. An exception from the host is a warning.
- `SpineWidgetsAndroidGenerate`: `PreviewImage` metadata, resolved against the new `ProjectDirectory` parameter. The file is copied to `res/drawable-nodpi/spine_widget_<i>_preview<ext>` when its bytes differ, handed to the SDK as an `AndroidResource`, and written as `android:previewImage`. A missing file is a build error that names the full path.
- Sample: `PreviewImage="Widgets\Previews\sample.png"` on the `sample` widget, cropped from the generated preview on the tablet emulator.
- docs/wiki/widgets.md: `PreviewImage` in the metadata table; a new *Android → Picker preview* section; the `<SpineWidget>` row in *How it maps*. The `spine-widgets` skill mentions `PreviewImage`.
- Verified on Pixel_Tablet (Android 16):
  - Before the first launch, the picker shows the `PreviewImage`.
  - After the first launch, with nothing placed, it shows the generated preview.
  - Three more launches in two minutes were refused. Each process logged one info line and kept the accepted preview.
  - A placed widget renders as before.
  - `aapt2 dump xmltree` shows `previewImage` in the APK's `spine_widget_0.xml`, and the drawable is in `res/drawable-nodpi-v4/`.
  - A missing `PreviewImage` fails the build with the path.
- Before the fix, on Pixel_10_Pro (Android 17): the picker showed only the app icon for both sample widgets.
- Verified on Pixel_10_Pro (Android 17), after rebasing onto #296:
  - Before the first launch, *Spine sample* shows its `PreviewImage` and *Spine card* (none set) shows the icon.
  - After one launch, both show generated previews.
  - The card in its preview is centred, which also confirms #296 on Android 17: placed on the home screen, it is centred in its tile, where it sat 62 px (display) left of centre before.

## Decisions

- A hash on disk, not in memory. The budget is per provider per hour and a process is short-lived. An in-memory check would spend a call at every launch.
- The smallest declared family comes from the provider info's `targetCellWidth`/`targetCellHeight`, which the build sets from that family. So the preview matches the size the picker offers by default, even for a timeline with one shared tree.
- No `android:previewLayout`. The only layout that exists without the app's data is the empty `spine_widget_root`. On Android 12–14, `previewLayout` takes precedence over `previewImage`, so it would hide the picture from b).
- The rate limit is logged at info, once per kind and process. It is expected, not a fault, and Almanacka's four widgets rebuilding at every launch would otherwise fill the log.
