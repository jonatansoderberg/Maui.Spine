# Issue #746 — Widgets: MAUI-style names in C# (Label, Elapsed, Countdown, Spacing, TextColor, FontAttributes)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/746
**Branch:** issue/746-widgets-maui-style-names-in-c
**Status:** Completed

Step W3 of [docs/plans/widgets-everywhere.md](../docs/plans/widgets-everywhere.md), decision D12.

## Plan

New names beside the old ones, each writing exactly the JSON its old name writes; italics and two text keys as the only new wire format; stacks that skip a `null` child.

## Changes

- `W.Label`, `W.Countdown`, `W.Elapsed(since, ElapsedFormat, prefix)` with `ElapsedFormat` (`Precise`, `Clock`, `Minutes`, `Ago`) mapping to the existing `compact`, `rounded`, `ago` flags.
- `.Spacing(n)`, `.TextColor(c)`, `.Tint(c)` on icon, ring, progress and sparkline, `.FontSize(TextRole)`, `.FontAttributes(WidgetFontAttributes)` (Bold, Italic), `.MaxLines(n)`, `.ShrinkToFit(scale)`.
- `TextStyle.Italic` → `"italic"`; `TextLikeNode.MaxLines` → `"maxLines"`; `Shrink` → `"shrink"`. Defaults left out, as `bold` and `font` already were.
- `W.VStack`/`HStack`/`ZStack` take `WidgetNode?` children and skip nulls.
- iOS: `.italic`, `lineLimit(maxLines)`, `minimumScaleFactor(shrink)`. Android: italics as a `StyleSpan` on plain text, `setMaxLines`.
- Tests: `WidgetNamesTests`. Docs: the names table in `widgets.md`; the plan's W3 row.

## Decisions

- **The serializer already left out `bold: false` and `font: Body`** (since an earlier PR); the `"bold":false` measured in Puckkoll's push came from its older Spine. Nothing more to do there.
- **No `FontDesign` enum rename.** `TextDesign` stays the enum behind `.FontDesign(…)`; a second enum with the method's name would add churn without changing what the call reads like.
- **`Spacing="Evenly"` is the file format's;** in C# `.Evenly()` stays.

## Verification

- `Plugin.Maui.Spine.Server.Tests`: 294 passed (the old and new names write the same JSON).
- `swiftc -typecheck`; the Android library builds. Drawn on devices together with W1–W5 at the end of W5.
