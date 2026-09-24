# Issue #329 — Plugin.Maui.Spine.Controls.Rows: Tap.Command, merged semantics and a SpineRow

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/329
**Branch:** issue/329-rows
**Status:** In Progress
**Stage:** 3 of the app-review plan (#333)

## Plan

Three pieces, split over two packages:

- **`Tap` (core, `Plugin.Maui.Spine.Extensions`)**: attached `Tap.Command`, `Tap.CommandParameter` and `Tap.HighlightColor` on any view. Whole-area hit test and native press feedback per platform, attached from the view's `HandlerChanged` so no builder call is needed:
  - iOS / Mac Catalyst: a `UIGestureRecognizer` on the platform view that recognises together with scroll views, fails on movement, ignores touches that start on a `UIControl` (a switch in the row) or inside a nested tap target, and shows a `systemFill` overlay layer after a short delay (so a scroll does not flash rows); a hover recognizer draws a lighter overlay on Mac Catalyst and iPad pointers.
  - Android: `Clickable` plus a click listener and a bounded `RippleDrawable` as the view's foreground (corner radius taken from a `Border`'s `RoundRectangle`), so press state, scroll-container delay and TalkBack activation are the platform's own.
  - Windows: `Tapped` plus pointer states driving a composition overlay (hover and pressed fills).
  - The command runs only when the view is enabled and `CanExecute` is true; `CanExecuteChanged` updates the platform state.
- **`Semantics.Merge` (core)**: children leave the accessibility tree (`AutomationProperties.IsInAccessibleTree=false`), their texts (a child's own `SemanticProperties.Description`, else a `Label`'s text) join into the container's `SemanticProperties.Description`, kept up to date as texts and children change. A `Switch` inside makes the merged element a toggle (VoiceOver toggle-button trait with its value, TalkBack checkable). A merged view with `Tap.Command` reads as a button.
- **`SpineRow` (new package `Plugin.Maui.Spine.Controls.Rows`)**: `Icon` (SVG), `Title`, `Detail` (optionally the `AnimatedLabel` marquee), `Value`, `Accessory` (any view: a switch, a button), a chevron, `Command`/`CommandParameter`. Merged semantics always on; a row whose accessory is a `Switch` and has no command toggles the switch when tapped. Colours through `SpineRowStyleOptions` (`SpineStyleOptions`) repainted with `SpineTheme.Track`. The chevron is the header bar's own back glyph (`arrowleft.svg` in the core), mirrored.
- **Registration (#355)**: none. `Tap`/`Semantics` attach from `HandlerChanged`; the Rows package has no strings.
- **Sample**: `Pages/Rows/RowsPage` (settings rows with icon, detail, value, switch, chevron, disabled row; key/value rows; a card with `Tap.Command`; a merged custom row; code). `ContextItem` rebuilt on `Tap.Command` + `Semantics.Merge`. Push sample's label+switch grids become `SpineRow`s.
- **Docs**: `docs/wiki/rows.md`, packages tables, `/spine-controls` skill, package icon.

## Open Questions

None.

## Changes

## Decisions
