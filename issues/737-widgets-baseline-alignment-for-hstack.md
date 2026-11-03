# Issue #737 — Widgets: baseline alignment for HStack

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/737
**Branch:** issue/737-widgets-baseline-alignment-for-hstack
**Status:** In Progress

Glukos's Live Activity has the name (15 pt, bold) and the age (caption) in one row. Centred, the smaller age sits about a point above the name's baseline, which the owner saw as misaligned (2026-11-03). Making the age as large as the name was rejected: it should stay small.

## Plan

- `HStackNode.AlignBaselines()`, serialized as `align: "baseline"` on the stack (the field `CenterItems` already uses for a `VStack`).
- iOS / Mac Catalyst: `HStack(alignment: .firstTextBaseline)`.
- Android (RemoteViews): top gravity on the horizontal `LinearLayout`, whose default `baselineAligned` then shifts each text child onto the common baseline.

## Changes

- `W.cs`: `AlignBaselines()`; `WidgetNode.cs`: `Align` documents `"baseline"`.
- `SpineWidgetRenderer.swift`: an `HStack` with `align == "baseline"` aligns on `.firstTextBaseline`, otherwise `.center` as before.
- `RemoteViewsRenderer.cs`: such a stack gets `Gravity.Top`.
- `docs/wiki/widgets.md` and the `spine-widgets` skill describe it; a Dynamic Island's regions cannot share a baseline.
- Test: `A_baseline_row_keeps_its_alignment` (round trip).

## Decisions

- **Top gravity on Android, not a baseline-aligned child index:** `LinearLayout` only applies baseline alignment to children it places at the top (or bottom); centred children ignore it. Children without text (a spacer, an icon) therefore sit at the top there, while iOS lines an icon's bottom up with the baseline. Documented.
- **No island support:** the expanded regions are separate views the system places, so text in `ExpandedLeading` and `ExpandedTrailing` cannot share a baseline. Put both in one region when it matters.

## Verification

- `swiftc -typecheck` of `native/ios/*.swift` against the iOS 18 simulator SDK: no errors.
- `Plugin.Maui.Spine.Server.Tests`: 274 passed.
- Android not built locally (no MAUI workloads on this Mac); CI builds it on Windows.
