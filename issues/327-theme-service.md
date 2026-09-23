# Issue #327 — IThemeService: persisted theme choice, Android background, token dictionaries, tab bar re-theming and a repaint signal

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/327
**Branch:** issue/327-theme-service
**Status:** Completed
**Stage:** 2 of the app-review plan (#332)

## Plan

### Gap
Every app repeats the same theme plumbing: a settings picker writes `Application.UserAppTheme` and forgets it at the next launch; the sample pushes a page background on Android in `RequestedThemeChanged` because the window keeps the background it was inflated with; Orientera copies a light or dark token dictionary into one merged dictionary and roots the handler because `RequestedThemeChanged` is weak; the tab bar is styled once with one colour for both themes; and every control that assigns colours in code is blind to the switch. Spine already owns the application, the window, the tab bar and the Android status bar, so the plumbing belongs there.

### Design
- **`IThemeService`** (`Core/IThemeService.cs`, implemented by `Services/ThemeService`, registered as a singleton):
  - `AppTheme Current { get; set; }`: the user's choice, `Unspecified` = follow the system. Setting it writes `Application.UserAppTheme` and, with `options.Theme.Persist` (default `true`), stores it in `Preferences` under `Spine.Theme`.
  - `AppTheme Effective { get; }`: `Light` or `Dark`, never `Unspecified`.
  - `int Version { get; }`: bumped on every change; controls compare it to repaint once on re-attach.
  - `event EventHandler? Changed`: raised on the UI thread after the token dictionary is swapped, so a subscriber reads a consistent palette.
  - `void Track(VisualElement view, Action onChanged)`: the repaint hook, with a static twin `SpineTheme.Track` for controls that subscribe in their constructor. Weak registry of subscription objects; the subscription is the target of the view's `HandlerChanged` delegate so the view keeps it alive; detached views skip the callback and catch up on re-attach when the version moved; all subscriptions are dropped when a new window is built, and a live control re-lists itself on its next handler change.
- **Startup**: the persisted choice is applied in the `SpineApplication` constructor, before the platform application handler connects, so Android's night mode is set before the activity inflates (no light flash). Token dictionaries are merged in `CreateWindow`, after the app's `InitializeComponent` has replaced `Resources`.
- **`options.Theme`** (`SpineThemeOptions`): `Persist`, `UseTokens<TLight, TDark>()` where both are `ResourceDictionary` subclasses with the same key set. The service keeps one merged dictionary and copies the right set into it on every change, so `{DynamicResource}` consumers re-resolve.
- **Android background**: `SpineApplication.Android` re-resolves `android:colorBackground` from the activity theme on every change and sets it as the window background, for the plain host as the tab host already does with `colorSurface`. The sample's `#if ANDROID` hack goes.
- **Tab bar keys**: `SpineTabBarStyle` gets `SelectedColorKey`, `UnselectedColorKey`, `BarBackgroundColorKey`, `BadgeBackgroundColorKey`, `BadgeTextColorKey`. A key is looked up in `Application.Current.Resources` at apply time; a key that resolves wins over the fixed colour. The Apple and Android hosts re-apply the style on `IThemeService.Changed` (the Android host moves its `RequestedThemeChanged` subscription to the service so the bar reads the swapped tokens).
- Spine's own theme followers (`PagePresenter` title colour, `PageActionView`, Android status bar) keep their `RequestedThemeChanged` subscriptions; they read no tokens.

### Steps
1. `IThemeService`, `ThemeService`, `SpineTheme`, `SpineThemeOptions` on `SpineOptions.Theme`; registration; startup application; tracking registry.
2. Android window background in `SpineApplication.Android`.
3. Tab bar keys and re-apply on change (Apple, Android).
4. Sample: `Pages/Theme/ThemePage` (picker bound to `Current`, `Effective` and `Version` labels, a token-coloured card via `{DynamicResource}` from `LightTokens`/`DarkTokens`, a code-drawn `GraphicsView` repainted through `Track`), Settings page uses the service, `App.xaml.cs` hack removed, index row.
5. Docs: `docs/wiki/theming.md`, README table row, `tab-host.md` key section, `/spine-page` skill note.
6. Build iOS, Android, Mac Catalyst; verify on the iPhone 17 simulator and the Pixel emulator: switch persists across a restart, Android page background follows, tokens swap, tracked view repaints, detached page catches up.

## Open Questions

None.

## Changes

- `Core/IThemeService.cs`: `Current`, `Effective`, `Version`, `Changed`, `Track`. `Core/SpineTheme.cs`: static `Track` and `Version` for constructors. `Core/SpineThemeOptions.cs` on `SpineOptions.Theme`: `Persist` (default on), `UseTokens<TLight, TDark>()`.
- `Services/ThemeService.cs`: stores the choice in `Preferences` (`Spine.Theme`), applies it in the `SpineApplication` constructor, merges the token dictionary in `CreateWindow`, roots its `RequestedThemeChanged` handler, and on a change bumps the version, copies the tokens, raises `Changed` and repaints tracked views, on the UI thread.
- `Services/ThemeTracker.cs`: weak registry of subscriptions kept alive by the view's own `HandlerChanged` delegate; attached views repaint on change, detached ones once on re-attach when the version moved; reset for every new window.
- `SpineApplication.Android`: paints the window with the theme's `colorBackground` on every change.
- `SpineTabBarStyle`: `…Key` twins for every colour, resolved from the application resources; the Apple host re-applies its style and badges on `Changed`, the Android host moved its subscription from `RequestedThemeChanged` to `Changed`.
- Sample: `Pages/Theme/ThemePage` (picker, effective/version, a `{DynamicResource}` card on `LightTokens`/`DarkTokens`, `Controls/TokenSwatch` painted in code through `SpineTheme.Track`, a code block), Settings page picks through the service, the `#if ANDROID` background hack is gone, `PackageChips` readable in dark mode, index row "Theming".
- Docs: `docs/wiki/theming.md`, README table row, key section in `tab-host.md`, notes in the `/spine-setup` skill.
- Verified on the iPhone 17 simulator (Dark: tokens swapped, swatch repainted, choice kept across a reinstall and relaunch) and the Pixel 10 Pro emulator in system dark mode (Light: page background, tokens, swatch and status bar icons follow; the choice survives a force-stop and relaunch).

## Decisions

- The stored choice is applied in the application constructor rather than in `CreateWindow`: on Android the application handler connects before the activity exists, so the night mode is in force when the window is inflated and a dark app does not flash light.
- The version is bumped before `Changed` is raised, so a subscriber that shows or records the version reads the new one; tracked repaints run after `Changed`, so options objects updated by a `Changed` handler are in place when a control rebuilds.
- Tokens are copied into one dictionary Spine owns rather than swapping merged dictionaries: `{DynamicResource}` follows a value change in a dictionary it already resolved through, but not a dictionary being removed and another added.
- `Persist` defaults to on: nothing is stored until the app sets `Current`, so an app that never offers a picker is unaffected.
