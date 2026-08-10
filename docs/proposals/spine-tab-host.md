# SpineTabHost — bottom tab navigation for Spine (proposal, rev 3)

**Status:** Implemented — see [docs/wiki/tab-host.md](../wiki/tab-host.md) and issue [#10](https://github.com/jonatansoderberg/Maui.Spine/issues/10). Kept as design history.
**Driver:** The Orientera sample (`samples/Orientera`) requires 5 bottom tabs (Hem, Tävlingar, Live, Resultat, Jag); Spine currently has no tab concept. A tab host is also the most common missing piece for any phone-first consumer app built on Spine.
**Rev 2 change:** the tab bar is **native platform chrome** (`UITabBarController` with Liquid Glass on iOS 26, Material `BottomNavigationView` on Android) instead of a Spine-drawn bar.
**Rev 3 change:** tabs are declared with a **`[NavigableTab]` attribute** — the third navigable kind alongside `[NavigableRegion]` and `[NavigableSheet]` — instead of `options.Tabs.Add<T>()` registration. Discovery activates the tab host.

## Goals

- A first-class **root-level tab host**: N tabs, each owning its **own navigation stack** with preserved state when switching tabs.
- Same code-first, attribute-scanned, typed-navigation philosophy as the rest of Spine — no route strings, no manual wiring.
- **Native tab bar chrome**: Liquid Glass floating bar on iOS 26 (free with .NET 10 / Xcode 26 SDK), Material 3 `BottomNavigationView` on Android. The bar should look, animate, and age exactly like the platform's own apps — the same reasoning that already made Spine's bottom sheets native (`UISheetPresentationController` / `BottomSheetDialog`).
- **Badges** (count or dot) settable from anywhere via a typed service, rendered by the native badge APIs.
- **Theme support**: accent/tint colors follow the app theme and light/dark automatically; native materials (glass, Material surfaces) are left intact by default.
- Bottom sheets, header bars, transitions and back-swipe keep working unchanged.

## Non-goals (v1)

- Nested tab hosts (a tab host inside a pushed page or sheet).
- More than one tab host per app.
- Swipe-between-tabs. Deliberately excluded — conflicts with back-swipe and horizontal content gestures (same conclusion as PwosTabView in PWOS).
- Pixel-identical bars across platforms. That was rev 1's goal and is explicitly dropped: each platform gets its own native look.
- Desktop-optimized chrome. Windows/Mac get whatever `TabbedPage` renders natively there (see Platform rendering); a `PlatformValue<TabBarPlacement>` option can come later.

## Which MAUI control? Shell TabBar vs. TabbedPage

Two native-backed tab options exist in MAUI:

| | Shell `TabBar` | `TabbedPage` |
|---|---|---|
| Native iOS control | `UITabBarController` | `UITabBarController` |
| Native Android control | `BottomNavigationView` | `BottomNavigationView` (with `ToolbarPlacement.Bottom`) |
| Requires | **Shell as the app root** — Shell owns routing, page creation, lifetimes, the nav bar | Just a `Page` subclass as window root |
| Fits Spine | **No.** Spine exists to replace Shell's navigation model (typed navigation, regions, own header bar, own transitions). Hosting Spine inside Shell means two navigation systems fighting over one visual tree | **Yes.** `TabbedPage` is only chrome + child-page switching; it has no opinion about navigation inside each tab |

**Decision: subclass `TabbedPage`.** `SpineTabbedHostPage : TabbedPage` becomes the window root when tabs are declared, with one lightweight child `ContentPage` per tab, each hosting its own `NavigationRegion`. All navigation *within* a tab remains Spine's virtualized stack (content swaps inside the child page) — the native side only ever sees N static child pages and a tab bar. This is the same division of labor Spine already uses for sheets: native chrome outside, Spine regions inside.

`TabbedPage` customization happens through its handler mappers (`TabbedViewHandler` on iOS/Catalyst gives access to the `UITabBarController`; the Android handler exposes the `BottomNavigationView`), which is where badges, reselection detection, and appearance tweaks live. No custom handler from scratch — append/modify mappings on the stock handler.

## Architecture fit

Today `SpineHostPage` (singleton root `ContentPage`) hosts `RootNavigationRegion`, `SheetNavigationRegion`, and routes `INavigationService` calls via `ActiveRegionViewModel` (sheet region while a sheet is open, otherwise root region).

With tabs declared:

```
SpineTabbedHostPage : TabbedPage          ← window root; native UITabBarController / BottomNavigationView
├── SpineTabPage [tab 0] : ContentPage    ← thin wrapper, Title + IconImageSource for the tab item
│   └── NavigationRegion [tab 0]          ← own stack + header bar + back-swipe, unchanged internals
├── SpineTabPage [tab 1]
│   └── NavigationRegion [tab 1]          (region content populated lazily on first selection)
└── ...
      SheetNavigationRegion               ← unchanged; presented natively OVER everything incl. the tab bar
```

Key changes:

- The sheet-messenger handler and `ActiveRegionViewModel` logic move from `SpineHostPage` into a shared **host coordinator** used by both hosts. Resolution becomes: sheet region → **current tab's** region (`TabbedPage.CurrentPage`). Everything downstream (`INavigationService`, transitions, back-swipe, header bar) already operates on a `NavigationRegionViewModel` and needs no changes.
- When the registry scan finds no `[NavigableTab]` pages, `SpineHostPage` remains the root exactly as today — the feature is strictly additive.
- Each tab's `NavigationRegion` + `NavigationRegionViewModel` pair is created through the existing DI infrastructure (keyed by tab index), exactly like the `"BottomSheet"` keyed region today.
- `SpineApplication<TNavigable>.CreateWindow` picks the host: `[NavigableTab]` pages discovered → resolve `SpineTabbedHostPage`; otherwise → `SpineHostPage`.

### Safe-area / insets impact (the main implementation risk)

Spine owns the safe-area contract: `NavigationRegion` disables MAUI's automatic insets and applies measured `ISystemInsetsProvider` values explicitly — including an iOS **negative-margin counteraction** of the host page's safe-area offset ([NavigationRegion.cs:128](../../src/Plugin.Maui.Spine/Presentation/NavigationRegion.cs)). Inside a `UITabBarController`, child pages get a *larger* `safeAreaInsets.bottom` that includes the tab bar height, and on iOS 26 the content **must** extend under the bar — Liquid Glass needs content behind it to refract, and the scroll-edge effect depends on it.

Plan:

- `ISystemInsetsProvider` (or a per-region variant) reports the tab-bar-inclusive bottom inset for regions hosted inside a tab. `SafeAreaEdges.Bottom` pages get padded above the bar; pages that exclude the bottom edge render behind the glass and offset their own content via the existing `SafeAreaInsets` binding. No new API — the tab bar height simply flows through the existing inset pipeline.
- On iOS the negative-margin counteraction in `UpdateContainerMargin` must use the *page-level* inset, not the window-level one, so each tab's region fills its child page correctly. This is the piece to prototype first (spike below).
- Android: `BottomNavigationView` is opaque Material surface; the region's bottom inset is the nav-bar inset only (the bar sits above the system nav bar, handled by the native view). Android 15/16 edge-to-edge continues to work through the existing `SystemInsetsProvider.Android` path.

## Platform rendering

| Platform | Native control | What you get for free |
|---|---|---|
| iOS 26+ | `UITabBarController` | Floating Liquid Glass capsule bar, scroll-edge effects, optional minimize-on-scroll, automatic light/dark, SF-quality selection animation |
| iOS < 26 | `UITabBarController` | Classic translucent-blur tab bar |
| Android | `BottomNavigationView` (`ToolbarPlacement.Bottom`) | Material 3 bar (active-indicator pill with a Material 3 app theme), `BadgeDrawable`, automatic dynamic-color/dark mode |
| Mac Catalyst | `UITabBarController` | Native Catalyst tab bar; acceptable for v1 (Orientera is phone-first) |
| Windows | MAUI `TabbedPage` (top tabs) | Functional but visually plain; acceptable for v1, revisit with `PlatformValue<TabBarPlacement>` |

Optional iOS 26 nicety, exposed as an option since it's one line on the controller: `tabBarMinimizeBehavior = .onScrollDown` (bar shrinks while scrolling content). Default off in v1.

### Icons

Tab icons remain short SVG names resolved through the existing `Plugin.Maui.SvgIcon` / `SvgImageSource` pipeline, rasterized once per tab at the platform's nominal size (25 pt iOS, 24 dp Android) and assigned to `SpineTabPage.IconImageSource`. Icons must be monochrome/template-style: both platforms tint them natively (selected/unselected states come from the bar's tint, not from Spine re-rendering). SF Symbols / selected-variant icons are a possible later enhancement, not v1.

