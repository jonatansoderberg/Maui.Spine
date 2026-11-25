# Issue #872 — Header bar: an icon after the page title (TitleIcon)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/872
**Branch:** issue/872-title-icon
**Status:** In Progress

## Plan
Three properties on `ViewModelBase`: `TitleIcon` (an embedded SVG name), `TitleIconColor`, `TitleIconLineWidthScale`. `PagePresenter` adds an `Image` to the title row after the label; `TitleSlotLayout` lays it out right after the title's text, centred with it.

## Changes
- `ViewModelBase.TitleIcon`, `TitleIconColor` (the title's colour when null) and `TitleIconLineWidthScale` (1).
- `PagePresenter`: the icon is the title row's second child, hidden from accessibility; it follows the title's visibility, opacity (large title collapse, search), font size and colour.
- `TitleSlotLayout`: a centred title and its icon are centred together; a left-aligned title has the icon after its text; the title truncates before the icon goes.

## Decisions
- The icon's box is 1.5 × the title's font size with no spacing: Spine's icons keep room around the glyph (a plus fills about 40 % of its box), so the glyph then stands about as tall as the title's capitals and sits a space's width from the text. Checked in Glukos (`GLUKOS +`, Brandon Grotesque Black 18, `Plus.svg` at line width 4) on the iOS simulator.
- No separate size property: the font gives it, and a title mark should match the title.
