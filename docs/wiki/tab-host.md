# Tab host — bottom tab navigation

Spine's tab host gives an app N root-level bottom tabs, each owning its **own navigation stack** with preserved state when switching. The tab bar is **native platform chrome**: `UITabBarController` on iOS and Mac Catalyst (the floating Liquid Glass bar on iOS 26) and Material `BottomNavigationView` on Android — it looks, animates, and ages exactly like the platform's own apps. Inside each tab, navigation is regular Spine: regions, header bar, typed navigation, back-swipe, transitions.

---

## Platforms

| Platform | Tab bar | Badges |
|---|---|---|
| iOS 26+ | ✅ `UITabBarController` — floating Liquid Glass bar | ✅ `UITabBarItem.BadgeValue` |
| iOS < 26 | ✅ `UITabBarController` — classic translucent bar | ✅ |
| Android | ✅ Material `BottomNavigationView` (bottom placement) | ✅ `BadgeDrawable` |
| Mac Catalyst | ✅ Native Catalyst tab bar | ✅ |
| Windows | ⚠️ MAUI `TabbedPage` top tabs — functional, unpolished in v1 | ❌ no-op |

---

## Declaring tabs — `[NavigableTab]`

`NavigableTab` is the third navigable kind, alongside `[NavigableRegion]` and `[NavigableSheet]`: **a region page that additionally roots a tab**. There is no registration call — declaring the attribute *is* the registration. When the assembly scan finds one or more `[NavigableTab]` pages, Spine hosts the app in the native tab host automatically; with none, behavior is exactly the classic single-region app.

```csharp
[NavigableTab(Title = "Hem", Icon = "tab_home.svg", Order = 0)]
public partial class HomePage { public HomePage() => InitializeComponent(); }

[NavigableTab(Title = "Live", Icon = "tab_live.svg", Order = 2)]
public partial class LivePage { public LivePage() => InitializeComponent(); }
```