## Declaration API — `[NavigableTab]`

Spine's pages already self-describe through attributes; tabs follow the same rule. `NavigableTab` becomes the third navigable kind, in deliberate contrast to `NavigableRegion` (stack page) and `NavigableSheet` (modal sheet): **a region page that additionally roots a tab**.

```csharp
[NavigableTab(Title = "Hem", Icon = "tab_home.svg", Order = 0)]
public partial class HomePage { public HomePage() => InitializeComponent(); }

[NavigableTab(Title = "Live", Icon = "tab_live.svg", Order = 2)]
public partial class LivePage { public LivePage() => InitializeComponent(); }
```

```csharp
public sealed class NavigableTabAttribute : NavigableAttribute
{
    public required string Icon { get; init; }   // SVG name via the SvgIcon pipeline; monochrome/template-style
    public required int Order { get; init; }     // bar position — required: assembly scan order is nondeterministic
    public string TabTitle { get; init; }        // bar label; defaults to Title (header-bar title and bar label often differ)

    // Region surface carried over 1:1 (same properties as NavigableRegionAttribute):
    // IsTitleBarVisible, SafeAreaEdges — plus the shared base set (TitlePlacement, IsHeaderBarVisible, …).
    // Lifetime defaults to Singleton (a tab's stack lives as long as the app), unlike the Transient region default.
}
```

