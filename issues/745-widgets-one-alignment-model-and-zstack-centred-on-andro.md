# Issue #745 — Widgets: one alignment model, and ZStack centred on Android

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/745
**Branch:** issue/745-widgets-one-alignment-model-and-zstack-centred-on-andro
**Status:** Completed

Step W2 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D2.

## Plan

- `WidgetAlignment` for a stack across its axis, written in the existing `align` key (`start`, `center`, `end`, `baseline`, `topStart` … `bottomEnd`), so old renderers keep reading `center` and `baseline` and ignore the rest.
- `WidgetLayoutOptions` (`start`, `center`, `end`, `fill`) for one child, in a new `hAlign` key.
- `WidgetTextAlignment` writing the existing `centered` / `trailing` keys.
- `.Expand()` beside `.Fill()`; `CenterItems` and `AlignBaselines` become `.Alignment(…)`.
- iOS: VStack/HStack/ZStack alignments from `align`; a VStack child with `hAlign` in a full-width frame.
- Android: `setGravity` on the stack; a ZStack's layers and a placed child in thin `LinearLayout` frames whose gravity places them, since RemoteViews cannot set `layout_gravity`.

## Changes

- `Core/WidgetAlignment.cs`: `WidgetAlignment`, `WidgetLayoutOptions` (string values via `JsonStringEnumMemberName`), `WidgetTextAlignment`.
- `WidgetNode.HorizontalOptions` (`hAlign`); `.Alignment`, `.HorizontalOptions`, `.HorizontalTextAlignment`, `.Expand` in `W.cs`; `CenterItems`/`AlignBaselines` through `.Alignment`. `.Alignment` refuses Baseline outside an HStack and the corners outside a ZStack.
- `SpineWidgetRenderer.swift`: `Align` (horizontal, vertical, both, options); the stacks use it; a VStack child with `hAlign` is framed across the width.
- `RemoteViewsRenderer.cs`: `Gravity`, `LayerGravity`, `Place`, `Wrap`; layouts `spine_widget_align_layer.xml`, `spine_widget_align_row.xml`; `spine_widget_zstack.xml` wraps its content, and a ZStack with a background fills the width (Android 12+).
- Tests: `WidgetAlignmentTests`.
- Docs: `widgets.md` (alignment table, the Android notes); the plan's W2 row and a refinement of D2.

## Decisions

- **No `VerticalOptions`, and `HorizontalOptions` only in a VStack.** SwiftUI places a child of an HStack or a ZStack only with a frame that grows the whole stack (`maxHeight: .infinity`), which would change the layout around it. Both platforms keep to the stack's alignment there, so they agree.
- **A ZStack wraps its content on Android.** Found on the emulator: with its layers centred, a full-width ZStack put a ring in the middle of the row, where iOS puts it at the leading edge. It now takes its content's width, and the full width only with a background, as SwiftUI's box does.
- **A behaviour change on Android:** a ZStack's layers used to sit at the top left (no gravity); they are now centred, as on iOS. None of Glukos, Puckkoll or Almanacka uses a ZStack.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 288 passed. `swiftc -typecheck`; the Android library builds.
- Pixel 10 Pro emulator (Android 17), Glukos drawn with a temporary ZStack (a ring with "60%" in it), a centred and an end-placed caption: the text centred in the ring, the ring at the leading edge, the captions in the middle and at the end. iOS not run.
