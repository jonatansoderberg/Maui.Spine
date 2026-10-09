# Search in the header bar (study, rev 1)

**Status:** Study, with the owner's decisions from 2026-09-30 in the section [Decisions](#decisions-2026-09-30). Step 3 (the API and the field at the top, without Windows' title bar and suggestions) and step 6 are implemented in #307; see `docs/wiki/search.md` and `issues/307-feature-idea-search-in-the-header-bar.md`. Windows' title bar (`TitleBar.Content`, without suggestions) followed in #480 (`issues/480-windows-titlebar-search.md`), compiled but not yet run on Windows (#501). Issue: [#307](https://github.com/jonatansoderberg/Maui.Spine/issues/307), prioritized as P2 in [#317](https://github.com/jonatansoderberg/Maui.Spine/issues/317), depends on [#269](https://github.com/jonatansoderberg/Maui.Spine/issues/269).
**Question:** Should the search field on a Spine page be drawn by Spine (like `HeaderBarView`) or be UIKit's own (`UISearchController`), given that iOS 26 has moved search to the bottom edge of the screen, and how does a page declare search?
**Answer:** Spine draws it. `UISearchController` ends up in the bottom toolbar only when its `UINavigationItem` sits in a `UINavigationController` with a toolbar. A Spine app has none: every page lives in a MAUI `ContentPage`, and the header bar is a MAUI `Grid`. The native route would require Spine to hide a `UINavigationController` behind its own header bar and swap `searchController` on every virtual navigation. That is more code and more risk than drawing the field itself. Spine instead draws a glass capsule (`Material.Kind="Glass"` from #300) around MAUI's `SearchBar`, which already provides the platform's return key and clear button on iOS, Android and Windows. The placement follows the HIG: at the bottom on iPhone with iOS 26 when the bottom edge is free, as a search button in the header bar when a tab bar or a footer takes the bottom edge, and at the top on all other platforms. The page declares search the way it declares page actions, with an attribute on a property in the view model. The search tab in the iOS 26 tab bar (`UISearchTab`) becomes an issue of its own, because MAUI's `TabbedPage` builds the tabs with `ViewControllers` and not with `UITab`.

---

## 1. The conclusion in short

| Question | Answer | Evidence |
|---|---|---|
| Spine-drawn or `UISearchController`? | **Spine-drawn.** The toolbar placement requires the navigation bar to belong to a `UINavigationController` ("On iPhone, when the navigation bar belongs to a UINavigationController, the search bar may be integrated into the toolbar"). Spine has none: `SpineHostPage` and `SpineTabPage` are `ContentPage`, and no `UINavigationController` appears in the package. | Apple docs, `SearchBarPlacement.integrated`; `SpineHostPage.cs:13`, `SpineTabPage.cs:8`; §3.1, §4 |
| Where does the field go on iOS 26? | **At the bottom** for a region page with no tab bar and no footer: a glass capsule that rests above the home indicator and moves up above the keyboard when it is focused. **In the header bar as a search button** when the bottom edge is taken (tab bar, footer). The button expands into a field above the keyboard, as the HIG describes for a search button in the top toolbar. **At the top** in sheets. | HIG *Search fields*, §5 |
| And on the other platforms? | **At the top**, in a row below the header bar: iOS 15–18 like `UISearchController` in *stacked* mode, Android like Material 3's search field, and iPad/Mac Catalyst at the far right of the header bar. On Windows the field goes in MAUI's `TitleBar.Content`, as an `AutoSuggestBox`. | §3, §8 |
| Which control is in the field? | **MAUI's `SearchBar`.** It is `UISearchBar` on iOS, `SearchView` on Android and `AutoSuggestBox` on Windows. The "Search" return key, the clear button and submit (`SearchButtonClicked`/`QuerySubmitted`) come from it. | `SearchBarHandler.iOS.cs:18`, `.Android.cs:25`, `.Windows.cs:12` (MAUI `main`) |
| How does a page declare search? | **`[PageSearch]` on a `string` property in the view model**, the same way `[PageAction]` sits on a command. Spine then creates an observable `PageSearch` on `ViewModelBase.Search`, which the page can change while it is shown. The issue's proposal with `ISearchable` and `[NavigableRegion(Search = true)]` splits the same thing across two places, the page class and the view model. | `PageActionDiscovery.cs:13`, §6 |
| The keyboard? | A field at the bottom **is** the #269 problem. Spine has to know the keyboard's height to lift the field, and the result list has to get the same inset. The framework-level observer that #269 proposes is therefore delivered first, and search builds on it. | §7 |
| `UISearchTab` (the search tab in iOS 26)? | **An issue of its own.** It requires `UITabBarController.Tabs` (the `UITab` API), but MAUI's `TabbedRenderer` sets `ViewControllers`. It is a search for the whole app, not for a single page. | `TabbedRenderer.cs:383` (MAUI), `SpineTabbedHostPage.Apple.cs:66` |
| Do the APIs exist in .NET? | **Yes**, in Microsoft.iOS 26.2 (the tag `dotnet-10.0.1xx-xcode26.2-10217`): `SearchBarPlacementAllowsToolbarIntegration`, `SearchBarPlacementBarButtonItem`, `UINavigationItemSearchBarPlacement.IntegratedButton`, `UISearchTab.AutomaticallyActivatesSearch`, `UITabBarController.BottomAccessory` and `UIView.KeyboardLayoutGuide`. | `src/uikit.cs` in dotnet/macios, §3.1 |

---

## 2. What Spine already has

| Part | Where | What it means for search |
|---|---|---|
| The header bar is a MAUI view | `HeaderBarView.cs:12`, a `Grid` with five columns and two `PageActionView` (`:422–445`), laid on top of the page in `NavigationRegion.cs:90–110` | There is no `UINavigationBar` and no `UINavigationItem` to attach a `searchController` to. Placement and animation have to come from Spine, just as the issue says. |
| One action per side of the bar | `NavigationRegionViewModel.cs:155` (`PrimaryPageAction`), `:183` (`SecondaryPageAction`) | A search button in the header bar competes for the right-hand slot. Either the bar gets a third slot, or the search button takes precedence over the page's own actions (§9 item 5). |
| The title row belongs to the page | `PagePresenter.cs:66–68` (rows: title, content, footer), `ApplyTitleRowHeight` `:428` | A search row below the title on Android, iOS 15–18 and in sheets becomes a fourth row in the same presenter. It moves with the page during the transition, as the title does. |
| Footer slot at the bottom | `SpinePage.Footer` (`SpinePage.cs:90`), `_footerHost` (`PagePresenter.cs:98–100`) | The capsule that rests at the bottom on iOS 26 lives next to the footer. A page with a footer gets the search button instead (§5). |
| The safe-area contract | `NavigationRegion.cs:64` (`SafeAreaEdges = None`), `ApplySafeAreaPadding` `:235`, `SafeAreaInsetsFor` `:272` | The search row and the capsule have to be counted into `SafeAreaInsets`, the way `BarHeight` is counted in under a floating header. |
| The scroll source and scroll edge | `HeaderBar.ScrollSource` (`HeaderBar.cs:31`), `UIScrollEdgeElementContainerInteraction` (`PagePresenter.Apple.cs:83`) | The same mechanism with edge `Bottom` gives the iOS 26 soft edge behind the capsule at the bottom. The list gets `ScrollInset` `Bottom`. |
| Glass surfaces | `MaterialKind.Glass` (`Material.cs:20`), `UIGlassEffect` and capsule corners (`MaterialExtensions.Apple.cs:176–190`, `:405`) | The capsule around the field needs no new code for the glass. |
| Glass buttons in the bar | `PageActionView.UseGlassHeaderActions` (`PageActionView.cs:93`), `SpineOptions.Apple.GlassHeaderActions`/`MorphHeaderActions` (`SpineOptions.cs:347`, `:355`) | The search button is an ordinary glass page action with the magnifying glass. The close button next to an active field is a glass circle of the same kind. |
| Header margin and height | `HeaderBarConstants.PageMargin` (`:27`), `BarHeight` (`:184`) | The capsule's side margin and the search row's height should come from there and not be new numbers. |
| Menus | `PageAction.Menu`, `MenuPicker` (#318, `MenuButton.cs`) | Search scopes can be built as a menu button next to the field instead of with UIKit's scope bar (§9 item 4). |
| Windows title bar | `SpineApplication.Windows.cs:77–86`: MAUI's `TitleBar` with `LeadingContent`/`TrailingContent` | `TitleBar.Content`, the center slot, is unused. That is where Windows apps put their search box. |
| Keyboard | Only in Android's sheet: `SoftInput.AdjustResize` (`BottomSheetPageExtensions.Android.cs:155`) and IME insets (`:616`). On iOS there is nothing. | That is #269. |
| The tab bar | `SpineTabbedHostPage : TabbedPage`. On iOS it is MAUI's `UITabBarController` (`SpineTabbedHostPage.Apple.cs:14`), driven through `ViewControllers` (`:66`, `:88`) and with `TabBarMinimizeBehavior` (`:41–42`). | A search tab requires the `UITab` API (§3.1, §4 C). |

So what is missing is the field, its placement and the keyboard. The page structure, the glass and the scroll edge effect are already there.

---

## 3. The platforms' building blocks

### 3.1 iOS 26 (iPhone)

The HIG (*Search fields*, iOS) names three places: **as a tab in the tab bar**, **in a toolbar at the bottom or the top**, and **inline with the content**. About the toolbar it says: "Place search at the bottom if there's room … Place search at the top when … there's no bottom toolbar." A search button in the top bar "animates into a search field that appears either above the keyboard or at the top if there isn't space at the bottom". An active field therefore ends up above the keyboard in both cases. What differs is where it rests when it is not in use.

UIKit's route there:

- `navigationItem.searchController` with `preferredSearchBarPlacement = .integrated` (new in iOS 26, same raw value as `.inline`). Apple: "On iPhone, when the navigation bar belongs to a UINavigationController, the search bar may be integrated into the toolbar."
- `searchBarPlacementAllowsToolbarIntegration` (default `true`) and `searchBarPlacementBarButtonItem`, which says where in `toolbarItems` the field should stand. Without it the field ends up at the far right of the `UIToolbar`.
- `.integratedButton` always shows search as a button until it is activated.
- `UISearchTab` (iOS 18) with `automaticallyActivatesSearch` (iOS 26): the tab sits by itself at the far right of the tab bar and expands into a search field. According to WWDC25 session 284 the other tabs collapse at the same time. When the search is canceled, the tab that was selected before is restored.
- `UITabBarController.bottomAccessory` (`UITabAccessory`) is a view above the tab bar. It is meant for something like a player, not for search, and is not proposed here.

All the names above exist in Microsoft.iOS 26.2 (see §1). **None of it sits in a Spine app:** there is no `UINavigationController`, and the tab bar is built by MAUI without `UITab`.

Forum thread 797701 collects four bugs in `UISearchController` on iOS 26 that concern the placements `integrated`/`integratedButton` and scope buttons. According to the thread they were still there in iOS 26.5 and Apple has not replied there. That is one reason not to tie Spine's search to that route.

### 3.2 iOS 15–18

The old pattern is `UISearchController` in *stacked* mode: a field below the title that slides away when the list is scrolled (`hidesSearchBarWhenScrolling`). It also requires a `UINavigationBar`. Spine draws the equivalent as a row below the title row with an ordinary `UISearchBar`. Spine's minimum is iOS 15 (`Directory.Build.props:22`).

### 3.3 iPad and Mac Catalyst

HIG: "Put a search field at the trailing side of the toolbar." The Mac pattern is a field at the far right of the toolbar, and on iPad it is the same. Spine puts the field at the far right of the header bar. It then takes the right-hand action slot, or stands to the left of it if there is room. ⌘F to focus the field requires a `UIKeyCommand` on the host's view controller. Spine does not have that today and it is not part of v1. `searchBarPlacementAllowsExternalIntegration` applies only inside a `UISplitViewController` and is not relevant.

### 3.4 Android

Material 3 has `SearchBar` ("a persistent and prominent search field at the top of the screen") and `SearchView` (a full-screen view that expands out of `SearchBar`, with room for history and suggestions). Both require Material Components 1.8 or later, and MAUI 10 has 1.12. They are built for a `CoordinatorLayout`/`AppBarLayout`, or for `SearchView.setUpWithSearchBar` without one. Spine's header bar is neither. The proposal is therefore the same as for iOS 15–18: a Spine-drawn capsule in M3 shape at the top (fully rounded, the *surface container high* surface color) with MAUI's `SearchView` inside. The return key then goes through `ImeAction.Search`. Expanding to full screen, as `SearchView` does, is a later step if the suggestion list requires it.

### 3.5 Windows

MAUI's `SearchBar` is already an `AutoSuggestBox`, with `QuerySubmitted`. Suggestions (`ItemsSource`, `SuggestionChosen`) are not exposed by MAUI and are set by Spine through a handler mapping, the same shape as the menus in #318. The place is `TitleBar.Content` on the desktop, in the middle of the title bar as in File Explorer and Settings. Spine already sets `LeadingContent`/`TrailingContent` there.

---

## 4. The alternatives

| | A. Spine-drawn field (MAUI `SearchBar` in a glass capsule) | B. `UISearchController` in a hidden `UINavigationController` | C. `UISearchTab` in the tab bar | D. The field in `bottomAccessory` |
|---|---|---|---|---|
| At the bottom on iOS 26 | Yes, Spine places it | Maybe. Requires a `UINavigationController` + a visible `UIToolbar` but a hidden navigation bar. Whether UIKit integrates the field into the toolbar when the navigation bar is hidden is **not known**. | Yes, but only as the app's search tab | Yes, above the tab bar |
| Follows Spine's navigation | Yes: the field belongs to the page and moves with it in the transition | No: one `navigationItem` for the whole host page, which is swapped on every virtual push and is not animated in step with Spine's transition | Not applicable | No: one for the whole tab host |
| Sheets | Yes | No: the sheet is a MAUI region of its own | No | No |
| iOS 15–18, Android, Windows | Same API and code, with a different placement | iOS 26 only. Everything else has to be built anyway. | iOS only | iOS 26 only |
| Keyboard | Spine lifts the field (#269) | UIKit lifts it | UIKit | UIKit |
| Risk | The look has to be matched against the native one (a spike, like #377) | Known bugs (forum 797701), and MAUI's own safe-area handling around a new container | MAUI's renderer sets `ViewControllers` and syncs `CurrentPage` through its delegate. Switching to `Tabs` happens underneath it. | Goes against the intent of the HIG |

**Recommendation: A.** It is the same trade-off that was made for the header bar and the glass buttons. Spine owns the navigation, so Spine has to own what moves with it. B gives the native field in only one of five cases, and only with a hidden container that nobody in the repo has tried. C is a good addition but answers a different question: a search for the whole app, not search on a page. D is not recommended.

---

## 5. The placement rule (`SearchPlacement.Automatic`)

| Context | iPhone, iOS 26 | iPhone, iOS 15–18 | iPad, Mac Catalyst | Android | Windows |
|---|---|---|---|---|---|
| Region page with no tab bar and no footer | **Capsule at the bottom**, full width within `PageMargin`, over the content with soft edge `Bottom`. Active: above the keyboard, with a glass circle to close on the right. | Row below the title | At the far right of the header bar | Capsule in a row below the header bar | `TitleBar.Content` (desktop), otherwise a row below the header bar |
| Tab root page or page in a tab (the tab bar is visible) | **Search button** (magnifying glass) on the right of the header bar. A tap opens the field above the keyboard. | Row below the title | As above | As above | As above |
| Page with a `Footer` | Search button, as above | Row below the title | As above | As above | As above |
| Sheet | Row below the header bar | Row below the header bar | Row below the header bar | Row below the header bar | Row below the header bar |

`SearchPlacement.Top` forces the row below the header bar and `SearchPlacement.Button` forces the search button, on all platforms. Sheets get the field at the top because the bottom edge moves with the detents. On Android the sheet's content is also offset (`SetSheetOverhang`), which a field at the bottom would have to follow. A native example of search at the bottom of a sheet (Maps) has not been examined.

The row below the header bar is counted into the page's top inset just like `BarHeight`. Under `HeaderBarMode.Overlay` and `LargeTitle` it floats with the bar. Whether the row should slide away on scroll, as *stacked* does, is settled in step 3 and is not in v1 (§9 item 6).

---

## 6. Proposed API surface

It follows `PageAction`: an attribute in the view model becomes an observable model that the page can change while it is shown.

```csharp
namespace Plugin.Maui.Spine.Core;

public enum SearchPlacement { Automatic, Top, Button }

/// <summary>Declares the page's search field on the string property that holds its text.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PageSearchAttribute : Attribute
{
    public string? Placeholder { get; init; }
    /// <summary>Name of an ICommand run on the return key or a picked suggestion, with the text as parameter.</summary>
    public string? Submit { get; init; }
    /// <summary>Name of an IEnumerable&lt;string&gt; property shown under the field while it is active.</summary>
    public string? Suggestions { get; init; }
    public SearchPlacement Placement { get; init; }
}

/// <summary>The page's search field, as the header bar shows it. Observable, like PageAction.</summary>
public sealed partial class PageSearch : ObservableObject
{
    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string? Placeholder { get; set; }
    [ObservableProperty] public partial SearchPlacement Placement { get; set; }
    /// <summary>Whether the field has focus. Set it to open or close search from code.</summary>
    [ObservableProperty] public partial bool IsActive { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial IEnumerable<string>? Suggestions { get; set; }
    public ICommand? SubmitCommand { get; init; }
}

// ViewModelBase
public partial PageSearch? Search { get; set; }   // set by discovery, or by hand
```

A page:

```csharp
public partial class CompetitionsViewModel(ICompetitionService _competitions) : ViewModelBase
{
    [PageSearch(Placeholder = "Sök tävling", Submit = nameof(OpenFirstCommand), Suggestions = nameof(Recent))]
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    public ObservableCollection<string> Recent { get; } = [];

    partial void OnQueryChanged(string value) => Filtered = _competitions.Filter(value);

    [RelayCommand]
    private Task OpenFirst(string text) => /* ... */;
}
```

```xml
<!-- XAML: nothing. The list the page already has is what search filters. -->
<CollectionView ItemsSource="{Binding Filtered}" />
```

Why this way:

- **The text is the view model's own property.** `partial void OnQueryChanged` is the way to react that toolkit apps already use. Spine binds the field two-way to the property through `PageSearch.Text`. With the issue's `ISearchable`, the view model would instead have to use the interface's names and raise `PropertyChanged` for `Query` itself.
- **One place.** `[NavigableRegion(Search = true)]` sits on the page class and `ISearchable` on the view model, so two declarations for one thing. `[PageAction]` has shown that the view model is enough. The issue's sketch has the attribute on the view model, but `NavigableAttribute` sits on the page (`NavigableAttribute.cs:28`).
- **Spine draws no results.** The page filters its own list. `UISearchController` switches to a results view (`searchResultsController`), but that model does not suit a page that already is its list, and the three use cases in the issue (Orientera #94, Puckkoll, Almanacka) are all such pages.
- **Suggestions are a list of strings** shown below the field while it is active. Picking one sets the text and runs `Submit`. On Windows they go straight into `AutoSuggestBox.ItemsSource`.
- **No scopes in v1.** See §9 item 4.
- Discovery happens in `PageActionDiscovery`, or in a sibling class, and runs once per view model (the `DeclaredActionsAdded` pattern).

---

## 7. The keyboard (#269)

The field at the bottom on iOS 26 is exactly what #269 describes: Almanacka has its name-day field at the bottom on iOS, and it ended up under the keyboard. The app solves that today with `UIKeyboard.Notifications.ObserveWillChangeFrame` in the page. Search needs three things that #269 also needs:

1. **The keyboard's overlap in the region's coordinates**, animated with the keyboard's curve and duration. On iOS it comes from the `UIKeyboard` frame notifications. `UIView.KeyboardLayoutGuide` (iOS 15, bound) is an alternative for a native view, but the field is a MAUI view in a MAUI `Grid`, so the notification with the curve is the closest fit. On Android it comes from `WindowInsetsCompat.Type.Ime()` through the existing `SystemInsetsProvider`, with `WindowInsetsAnimationCompat` if the field is to follow along during the animation.
2. **The capsule's lift:** `TranslationY = -(overlap - bottomSafeArea)` while the field is active.
3. **The list's inset:** the bottom `ScrollInset` becomes *capsule + overlap*, so that the last row can be reached above both the keyboard and the field.

The proposal is that #269's framework variant (the observer in `NavigationRegion`, the inset on `ViewModelBase`) is delivered first, as step 1, and that search reads the same value. Almanacka's workaround then goes away twice over: both through #269 and because the field becomes Spine's. On Android, on Windows and with the field at the top, the keyboard is only the list's problem, that is item 3.

---

## 8. Platforms

| Platform | Support | Building block | Note |
|---|---|---|---|
| iOS 26+ (iPhone) | Full | Glass capsule (`MaterialKind.Glass`) + `UISearchBar` through MAUI, at the bottom or as a search button | Soft edge `Bottom` through `UIScrollEdgeElementContainerInteraction` |
| iOS 15–18 | Full | `UISearchBar` through MAUI in a row below the title | No glass |
| iPadOS | Full | Field at the far right of the header bar | Compact width (Split View) like iPhone |
| Mac Catalyst | Full | Same as iPad | ⌘F in a later step |
| Android | Full | M3-shaped capsule + `SearchView` through MAUI, at the top | M3's full-screen `SearchView` is a possible later step |
| Windows | Full | `AutoSuggestBox` through MAUI in `TitleBar.Content`, with suggestions through a mapping | Row below the header bar when the title bar is hidden |
| iOS 26 search tab (`UISearchTab`) | Not included | An issue of its own | Requires `UITab` instead of MAUI's `ViewControllers` |

---

## 9. What cannot be done, and what is not verified

The study was done in a Linux container with no Mac, simulator or device. **Nothing has been tried.** The API names are checked against Apple's documentation and against `src/uikit.cs` in dotnet/macios at the tag the repo builds against. The placement rules are based on the HIG, and the MAUI behavior on the source code on `main`.

1. **Whether UIKit integrates a `UISearchController` into the toolbar when the navigation bar is hidden** is unknown. It is the only thing that could make alternative B cheaper. An evening's spike in the simulator settles it. The study recommends A regardless, for the reasons in §4.
2. **What `UISearchBar` looks like in iOS 26 outside a system bar.** Whether it draws its own glass background, or needs `SearchBarStyle.Minimal` and a cleared `SearchTextField` background inside Spine's capsule, has to be measured against a native reference (`UINavigationController` + `UIToolbar` + `searchController`) the same way #377 measured the header height. The capsule's height, margin and close button are taken from that measurement, not from memory.
3. **Where UIKit puts search on iOS 26 when a tab bar is visible** (the toolbar then lies below the tab bar) is not verified. The rule in §5 (search button in the header bar) follows the HIG's "no bottom toolbar" case and is checked in the same reference.
4. **Scopes.** UIKit's scope bar has three of the four bugs in forum thread 797701. The proposal is that scopes become a menu button (#318, `MenuPicker`) next to the field. It is a design decision for the owner and is not part of v1.
5. **A third slot in the header bar.** The search button takes the right-hand slot and the page's own action disappears. That is good enough for v1 (pages with search rarely have another action), but if both search and, say, a filter are to be visible, `HeaderBarView` needs a group of two buttons. On iOS 26 that is the glass group `UINavigationBar` draws for image buttons. A decision for the owner.
6. **A search row that slides away on scroll** (iOS 15–18, Android) requires the row to follow `HeaderBarCollapseProgress` and change the top inset during scroll. The mechanism has existed since #330 but has not been tried for an extra row.
7. **The Android capsule's dimensions.** The M3 spec has the dimensions, but they are not checked against the styles in Material 1.12. Measure them in the emulator against a native `SearchBar`.
8. **The keyboard animation on iOS.** Translating the curve (`UIViewAnimationCurve` 7, which is not public) to MAUI's `Easing` is a known problem. If that is too coarse, the capsule's lift may have to be done natively: a `UIView` animation with the keyboard's curve on the handler's platform view.
9. **Mac Catalyst ⌘F and the field in the title bar** have not been tried. Spine makes the title bar transparent today (`SpineApplication.MacCatalyst.cs:92`), and the field goes in the header bar below it.

---

## 10. Delivery plan

1. **#269 first:** the keyboard observer in `NavigationRegion` (iOS notifications, Android IME), the inset on `ViewModelBase`, the list's bottom inset. Almanacka's workaround is removed.
2. **Spike on iOS 26 (simulator, then device):** a native reference (`UINavigationController` with a toolbar and `searchController`, with and without a tab bar) next to a glass capsule with MAUI's `SearchBar`. Measure the resting height, the margin and the position above the keyboard, compare with screenshots and answer §9 items 1–3.
3. **API + top:** `PageSearchAttribute`, `PageSearch`, `ViewModelBase.Search`, discovery. The row below the header bar (iOS 15–18, Android, sheets), the field at the far right (iPad, Mac Catalyst) and `TitleBar.Content` + suggestions (Windows).
4. **iOS 26 bottom:** the capsule, soft edge `Bottom`, the lift above the keyboard, the search button for tab and footer pages, the close circle, `IsActive` from code.
5. **The suggestion list** on iOS and Android, and ⌘F on Mac Catalyst.
6. **Sample and docs:** a "Search" page in `MauiSpineSampleApp` (a list that is filtered, suggestions, placement choice), `docs/wiki/search.md`, a row in `page-actions.md`, the `/spine-page` skill.
7. **In the apps:** Orientera's free-text search (#94), Almanacka's name search, Puckkoll.
8. **Issues of their own:** `UISearchTab` (search tab in iOS 26, requires `UITab` under MAUI's `TabbedPage`), scopes through #318 and a group of two buttons in the header bar (§9 item 5).

---

## Decisions (2026-09-30)

Jonatan went through the study's questions on 2026-09-30 and followed the recommendations. Rows marked **Proposal** had no recommendation in the study; they carry a proposal with reasons, which holds until he says otherwise.

- **The field.** Spine draws it: MAUI's `SearchBar` in a glass capsule (alternative A), not `UISearchController` in a hidden `UINavigationController`.
- **Placement.** `SearchPlacement.Automatic` as in §5: at the bottom on iPhone with iOS 26 when the bottom edge is free, a search button when a tab bar or footer takes it, at the top everywhere else and in sheets.
- **Declaration.** `[PageSearch]` on a string property in the view model, not the issue's `ISearchable` with `[NavigableRegion(Search = true)]`.
- **The order.** #269 (the keyboard observer) is delivered first and search is built on it.
- **Scopes.** A menu button (`MenuPicker`) next to the field, not in v1 (§9 item 4).
- **The third slot in the header bar.** In v1 the search button replaces the page's right-hand action. A group of two buttons becomes an issue of its own (§9 item 5).
- **`UISearchTab`.** An issue of its own.
- **A search row that slides away on scroll.** Not in v1; settled in step 3.
- **Results.** Spine draws no results view. The page filters its own list, and suggestions are a list of strings.
- **Later steps.** ⌘F on Mac Catalyst and M3's full-screen `SearchView` on Android.
- **The spike.** The iOS 26 spike against a native reference is run before the bottom capsule is built.

---

## 11. References

- Issue #307, *Search in the header bar*: https://github.com/jonatansoderberg/Maui.Spine/issues/307
- Issue #317, prioritized backlog: https://github.com/jonatansoderberg/Maui.Spine/issues/317
- Issue #269, *Keyboard avoidance*: https://github.com/jonatansoderberg/Maui.Spine/issues/269
- Apple HIG, *Search fields*: https://developer.apple.com/design/human-interface-guidelines/search-fields
- Apple, *Build a UIKit app with the new design* (WWDC25, session 284): https://developer.apple.com/videos/play/wwdc2025/284/
- Apple, *Customizing your app's navigation bar* (the section *Integrate search in your toolbar*): https://developer.apple.com/documentation/uikit/customizing-your-app-s-navigation-bar
- Apple, `UINavigationItem.SearchBarPlacement.integrated`: https://developer.apple.com/documentation/uikit/uinavigationitem/searchbarplacement-swift.enum/integrated
- Apple, `UINavigationItem.SearchBarPlacement.integratedButton`: https://developer.apple.com/documentation/uikit/uinavigationitem/searchbarplacement-swift.enum/integratedbutton
- Apple, `searchBarPlacementAllowsToolbarIntegration`: https://developer.apple.com/documentation/uikit/uinavigationitem/searchbarplacementallowstoolbarintegration
- Apple, `searchBarPlacementBarButtonItem`: https://developer.apple.com/documentation/uikit/uinavigationitem/searchbarplacementbarbuttonitem
- Apple, `searchBarPlacementAllowsExternalIntegration`: https://developer.apple.com/documentation/uikit/uinavigationitem/searchbarplacementallowsexternalintegration
- Apple, `preferredSearchBarPlacement`: https://developer.apple.com/documentation/uikit/uinavigationitem/preferredsearchbarplacement
- Apple, `UISearchTab`: https://developer.apple.com/documentation/uikit/uisearchtab
- Apple, `UISearchTab.automaticallyActivatesSearch`: https://developer.apple.com/documentation/uikit/uisearchtab/automaticallyactivatessearch
- Apple, `UITabBarController.bottomAccessory`: https://developer.apple.com/documentation/uikit/uitabbarcontroller/bottomaccessory
- Apple forum 797701, *Summary of iOS/iPadOS 26 UIKit bugs related to UISearchController & UISearchBar using scope buttons*: https://developer.apple.com/forums/thread/797701
- dotnet/macios, `src/uikit.cs` at `dotnet-10.0.1xx-xcode26.2-10217`: https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode26.2-10217/src/uikit.cs
- MAUI, `TabbedRenderer.cs` (iOS) on `main`: https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/Compatibility/Handlers/TabbedPage/iOS/TabbedRenderer.cs
- MAUI, `SearchBarHandler.iOS.cs`, `.Android.cs`, `.Windows.cs` on `main`: https://github.com/dotnet/maui/tree/main/src/Core/src/Handlers/SearchBar
- MAUI, `TitleBar.cs` on `main`: https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/TitleBar/TitleBar.cs
- Material Components Android, *Search*: https://github.com/material-components/material-components-android/blob/master/docs/components/Search.md
- Spine: `issues/330-collapse-on-scroll.md`, `issues/366-scroll-edge.md`, `issues/377-ios26-header-height.md`, `issues/379-header-bar-page.md`, `issues/409-…`, `issues/411-…`, `issues/318-menu-buttons.md`, `docs/wiki/regions.md`, `docs/wiki/page-actions.md`, `docs/wiki/tab-host.md`, `docs/proposals/spine-glass-buttons.md`