Design points:

- **`Presentation` stays `RegionPresentation`.** Inside its region a tab root is indistinguishable from any `[NavigableRegion]` page — header bar, safe-area padding, transitions, back-swipe all operate on the existing code paths with zero changes. The attribute *type* is what tells the registry "this page also roots a tab". `NavigationPresentation` gains no new member.
- **Discovery activates the feature.** The existing `NavigationRegistry` scan finds `[NavigableTab]` pages; one or more found ⇒ the window root is `SpineTabbedHostPage`, tabs ordered by `Order`. None found ⇒ `SpineHostPage`, exactly today's behavior. No `options.Tabs.Add` registration step — declaring the attribute *is* the registration, consistent with how region and sheet pages appear in the app.
- **Defaults config joins the family.** `SpineOptions` gets `TabDefaults` (a `TabDefaultsConfig`) alongside `RegionDefaults`/`SheetDefaults`, using the same set-tracking merge (`WithDefaults`) pattern the other two attributes already implement.
- **`x:TypeArguments` still picks the initial tab.** The `SpineApplication<TNavigable>` root page must be one of the `[NavigableTab]` pages when any exist (registry validates at startup with a clear error).
- **Validation at scan time:** duplicate `Order` values, and more than 5 tabs (iOS demotes extras into a "More" item, Android guidelines cap at 5 — Spine fails fast instead of inheriting either behavior silently).

Host-level knobs that aren't per-page stay in options, now config-only:

```csharp
public sealed class SpineTabsOptions   // options.Tabs
{
    public TabRootBackBehavior RootBackBehavior { get; set; }   // Android system back, see below
    public bool MinimizeOnScroll { get; set; }                  // iOS 26 tabBarMinimizeBehavior, default false
    public SpineTabBarStyle? Style { get; set; }                // null = pure native look (recommended)
}
```

