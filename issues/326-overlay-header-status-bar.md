# Issue #326 — HeaderBarMode.Overlay and per-page StatusBarStyle

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/326
**Branch:** issue/326-overlay-header-status-bar
**Status:** Completed
**Stage:** 2 of the app-review plan (#332)

## Plan

### Gap
The sample, Almanacka and Orientera each re-draw Spine's header to get content behind it (hand-computed header heights, the Windows caption-button width, a copied 44-point row with a forced white title, `HeaderBarConstants` copied because they are internal), and Almanacka drives the status bar with a deprecated API that fights Spine's own Android handling.

### Design
Three attribute properties with defaults in `RegionDefaults`/`SheetDefaults`/`TabDefaults`, mirrored to `ViewModelBase`:
- `HeaderBar = HeaderBarMode.Overlay`: the region does not pad the content host at the top; `PagePresenter` spans the content over both rows and pushes the title row down by the status bar; `SafeAreaInsets.Top` becomes status bar plus header height so `SafeArea.ScrollInset="Top"` keeps a list clear of the bar. The action bar already floats in the region, so it needs nothing.
- `HeaderBarForeground` (hex): `HeaderBar.Foreground` → `PageActionView.Foreground` (text colour and SVG tint) and the presenter's title colour.
- `StatusBarStyle`: a `StatusBar` helper applied when a region page (not a sheet) becomes current and re-applied on theme change. Android sets `AppearanceLightStatusBars`; iOS uses `UIApplication.SetStatusBarStyle` when `Info.plist` has `UIViewControllerBasedStatusBarAppearance = false`, and logs a hint otherwise, because MAUI's page controllers offer no per-controller override.
- `HeaderBarConstants` becomes public.

### Steps
1. Enums, attribute/defaults plumbing, view model properties, `NavigableMeta`.
2. Region padding and insets aware of the overlay; presenter layout; header and action foreground.
3. `StatusBar` helper with Android and iOS implementations; theme-change re-apply on Android.
4. Sample "Overlay header" page on the hero photo with a glass bell action; sample Info.plist gets the iOS key.
5. Docs in `regions.md`; `/spine-page` skill.
6. Build iOS, Android, Mac Catalyst; verify on both devices.

## Open Questions

None.

## Changes

- `Core/HeaderBarMode.cs` (new): `HeaderBarMode { Normal, Overlay }` and `StatusBarStyle { Default, LightContent, DarkContent }`.
- `NavigableAttribute` (+ copy-with-defaults) and `NavigableDefaults`: `HeaderBar`, `HeaderBarForeground` (hex string), `StatusBarStyle`; `ViewModelBase` mirrors them as `HeaderBarMode`, `HeaderBarForeground` (`Color?`) and `StatusBarStyle`; `NavigableMeta` fills them and computes `SafeAreaInsets` through `NavigationRegion.SafeAreaInsetsFor`.
- `NavigationRegion`: `ApplySafeAreaPadding`/`SafeAreaInsetsFor` take the view model; an overlay page is never padded at the top and reports status bar plus header height as its top inset; binds `HeaderBar.Foreground`; applies `StatusBar` when a region page becomes current (a sheet keeps the page's).
- `PagePresenter`: watches the page for `HeaderBarMode`, `SystemBarInsets`, `HeaderBarForeground`, `IsHeaderBarVisible`; in overlay mode the content spans both rows, the title row grows by the status bar and the title sits over the content (`ZIndex`) in the foreground colour.
- `HeaderBar.Foreground` → `PageActionView.Foreground`: text colour and SVG tint follow it.
- `Core/StatusBar.cs` + `Platforms/Android/StatusBar.Android.cs` + `Platforms/iOS/StatusBar.iOS.cs`: Android sets `AppearanceLightStatusBars`; iOS uses the application-level style behind the `UIViewControllerBasedStatusBarAppearance = false` flag and logs a hint without it. `SpineApplication.Android` re-applies the current style on theme change.
- `HeaderBarConstants` is public.
- Sample: `Pages/Overlay/OverlayPage` (photo under status bar and header, white title and glass bell, `SafeArea.ScrollInset="Top,Bottom"` list with a header that starts under the photo); the sample's iOS `Info.plist` gets the status bar key. Listed as "Overlay header".
- Docs: "Overlay header" section and three table rows in `regions.md`; `/spine-page` skill.
- Verified on iPhone 17 (iOS 26.4): content starts at the top of the screen, the title is centred on the actions' row in white over the photo, the status bar is light, the first row is clear of the header and the photo shows through the bar. On the Pixel 10 Pro emulator the title alignment, the white glyphs and the light status bar were confirmed on the build before the last sample-only change; the final run was cut short by the emulator thrashing (1.3 GB of swap in use, ANRs on launch) rather than by the app.

## Decisions

- The action bar was already an overlay in `NavigationRegion`; only the title row (inside the page content) and the content padding had to learn the mode, which is why the change is small for what it does.
- The status bar style stays with the region page under a sheet, because a sheet is a card over that page and its top is still the page's.
- The title row keeps its own height plus the status bar in overlay mode instead of a margin inside a fixed row: with the margin the title was squeezed half a row up on both platforms.
- iOS keeps the deprecated application-level API behind the plist flag rather than swapping MAUI's page controller: the flag is what CommunityToolkit's status bar behavior requires too, and the alternative reaches into MAUI's handler internals.
