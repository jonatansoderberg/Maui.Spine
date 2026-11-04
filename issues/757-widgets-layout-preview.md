# Issue #757 — Widgets: a preview page for layouts in the Showcase

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/757
**Branch:** issue/757-widgets-layout-preview
**Status:** Completed

Step W14 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md).

## Plan

Every change to a widget or Live Activity is a build and a run per surface. A view that draws a layout in the app at every place and size, light and dark, with `Explain()` beside it, and a Showcase page with it.

## Changes

- `Plugin.Maui.Spine.Widgets`: `WidgetPreview` (`Activity`, `Tree`, `Place`, `Family`, `Platform`, `IsDark`, `Notes`) and `WidgetPreviewPlace`. It draws from the platforms' JSON: shared nodes resolved, overrides applied for the platform and the place, stacks as grids with SwiftUI's rules for what takes the width, texts by role and size, icons through `WidgetIconAssets.Picture` (an SVG tinted to the node's colour), stored images through `IWidgetPlatform.AssetPath`, rings and sparklines (band colours, area, dot) drawn with `GraphicsView`.
- `IWidgetPlatform.AssetPath` on iOS, Android and the no-op platform.
- Sample: *Layout preview* page (`Pages/Widgets/LayoutPreviewPage`): the score's activity and widget (C#) and `Widgets/Layouts/Reading.layout.xml` (the generator now runs in the sample), iOS or Android, light or dark. `ScoreWidget.Trees` is internal for it.
- Docs: `widgets.md` "Previewing in the app"; the plan's W14 row, note and §9.

## Decisions

- **The platforms' JSON, not the C# tree:** what the preview draws has been through the same resolution and overrides as on the device, so `On(...)` and Spine's own rules show.
- **MAUI views, not the real renderers:** the SwiftUI renderer lives only in the widget extension, and RemoteViews only on Android; one MAUI drawing works on both and in the simulator. It is close, not exact, which the page says.
- **No run-time reading of layout files:** that needs an interpreter of the format beside the generator, the second implementation W15 is about; parked in §9.

## Verification

- iPhone 17 Pro simulator, iOS 26.4: the page draws the score activity (logos from the stored assets, three columns), the island's expanded, compact and minimal presentations, CarPlay, Android's notification (its text and chip, then the tree), the widget's small and medium sizes with their buttons, and the layout file's reading with the graph and its band. `docs/plans/images/w14-*.png`.
- Widgets builds for iOS and Android; the sample builds for the iOS simulator.
- Not run on Android.
