# Issue #360 — Accent colour: IThemeService.Accent and a picker in the sample

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/360
**Branch:** sample/accent-colour
**Status:** In Progress
**Stage:** related to stage 3 of the app-review plan (#333)

## Plan

Jonatan asked for the sample's accent to be Apple's blue (the App Store icon blue) and for the Theme page to offer a choice of modern accents.

- **Framework**: `IThemeService.Accent` (`SpineAccent?`, a light/dark pair; `null` = the app's own resources). Persisted next to the theme choice when `options.Theme.Persist` is on and applied when the first window is created, before the first page. Written into the merged token dictionary under keys configurable in `SpineThemeOptions`: `Primary` (light), `PrimaryDark` (dark), `Accent` (the one for the theme in effect) and `OnAccent` (black or white text on it). Setting it runs the same path as a theme change: version bump, token copy, `Changed`, `SpineTheme.Track` repaints.
- **Reading the accent in code**: `SpineTheme.GetAccent(AppTheme)` so Calendar, DataGrid and the header-bar text actions read one definition instead of each looking up `Primary`/`PrimaryDark`.
- **Sample default**: `#007AFF` light / `#0A84FF` dark in `Colors.xaml` and Android `colors.xml` (plus a `values-night` pair). Styles switch from `{AppThemeBinding … {StaticResource Primary}}` to `{DynamicResource Accent}` / `OnAccent` so they repaint on a runtime change; the purple left in chips, tokens, Typography and Glass pages goes.
- **Theme page**: an "Accent" section with round swatches of Apple's system colours (Blue as the default, Indigo, Purple, Pink, Red, Orange, Yellow, Green, Mint, Teal), the selected one ringed, with names for screen readers and a code example.
- **Docs**: `docs/wiki/theming.md` section, `/spine-setup` and `/spine-controls` skill lines.

## Open Questions

None.

## Changes

## Decisions
