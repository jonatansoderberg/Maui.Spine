# Issue #360 — Accent colour: IThemeService.Accent and a picker in the sample

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/360
**Branch:** sample/accent-colour
**Status:** Completed
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

- `SpineAccent` (light/dark record, `For(theme)`, `TextOn(color)`), `IThemeService.Accent`, `SpineThemeOptions.AccentLightKey` / `AccentDarkKey` / `AccentKey` / `OnAccentKey`.
- `ThemeService`: stores the accent under `Spine.Accent` next to the theme, reads the app's own `Primary`/`PrimaryDark` before merging its dictionary, writes the four keys on attach, on every theme change and on every accent change; the dictionary is now always merged. Accent changes go through the same `Announce` path as a theme change (version bump, token copy, `Changed`, `ThemeTracker.Notify`). Merged with #355's `ThemeTracker.TakeOver()`.
- `SpineTheme.GetAccent(theme)`; Calendar and DataGrid defaults read it (DataGrid used a fixed system blue before), and the swipe action text uses `SpineAccent.TextOn`.
- `PageActionView` (header text actions) reads the accent through `GetAccent` and repaints with `SpineTheme.Track` instead of a strong `RequestedThemeChanged` subscription; its fallback for apps without `Primary` is the system blue instead of the template purple.
- Sample: `Primary` `#007AFF`, `PrimaryDark` `#0A84FF`, blue `Secondary`/`Tertiary`; styles use `{DynamicResource Accent}` / `OnAccent` (Button, CheckBox, Switch, Slider, ProgressBar, ActivityIndicator); `CardAccent` token removed in favour of `Accent`; PackageChips, TokenSwatch, Typography and Glass pages follow the accent; Android `colors.xml` + `values-night`, and an `AccentOverlay` applied in `MainActivity.OnApplyThemeResource`.
- Theme page: Accent section with ten Apple system colour swatches, a caption, sample controls and a code example.
- Docs: `docs/wiki/theming.md` accent section, calendar and page-actions notes, `/spine-setup` and `/spine-controls` skills.

Verified on the Pixel 10 Pro emulator and the iPhone 17 simulator: default blue on chips, buttons, switch, checkbox, slider, calendar; picking Pink/Orange/Purple repaints the page, chips and (after navigation back) the index and Calendar at once; the pick survives an app restart; light and dark both right; Blue resets to the default. Mac Catalyst builds.

## Decisions

- **A light/dark pair, not one `Color`.** Apple's system colours differ between modes (`#007AFF` / `#0A84FF`); deriving the dark one would not match them. `new SpineAccent(color)` gives the same colour in both.
- **Four keys.** `Primary`/`PrimaryDark` keep the MAUI template and existing readers (Calendar, header actions) working; `Accent` is the one key that follows both a theme switch and an accent change, which `AppThemeBinding` over `StaticResource` cannot do; `OnAccent` because yellow, orange, green and mint need black text. All four are options, `null` skips one.
- **`TextOn` leans to white** (perceived luma > 0.5 → black, the Calendar's existing rule) so Apple blue, red, pink, indigo and purple keep white text like the platforms' buttons, while strict WCAG contrast would put black on `#007AFF`.
- **Blue is the default swatch.** A separate "Default" swatch would have been a second identical blue circle; picking Blue sets `Accent = null`, and the caption says "the app's default".
- **null restores the app's own values** as they were when the first window was created. An app with no `Primary` at all has nothing to restore; after a reset its keys keep the last accent until restart (MAUI's `ResourceDictionary.Remove` does not notify `DynamicResource` consumers). Edge case, documented.
- **No UIWindow tint.** Setting the window tint would change system-tinted UI in every existing app that has a `Primary`; left out.
- **Native Material colours stay build-time.** Android's radio button circle comes from the activity theme (MAUI's Material 3 theme ignores `colors.xml`, hence the overlay); it shows the default blue and does not follow a runtime pick. Documented in theming.md.