| Property | Meaning |
|---|---|
| `Title` | Header-bar title, and the tab label unless `TabTitle` is set. |
| `TabTitle` | Bar label when it should differ from the header-bar title. |
| `Icon` | Short SVG name (embedded resource, resolved like all Spine SVGs). Monochrome/template-style — the native bar tints selected/unselected states itself. Omit for a text-only tab. |
| `Order` | Bar position. Effectively required: assembly scan order is nondeterministic, and two tabs left at the default `0` fail startup validation as duplicates. |
| `Lifetime` | Defaults to `Singleton` (a tab's stack lives as long as the app), unlike the `Transient` region default. |

`[NavigableTab]` also carries the full region surface (`SafeAreaEdges`, `IsTitleBarVisible`, `IsHeaderBarVisible`, `TitlePlacement`, …). Inside its own stack a tab root is indistinguishable from a `[NavigableRegion]` page.

Startup validation fails fast on: duplicate `Order` values, more than 5 tabs (iOS would demote extras into a "More" item; Android guidelines cap at 5), and an app root page that is not a tab when tabs exist.

### The initial tab

The `x:TypeArguments` of your `SpineApplication` stays the single source of truth for what the app opens on — it must be one of the `[NavigableTab]` pages and decides the initially selected tab:

```xml
<SpineApplication x:TypeArguments="HomePage" ... />
```

### Defaults

`options.TabDefaults` joins `RegionDefaults`/`SheetDefaults` for values not set per-attribute. Host-level knobs live on `options.Tabs`:

```csharp
.UseSpine(options =>
{
    options.Tabs.RootBackBehavior = TabRootBackBehavior.SwitchToFirstTab; // Android back at a tab root (default)
    options.Tabs.MinimizeOnScroll = true;   // iOS 26: bar minimizes while scrolling down
    options.Tabs.Style = null;              // null = untouched native look (recommended)
})
```

---

## Navigation semantics

The attribute kind decides the verb: region pages **push**, sheet pages **present**, tab pages **switch**.

| Situation | Behavior |
|---|---|
| `NavigateToAsync<TPage>()`, `TPage` is `[NavigableRegion]` | Pushes onto the **active tab's** stack. |
| `NavigateToAsync<TPage>()`, `TPage` is `[NavigableTab]` | Switches to that tab. Stack state of every tab is preserved. A tab page can never be pushed. |
| `SwitchToTabAsync<TPage>()` | Explicit, intention-revealing tab switch. Runtime-validated to target a `[NavigableTab]` page. |
| Re-selecting the active tab in the bar | Pops that tab's stack to root (iOS convention). Already at root → `OnTabReselectedAsync()` is raised on the root's ViewModel for scroll-to-top handling. |
| `BackAsync()` / header back / back-swipe | Pops the active tab's stack only. |
| Android system back at a tab root | `TabRootBackBehavior.SwitchToFirstTab` (default) or `LeaveApp`. |
| Bottom sheet open | The sheet is a native presentation and covers the tab bar, per platform convention. Navigation routes to the sheet region exactly as without tabs. |
| `SetRootAsync<TPage>()`, `TPage` is `[NavigableTab]` | Switches to the tab and resets its stack. |
| `SetRootAsync<TPage>()`, `TPage` is a non-tab page | Replaces the whole tab host with a plain root region (logout → login), and a later tab-rooted `SetRootAsync` swaps the tab host back in. |

Tabs are **realized lazily**: a tab's region and root page are created on first selection, then kept alive — instant switching with preserved scroll positions, no startup cost for unvisited tabs.

### Lifecycle

Tab roots receive `OnAppearingAsync`/`OnDisappearingAsync` on tab switches, not only on push/pop — start live polling when a tab appears, stop when it disappears. `OnTabReselectedAsync()` fires when the already-active tab is re-selected at root.

---

## Badges

`ITabBadgeService` is injectable anywhere and renders through the native badge APIs:

```csharp
public sealed class LiveMonitor(ITabBadgeService badges)
{
    public void OnLiveStarted() => badges.SetBadge<LivePage>("");   // "" = dot
    public void OnUnread(int n)  => badges.SetBadge<EventsPage>(n.ToString());
    public void Clear()          => badges.SetBadge<LivePage>(null); // null clears
}
```

Badge state is stored per tab page type, so badges set before the tab host exists (or for a not-yet-realized tab) apply when the bar materializes. Setting a badge on a non-tab page throws.

---

## Theming

The default is the **untouched native look** — Liquid Glass on iOS 26, Material (including dynamic color / dark mode) on Android — and that is the recommended configuration. `SpineTabBarStyle` offers opt-in overrides:

```csharp
options.Tabs.Style = new SpineTabBarStyle
{
    SelectedColor = Color.FromArgb("#E8590C"),
    UnselectedColor = Colors.Gray,
    BadgeBackgroundColor = Colors.Red,
};
```

Only non-`null` properties are applied. Setting `BarBackgroundColor` on iOS 26 replaces the Liquid Glass material with a flat color — leave it `null` unless that is the intent.

---

## Safe areas inside the tab host

Spine's explicit safe-area contract extends into tabs: the **bottom inset a tab page sees includes the native tab bar**.

- `SafeAreaEdges.All` (default): content is padded above the bar — nothing to do.
- Excluding `Bottom`: content renders behind the bar (required for the full iOS 26 glass effect on scrolling content); offset your own content via `SafeAreaInsets` exactly as for system bars.

On Android the opaque Material bar owns the bottom edge and content lays out above it; the same page code works unchanged.

---

## How it works (internals)

`SpineTabbedHostPage : TabbedPage` becomes the window root when tabs are discovered — MAUI's `TabbedPage` maps to `UITabBarController`/`BottomNavigationView` without imposing any navigation model. Each tab is a thin `SpineTabPage : ContentPage` hosting a full Spine `NavigationRegion`; all pushes are Spine-virtualized inside the page, so the native side only ever sees N static children and a bar. Shell is deliberately not used: Shell owns routing, page creation, and lifetimes — the machinery Spine replaces.

Reselection is observed natively (`ShouldSelectViewController` on the controller's delegate, `ItemReselected` on the Material bar), and badges/appearance are applied directly to the native controls. See `docs/proposals/spine-tab-host.md` for the design history.
