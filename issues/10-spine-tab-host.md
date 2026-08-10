# Issue #10 — SpineTabHost: native bottom tab navigation via [NavigableTab]

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/10
**Branch:** issue/10-spine-tab-host
**Status:** Completed

## Plan

Implementation of docs/proposals/spine-tab-host.md (rev 3). Approach in delivery order:

1. **Attribute + registry:** `NavigableTabAttribute` (Icon, Order, TabTitle + region surface, `Lifetime = Singleton` default), `TabDefaultsConfig` on `SpineOptions`, `SpineTabsOptions` host knobs. Registry exposes ordered tab list; validates duplicate `Order`, >5 tabs, and that the `x:TypeArguments` root is a tab page when tabs exist.
2. **Host:** extract the sheet-messenger + active-region logic from `SpineHostPage` into a shared coordinator; add `SpineTabbedHostPage : TabbedPage` with one thin `SpineTabPage` child per tab, each hosting a keyed `NavigationRegion`. Lazy region population on first selection; `OnAppearing`/`OnDisappearing` forwarded on tab switches. `SpineApplication.CreateWindow` picks the host from discovery.
3. **Navigation semantics:** `SwitchToTabAsync<TPage>` on `INavigationService`; `NavigateToAsync` on a `[NavigableTab]` page switches instead of pushing; re-selection pops the active tab to root (native delegate/listener hooks); Android `TabRootBackBehavior`.
4. **Native chrome:** handler mapper appends for badges (`UITabBarItem.BadgeValue` / `BadgeDrawable`), SVG tab icons through the SvgIcon pipeline, accent tint + optional `SpineTabBarStyle`, iOS 26 `MinimizeOnScroll`.
5. **Validation:** Orientera's five tabs replace the HomePage placeholder buttons; verify on iOS simulator (glass bar, per-tab stacks, back-swipe, sheet over bar, badges, safe areas).
6. **Docs:** wiki page `docs/wiki/tab-host.md`; proposal becomes historical.

Key risk (from proposal): safe-area plumbing inside `UITabBarController` — the tab bar contributes to the bottom safe-area inset and `NavigationRegion`'s iOS negative-margin counteraction must use page-level insets. Addressed first during host bring-up.

## Changes

- `NavigableTabAttribute` added as the third navigable kind (Icon, Order, TabTitle + full region surface; `Lifetime` defaults to `Singleton`). `TabDefaultsConfig` and `SpineTabsOptions` (`RootBackBehavior`, `MinimizeOnScroll`, `Style`) added to `SpineOptions`.
- `NavigationRegistry` collects an ordered `Tabs` list with startup validation (duplicate `Order`, >5 tabs); `SpineApplication` validates that `x:TypeArguments` is a tab root when tabs exist.
- Host layer refactored behind internal `ISpineHost` + `SpineHostProvider`: sheet handling extracted into shared `BottomSheetCoordinator`; `SpineHostPage` refactored onto it (no behavior change without tabs).
- New `SpineTabbedHostPage : TabbedPage` window root with one thin `SpineTabPage : ContentPage` per tab hosting a keyed `NavigationRegion`; lazy realization on first selection; `OnAppearing`/`OnDisappearing` forwarded on tab switches.
- Navigation semantics: `SwitchToTabAsync<TPage>` on `INavigationService`; `NavigateToAsync` on a tab page switches instead of pushing; `NavigateToWithResultAsync` on a tab throws; `SetRootAsync` switches+resets for tab pages and swaps to the plain host for non-tab pages (and back).
- Reselection pops the active tab to root (`NavigationRegionViewModel.PopToRootAsync`, single back transition); already at root raises new `ViewModelBase.OnTabReselectedAsync`. Native hooks: `ShouldSelectViewController` (Apple), `ItemReselected` (Android).
- `ITabBadgeService` + `TabBadgeService` rendering via `UITabBarItem.BadgeValue` / Material `BadgeDrawable`; state stored per page type and applied when the bar materializes.
- Safe areas: per-tab `TabInsetsProvider` wraps the global provider; iOS measures each tab page's real bottom safe area (includes the tab bar), Android overrides bottom to 0 (opaque bar owns the edge). `SpineTabPage` replicates `SpineHostPage`'s Android zero-padding/insets-consumer discipline.
- Tab icons rendered from embedded SVGs: density-scaled `UIImage` assigned directly to `UITabBarItem` on Apple (a stream `ImageSource` would render at scale 1), high-res bitmap via `IconImageSource` elsewhere.
- Android system back: handler now targets the active region dynamically, resubscribes on tab switches, and honors `TabRootBackBehavior.SwitchToFirstTab`.
- Orientera: all five pages converted to `[NavigableTab]` with new tab icon SVGs; placeholder navigation buttons removed; `EventDetailsPage` added to exercise push-inside-a-tab; Live-tab badge demo via `ITabBadgeService`.
- Wiki page `docs/wiki/tab-host.md` added.

## Decisions

- **`ShouldSelectViewController` property instead of a custom `UITabBarControllerDelegate`.** MAUI's renderer installs an event-backed internal delegate and relies on it for `CurrentPage` sync; replacing it throws (`Event registration is overwriting existing delegate`). Assigning the binding's delegate-property joins the same internal delegate safely.
- **Host swap via `SpineHostProvider`** rather than making `NavigationService` depend on a concrete host: enables logout→login (`SetRootAsync` to a non-tab) to replace the tab host with the plain host and back. `BottomSheetCoordinator` only responds when its host is current, so both hosts can exist without double-presenting sheets.
- **Per-tab insets provider** instead of changing the global `ISystemInsetsProvider` contract: `NavigationRegion` needed zero changes — the tab bar height simply flows through the existing inset pipeline.
- **Windows tab support is v1-minimal** (top tabs, no badges, title-bar bindings stick to the initial region). Orientera is phone-first; revisit with a `PlatformValue<TabBarPlacement>`.
- Android back handler subscriptions after a host swap still target the original host — acceptable v1 edge; noted for a follow-up if logout/login ships on Android.
- Verified on iPhone 17 Pro simulator (iOS 26.2): Liquid Glass bar, tab switching, per-tab stack preservation, push with bar visible, reselection pop-to-root, back-swipe, badge dot. Verified on Pixel emulator (dark theme): Material 3 bar with active indicator, badge dot, push, system back pops then switches to first tab.