Gone from rev 1: `TabBarHeight` (the platform owns the bar's geometry now) and `IsVisibleWhilePushed` (see Navigation semantics — the bar simply stays visible in v1; native hide-on-push is a v2 candidate). Gone from rev 2: `options.Tabs.Add<TPage>()`.

## Navigation semantics

| Situation | Behavior |
|---|---|
| `NavigateToAsync<TPage>()` where `TPage` is `[NavigableRegion]` | Pushes onto the **active tab's** stack (via the host coordinator, unchanged mechanics). |
| `NavigateToAsync<TPage>()` where `TPage` is `[NavigableTab]` | Switches to that tab instead of pushing. Stack state of both tabs is preserved. A tab page can never be *pushed* — the attribute kind decides the verb, mirroring how `[NavigableSheet]` pages always present as sheets. |
| `SwitchToTabAsync<TPage>()` (new on `INavigationService`) | Explicit tab switch; same as above but intention-revealing. Runtime-validated: `TPage` must be `[NavigableTab]` (attributes can't constrain generics at compile time). |
| Re-selecting the already-active tab | Pops that tab's stack to root (iOS convention). If already at root, raises `TabReselected` for scroll-to-top. Detected natively: `UITabBarControllerDelegate.ShouldSelectViewController` on iOS, `OnItemReselectedListener` on Android — wired in the handler mappings. |
| `BackAsync()` / header back / back-swipe | Pops the active tab's stack only. Back-swipe is per-region and unaffected by the tab host. |
| Android system back at a tab root | Configurable `TabRootBackBehavior`: `SwitchToFirstTab` (Android guideline, default) or `LeaveApp`. |
| Bottom sheet open | Sheet region takes over navigation exactly as today. **Changed from rev 1:** the sheet is a native presentation and covers the tab bar per platform convention (`UISheetPresentationController` presents over the tab bar; `BottomSheetDialog` dims and covers it). No custom scrim behavior. |
| `SetRootAsync<TPage>()` | Resets the **active tab's** stack when `TPage` is not a tab root; swaps the window root back to a plain `SpineHostPage` when `TPage` is not part of any tab (e.g. logout → login page). |
| Tab switch animation | Native (`UITabBarController`'s default on iOS, none/fade on Android). Spine's `ISpineTransitions` is **not** involved in tab switches — another rev 1 deletion; the platform's own switch behavior is the point of going native. |

### Lazy realization

`TabbedPage` requires all child `Page` objects up front, but they are thin shells. Each tab's `NavigationRegion` content (root page instance + view model) is populated on **first selection**, then kept alive — instant switching and preserved scroll positions (the PwosTabView lesson), without paying app-startup cost for five tabs.

## Badges

Unchanged API from rev 1, now rendered natively:

```csharp
public interface ITabBadgeService
{
    void SetBadge<TPage>(string? text) where TPage : INavigable;  // null clears; "" renders a dot
}
```

- **iOS/Catalyst:** `UITabBarItem.BadgeValue` (text) — a dot is rendered by setting a value with adjusted appearance, or `BadgeValue = "●"`-free via `UITabBarItem` small-dot convention: use `BadgeValue = ""` mapped to a dot-styled appearance. Badge colors via `UITabBarItemAppearance.Normal.BadgeBackgroundColor`.
- **Android:** `BottomNavigationView.GetOrCreateBadge(menuItemId)` → Material `BadgeDrawable`; `Number`/`Text` for counts, bare badge for the dot case. Colors via `BadgeDrawable.BackgroundColor`/`BadgeTextColor`.
- **Windows:** no native equivalent on `TabbedPage`'s top tabs — no-op in v1 (documented).

The service stores state keyed by tab page type, so badges set before the host exists (or for a not-yet-realized tab) apply when the bar materializes. Like `SwitchToTabAsync`, `TPage` is runtime-validated to be a `[NavigableTab]` page.

## Theming

Two layers, both optional — the default is the untouched native look:

1. **Accent tint (the common case).** A single accent color (selected item tint) sourced from the app's resources/theme, applied as `UITabBar.TintColor` on iOS and the item icon/text tint `ColorStateList` on Android. Light/dark switching is automatic on both platforms; `AppThemeBinding`-style dual values supported.
2. **`SpineTabBarStyle` (opt-in overrides).** Selected/unselected ink, badge background/text, and — discouraged on iOS 26 — bar background. Applied via `UITabBarAppearance` (standard + scroll-edge appearances) and `BottomNavigationView.ItemIconTintList`/`ItemTextColor`/`ItemBackground`. Setting a bar background on iOS 26 forfeits Liquid Glass; the style doc will say so and the property stays `null` by default.

MAUI's own `TabbedPage` properties (`BarBackgroundColor`, `SelectedTabColor`, `UnselectedTabColor`) exist but their handler mappings are blunt (they reset appearance objects wholesale); Spine writes appearance through its own mapper appended after the stock ones, so native materials survive.

## Lifecycle

- Tab roots receive `OnAppearingAsync`/`OnDisappearingAsync` on tab switches, not only on push/pop — required for Orientera's live polling (start when Live tab appears, stop when it disappears). Driven from `CurrentPageChanged` on the host, forwarded to the outgoing/incoming region view models.
- App backgrounding forwards to the active tab's stack only (current behavior for the single region).

## Risks & mitigations

1. **Safe-area plumbing inside `UITabBarController`** (described above) — highest risk; prototype first.
2. **MAUI `TabbedPage` maintenance state.** `TabbedPage` gets less attention than Shell in MAUI; known quirks exist (Android fragment recreation on theme change, Windows rendering). Mitigation: Spine's usage is minimal — static children, no MAUI navigation inside — which avoids most reported issues; the handler-level access we need (controller/bar instances) is stable API.
3. **Android fragment lifecycle vs. kept-alive regions.** MAUI's Android `TabbedPage` may destroy/recreate child fragment views on tab switches; Spine's regions hold their state in the view-model layer, so worst case a view re-attach occurs but stack/scroll state must be verified in the spike.
4. **`SetRootAsync` swapping the window root page** (tab host ↔ plain host on logout/login) — window `Page` swap is supported in MAUI but needs testing on all four platforms.

### Spike (before full implementation)

A minimal `TabbedPage` with two Spine `NavigationRegion` children on iOS 26 + Android, verifying: Liquid Glass appearance, content scrolling under the bar, correct `SafeAreaEdges` behavior per page, back-swipe inside a tab, native sheet presenting over the bar, badge set/clear, and tab-switch state preservation. This de-risks items 1–3 in a day-scale effort.

## Open questions

1. `INavigationService.SwitchToTabAsync` vs. separate `ITabNavigationService` — proposal: extend `INavigationService`, it is already the single navigation entry point.
2. Does `SpineApplication x:TypeArguments` stay the tab-selection mechanism, or should the attribute carry `IsInitial`? Proposal: `x:TypeArguments` — one source of truth for "what the app opens on", and it already exists.
3. `NavigableTabAttribute` implementation: `NavigableRegionAttribute` is sealed, so the region-surface properties (`IsTitleBarVisible`, `SafeAreaEdges`) are duplicated on the tab attribute rather than inherited. Alternative: unseal and derive. Proposal: duplicate — the copy-with-defaults pattern already duplicates per-attribute anyway, and sealed attributes keep the merge logic simple.
4. Hide tab bar on pushed pages (native `hidesBottomBarWhenPushed`-style)? Native mechanics differ per platform and Spine's pushes are virtual; proposal: defer to v2, bar always visible in v1 (also the iOS 26 convention — detail pages keep the floating bar).
5. iOS 26 `UITabBarController` search-tab / accessory-view features — out of scope, revisit if Orientera needs them.

## Verified baseline (2026-08-10)

Tested on iPhone 17 Pro simulator (iOS 26.2) with the Orientera scaffold: Spine region push/pop, header bar + back button and the interactive back-swipe gesture all work on iOS today. The tab host builds on exactly these verified primitives — one `NavigationRegion` per tab, now wrapped in native tab chrome.

## Suggested delivery

1. Spike (above) on a scratch branch — confirms the native approach before any framework surgery.
2. GitHub issue + `issues/<id>-spine-tab-host.md` changelog per repo convention.
3. Extract the host coordinator from `SpineHostPage` (no behavior change), then implement `SpineTabbedHostPage` behind `options.Tabs` (empty ⇒ zero behavioral change).
4. Wire Orientera's five tabs as the real-world validation (replaces the placeholder buttons on HomePage).
5. Wiki page `docs/wiki/tab-host.md` on completion; this proposal then becomes historical.
