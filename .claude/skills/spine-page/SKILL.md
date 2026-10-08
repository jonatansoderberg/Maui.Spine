---
name: spine-page
description: Add or change a page in a Plugin.Maui.Spine app — the three-file page pattern, [NavigableRegion] / [NavigableSheet] / [NavigableTab], typed navigation parameters and results, action sheets (ShowActionsAsync), page actions in the header bar, lifecycle hooks, loading data with TaskState and StateView (loading, error with retry, empty), dismiss guards, and tab badges. Use when creating pages, navigating between them, loading their data, or wiring header-bar buttons. Invoke as /spine-page.
---

You are adding or changing a page in an app built on **Plugin.Maui.Spine**. Spine discovers pages by attribute (no route tables, no DI registration) and every navigation call is one typed async method. Full docs: https://github.com/jonatansoderberg/Maui.Spine/tree/master/docs/wiki.

Look at an existing page in the app first and match its namespaces, folder and style. Then follow the pattern below.

---

## The three-file pattern

```
Pages/
  SettingsPage.cs             code-behind: the navigation attribute + InitializeComponent
  SettingsPage.View.xaml      layout: <SpinePage> root
  SettingsPage.ViewModel.cs   ViewModelBase subclass, primary-constructor injection
```

```csharp
// SettingsPage.cs
namespace MyApp.Pages;

[NavigableRegion(Title = "Settings")]
public partial class SettingsPage { public SettingsPage() => InitializeComponent(); }
```

```xml
<!-- SettingsPage.View.xaml -->
<SpinePage
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="MyApp.Pages.SettingsPage"
    x:TypeArguments="SettingsPageViewModel"
    x:DataType="SettingsPageViewModel">
    <VerticalStackLayout Padding="16">
        <Label Text="{Binding Title}" />
        <Button Text="Save" Command="{Binding SaveCommand}" />
    </VerticalStackLayout>
</SpinePage>
```

```csharp
// SettingsPage.ViewModel.cs
namespace MyApp.Pages;

public partial class SettingsPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty] public partial string? Title { get; set; }

    [PageAction("Save", Role = PageActionRole.Confirm)]
    [RelayCommand] private async Task Save() => await _navigation.BackAsync();
}
```

And in the csproj (see `/spine-setup` §5), the two `<DependentUpon>` entries — with no bare `<Compile Include>` duplicate:

```xml
<MauiXaml Update="Pages\SettingsPage.View.xaml"><Generator>MSBuild:Compile</Generator><DependentUpon>SettingsPage.cs</DependentUpon></MauiXaml>
<Compile Include="Pages\SettingsPage.ViewModel.cs"><DependentUpon>SettingsPage.cs</DependentUpon></Compile>
```

Naming: `XxxPage` / `XxxPageViewModel` / `XxxPage.View.xaml`; a subfolder gets its own namespace (`MyApp.Pages.Settings`) and its own `XmlnsDefinition` line in `GlobalXmlns.cs`.

## Which attribute

| Attribute | Presentation | Navigate with |
|---|---|---|
| `[NavigableRegion(Title = …)]` | Full-screen page pushed on the stack; header bar, back button, slide transition, back-swipe | `NavigateToAsync<TPage>()` |
| `[NavigableSheet(Title = …, AllowedDetents = […])]` | Bottom sheet over the current page, with its own stack | `NavigateToAsync<TPage>()` — the attribute decides |
| `[NavigableTab(Title = …, Icon = "tab_home.svg", Order = 0)]` | A region that roots a bottom tab in the native tab bar | `NavigateToAsync<TPage>()` switches to the tab; `SwitchToTabAsync<TPage>()` says so explicitly |

Useful attribute properties, all optional: `Lifetime` (`Transient` default; `Singleton` keeps state — tabs default to `Singleton`), `IsHeaderBarVisible`, `IsBackButtonVisible`, `TitleAlignment`, `SafeAreaEdges` (exclude an edge to draw behind it, then offset with `ViewModelBase.SafeAreaInsets`), `ScrollInset` (edges on which the page's first `ScrollView`/`CollectionView` takes that inset as a native content inset, so a list can scroll under a bar and still reach its last row; the same thing per view is `SafeArea.ScrollInset="Bottom"`). The header bar has three independent settings, each on the attribute, as an app-wide default in `options.RegionDefaults`/`TabDefaults`/`SheetDefaults`, and settable on the view model while the page is shown (Spine re-resolves and re-lays out): **layout** `HeaderBar` (`Normal`: content starts below the bar; `Overlay`: content starts at the top of the screen behind the bar, and the page keeps clear what it wants with `SafeAreaInsets.Top` / `SafeArea.ScrollInset="Top"` — Spine adds no top inset there), **`LargeTitle`** and **background** `HeaderBarBackground`, which only shows once content scrolls under the bar (hidden at rest, `Overlay` included — `Normal` and `Overlay` treat backgrounds identically): `Auto` (default; the platform's bar — on iOS the navigation bar's default, `SoftEdge` on 26 and `HardEdge` from 27, for a region/tab page whose `ScrollView`/`CollectionView` fills it from the top, `Solid` otherwise), `Solid` (the page's colour hides content under the bar; under `Normal` the bar has its own row), `Transparent` (content shows through, the title floats over it — for a photo or map under `Overlay`), `SoftEdge` (iOS 26+ soft scroll edge over the whole header; a fading band on Android/Windows; `Solid` before iOS 26), `SoftStatusBar` (the soft effect behind the status bar only, rows stay sharp behind the title; a status-bar band on Android/Windows) and `HardEdge` (frosted, nearly opaque band, the iOS 27 default; Android/Windows: nearly opaque with a hairline). Reduce Transparency turns the scroll edge values into `Solid`; `ViewModelBase.EffectiveHeaderBarBackground` tells what the page got. Under `Normal`, a large title, `Transparent` or a scroll edge value lays the page out under the bar and Spine adds `Top` to the scroll source's `SafeArea.ScrollInset`, so the first row starts below the bar. For a page on a photo or hero: `HeaderBar = HeaderBarMode.Overlay`, `HeaderBarBackground = Transparent` (or `Solid` to close the bar once the list scrolls), `HeaderBarForeground = "#FFFFFF"` (title and icons in a fixed colour; it stays fixed on a solid bar; `null` = Auto, follows the theme) and `StatusBarStyle = StatusBarStyle.LightContent` (default `Auto` follows the theme) (iOS needs `UIViewControllerBasedStatusBarAppearance` false in Info.plist). For an iOS-style large title: `LargeTitle = true`, put `<HeaderBarLargeTitle />` first in the page's `ScrollView`/`CollectionView` (e.g. `CollectionView.Header`) — it shows the page title in the platform's large-title size and is what Spine measures (custom titles: `HeaderBarConstants.LargeTitle…` plus `HeaderBar.CollapseDistance`; the whole bar's height is `HeaderBarConstants.BarHeight`, 54 on iOS 26 with the 44-point `Height` item row at its top); Spine cross-fades the large title out and the bar's title in as the label scrolls under the bar and publishes `ViewModelBase.HeaderBarCollapseProgress`. `HeaderBar.ScrollSource="{x:Reference List}"` on the page picks the scroll view (default: the first). Let that scroll view reach the page's sides — no side `Margin` on it, no side `Padding` on its parents — and keep the page's side margin inside it, so every page lines up with the header bar's buttons: `SafeArea.PageMargin="True"` on a `CollectionView` (rows, header and footer inside `HeaderBarConstants.PageMargin`, items without a side margin of their own, a grid's column gap from `GridItemsLayout.HorizontalItemSpacing`), `Padding="{x:Static HeaderBarConstants.PagePadding}"` on a `ScrollView`'s content: UIKit draws the scroll edge effect inside the scroll view and no wider, so a narrower list leaves the bar's sides without it, and Spine logs a `[Spine]` console line for that page. The sample's Header bar page combines every setting live.

Sheets: `AllowedDetents = [SheetDetent.Compact | Medium | Expanded | FullScreen, "75%", "300px"]`, `InitialDetent`, `BackgroundPageOverlay = None | Dimmed | Blurred`. Override `OnCloseRequestedAsync` to guard dismissal; `OnDismissedAsync` runs when the user closes it without a result.

Buttons in a sheet: confirm and cancel are always page actions in the sheet's header bar, never a button stack at the bottom, and they are icons, not text: `[PageAction("Save", Role = PageActionRole.Confirm)]` draws a checkmark on the right, `[PageAction("Cancel", Role = PageActionRole.Cancel)]` an X on the left (iOS 26 HIG; the same on Android). The text is only what a screen reader says (default: localised Done/Cancel). A sheet with nothing to discard needs no Cancel — Spine's own X closes it. Text buttons are for actions with no standard icon. A sheet's own primary action (Log in, Continue, Pay) goes in `<SpinePage.Footer>`: outside the scrolling content, pinned to the bottom of the visible sheet at every detent and following it while dragged, with the page's `BindingContext`. Don't pad the top of a sheet page to clear the close button — Spine already starts the content below the handle and the header row.

Tabs: `Order` is effectively required (assembly scan order is random; duplicates fail startup). At most five tabs. `SpineApplication`'s `x:TypeArguments` must be one of the tab pages and picks the initial tab. Re-selecting the active tab pops to root, then raises `OnTabReselectedAsync()` on the root's ViewModel. `ITabBadgeService.SetBadge<TPage>("3")` / `("")` for a dot / `(null)` to clear.

## Navigation

```csharp
await _navigation.NavigateToAsync<DetailPage>();
await _navigation.BackAsync();
await _navigation.SetRootAsync<LoginPage>();          // replaces the stack (logout); with a tab page, resets that tab
await _navigation.ShowAsync<SettingsPage>();          // from outside the app: goes back to it if it is on the stack, pushes otherwise
```

Use `ShowAsync` (and `ShowAsync<TPage, TParam>`) for every way in from outside the app: shortcuts, widget and notification taps, links. `NavigateToAsync` always pushes, so a second tap stacks a second copy. A page already in front gets the parameter in `OnNavigationParameterAsync` but no `OnAppearingAsync`.

### Typed parameter

```csharp
public sealed record PersonData(string Name, string Email);

[NavigableRegion(Title = "Person")]
public partial class PersonPage : INavigableWithParameter<PersonData> { public PersonPage() => InitializeComponent(); }

public partial class PersonPageViewModel : ViewModelBase, IReceivesNavigationParameter<PersonData>
{
    public Task OnNavigationParameterAsync(PersonData p) { Name = p.Name; return Task.CompletedTask; }   // before OnAppearingAsync
}

await _navigation.NavigateToAsync<PersonPage, PersonData>(new("Alice", "alice@example.com"));
```

### Typed result

```csharp
public sealed record PickResult(string Choice);

[NavigableSheet(Title = "Pick", AllowedDetents = [SheetDetent.Medium])]
public partial class PickSheet : INavigableWithResult<PickResult> { public PickSheet() => InitializeComponent(); }

// inside the sheet's ViewModel
[RelayCommand] private Task Choose(string c) => _navigation.ReturnAsync(new PickResult(c));   // closes and delivers
[RelayCommand] private Task Cancel() => _navigation.BackAsync();                               // closes with IsSuccess = false

// the caller
var result = await _navigation.NavigateToWithResultAsync<PickSheet, PickResult>();
if (result is { IsSuccess: true, Value: { } picked }) …
```

Both at once: implement both interfaces and call `NavigateToWithResultAsync<TPage, TParam, TResult>(param)`.

### Found from the platform's search

A page with a parameter can be opened from Spotlight (iOS, Mac) or a shortcut (Android): inject `ISearchIndex` and `await searchIndex.UpsertAsync(new SearchableItem(Id: $"room-{id}", Title: name, Target: NavigationTarget.To<RoomPage, RoomId>(new RoomId(id)), Description: summary, Icon: "kitchen", Keywords: ["…"]))`; `RemoveAsync(id)` takes it away. A tapped result is opened with `ShowAsync<TPage, TParam>`, also on a cold start (over the root page). The parameter is stored as JSON and read back later, maybe by a newer app version: pass a small serialisable id, not the loaded data; one that cannot round-trip throws from `UpsertAsync`. A result whose page or parameter type no longer fits is logged and removed. `[Searchable(Description = …, Icon = …, Keywords = […])]` on a page makes it a fixed entry, indexed at startup, opened without a parameter. Windows has no index (`IsSupported` is false). See docs/wiki/searchable-items.md.
### Action sheets

For a choice that needs no page, ask for the platform's action sheet and await the pick: `var picked = await _navigation.ShowActionsAsync(new ActionSheet("Night sprint", "optional message") { Actions = [new("Share", "share.svg", ShareCommand), new("Remove", "trashcan.svg", RemoveCommand) { IsDestructive = true }] });`. The rows are `MenuAction`s (Title, Svg, Command, CommandParameter, IsDestructive, IsEnabled, IsVisible); the picked row's command runs, then the task returns the row, or `null` on cancel. `ActionSheet.CommandParameter` goes to rows without their own (one set of rows for every list item); pass `anchor: button` so iPad points the popover at it (iOS 26 grows the sheet out of it on the iPhone too) — from XAML `CommandParameter="{Binding Source={RelativeSource Self}}"`. iOS: `UIAlertController` with a Cancel row (`CancelText`); Android: a Material bottom sheet; Windows: a `MenuFlyout`. A menu that belongs to a button or a row is `MenuButton.Items`/`PageAction.Menu`/`ContextMenu.Items` instead.

### Shared elements and zoom

`Transition.Tag` (namespace `Plugin.Maui.Spine.Extensions`) carries a view from one page to the next in the same stack. The same tag on a view on each page makes a shared element: it flies between them on the push and back on the pop, while the pages slide. The tag on the page arriving itself (its root `SpinePage`) makes a zoom: the page grows out of the view with that tag and shrinks back into it, under the finger on the back-swipe. The same tag on a view inside the zooming page makes that view its focus, the part that lines up with the tapped view; give one whenever the page shows what was tapped. Make tags unique per item (`Key => $"tile-{Id}"`), so a list matches the right row. A view scrolled out of sight, Reduce Motion, sheets and tab switches get the usual transition. iOS, Mac Catalyst and Android; Windows plays the usual transition.

## Lifecycle

| Hook | When |
|---|---|
| `OnNavigationParameterAsync` | Before appearing, with the parameter |
| `OnAppearingAsync(NavigationDirection)` | `None` (root), `NavigateTo` (pushed), `Back` (a child popped). Tab roots also get it on tab switches |
| `OnDisappearingAsync` | Just before leaving the screen |
| `OnResumedAsync` | The app came back to the foreground (or its window got focus again) while the page is shown: the current page, and an open sheet's. Not at launch |
| `OnBackRequestedAsync` → `bool` | Return `false` to cancel back (unsaved changes) |
| `OnCloseRequestedAsync` → `bool` | Same, for a sheet's close |
| `OnTabReselectedAsync` | The active tab tapped again at root (scroll to top) |

Load data in `OnAppearingAsync`; keep constructors cheap. For work that lives with the page use `Poll(interval, ct => …)` (runs while shown, pauses in the background), `WhileVisible(h => svc.Changed += h, h => svc.Changed -= h, OnChanged)` (subscribed while shown, UI thread) and `PageLifetime` (a token cancelled when the page is left) instead of timers, tokens and subscribe/unsubscribe pairs. Declare page actions with `[PageAction]` (below) rather than adding them in `OnAppearingAsync`. Refresh in `OnResumedAsync` what may have changed while the app was away (server data, today's date) instead of subscribing to `Window.Activated` in code-behind. Spine has no day-change hook; a page that must turn at midnight runs its own timer.

## Loading data (`TaskState` / `StateView`)

For data a page shows, use `Load(...)` from the constructor instead of hand-written `IsLoading`/`HasError` flags and a try/catch in `OnAppearingAsync`. It loads when the page first appears, is cancelled when the page is left, and loads again on reappearing only if it has nothing to show (never loaded, cancelled, failed):

```csharp
public TaskState<IReadOnlyList<Game>> Games { get; }

public GamesPageViewModel(IGamesApi api)
{
    Games = Load(ct => api.GetTodayAsync(ct), isEmpty: g => g.Count == 0);
}
```

```xml
<StateView State="{Binding Games}" EmptyText="No games today">
    <CollectionView ItemsSource="{Binding Games.Value}" />
</StateView>
```

`StateView` shows a spinner, "Couldn't load" + the exception's message + Try again, or the empty text in the content's place; the content keeps the page's binding context. Custom `LoadingTemplate`/`ErrorTemplate`/`EmptyTemplate` bind to the `TaskState` (`ErrorMessage`, `LoadCommand`); app-wide ones are resources `DefaultStateView…Template`. Pull to refresh: `RefreshView IsRefreshing="{Binding Games.IsRefreshing}" Command="{Binding Games.LoadCommand}"`. Skeleton: pass `placeholder: () => [.. Enumerable.Repeat(Game.Empty, 4)]` and bind `Skeleton.IsActive="{Binding Games.IsLoading}"` on the content. A failed refresh keeps the result and sets `HasError`/`ErrorMessage`. When a filter changes, `Games.Reset()` then `Games.LoadAsync()`. Refresh on return with `OnResumedAsync() => Games.LoadAsync()`.

## Page actions (header bar)

An action that should open a menu rather than run a command is added in the constructor as `PageActions.Add(new PageAction(null, new MenuItems { new MenuSection("Filter:") { new MenuPicker(FilterCommand) { new MenuAction("All", "house.svg") { IsChecked = true }, … } }, new SubMenu("More") { … } }) { Svg = "more.svg" })`; the same `MenuItems` goes on a page button as `MenuButton.Items="{Binding SortMenu}"` (with `MenuButton.ShowsSelection="True"` for a pop-up whose text follows the pick). See docs/wiki/menus.md.

Put `[PageAction]` on a `[RelayCommand]` method (or an `ICommand` property); Spine adds the button once, before the page appears:

```csharp
[PageAction(Svg = "settings.svg")] [RelayCommand] private Task OpenSettingsAsync() { … }   // icon
[PageAction("Save", Role = PageActionRole.Confirm)] [RelayCommand] private Task SaveAsync() { … }   // checkmark, says "Save"
[PageAction("Cancel", Role = PageActionRole.Cancel)] [RelayCommand] private Task CancelAsync() { … } // X on the left, replaces back/close
[PageAction("Filter")]             [RelayCommand] private Task FilterAsync() { … }         // text: only when there is no standard icon
```

Hand-made actions still work (`PageActions.Add(new PageAction("Save", SaveCommand) { Svg = … })`, best from the constructor). The header shows the first visible action per slot (`Primary` left, `Secondary` right). `Svg` is a short file name of an embedded SVG (the app's own or `Plugin.Maui.Spine.Svg.Icons`). On iOS 26 the header bar's buttons are Liquid Glass by default (`options.Apple.GlassHeaderActions = false` turns it off).

A tap can play a haptic: `[PageAction("Save", Role = PageActionRole.Confirm, Haptic = Haptic.Success)]` (see docs/wiki/haptics.md).

`PageAction` is observable: set `Text`, `Svg`, `Badge` ("3", "•"), `IsEnabled`, `IsVisible` or `Haptic` on the instance while the page shows and the header follows. Find a declared one with `PageActions.First(a => a.Command == FilterCommand)`. Adding or removing from `PageActions` at runtime also updates the header.

## Search in the header bar

Put `[PageSearch(Placeholder = "Search towns", Submit = nameof(OpenFirstCommand))]` on the `[ObservableProperty] public partial string Query { get; set; } = "";` that holds the text, and filter the page's own list in `partial void OnQueryChanged(string value)`; nothing goes in XAML. Spine creates `ViewModelBase.Search` (a `PageSearch`) before the page appears and keeps it and `Query` in step both ways. `Search.IsActive = true` starts a search (focus and keyboard), `Search.IsVisible = false` hides the field. Placement `Automatic`: a row below the header bar on phones, Android, Windows and in sheets, the trailing end of the bar on iPad and Mac Catalyst (wide windows); `SearchPlacement.Top` forces the row. One per page; no header bar, no field. Spine draws no results view. See docs/wiki/search.md.

## Binding to the page from a template

`{PageCommand Pick}` binds `PickCommand` on the page's view model from inside a `DataTemplate`; `{PageBinding Path}` binds any member of it (supports `Mode`, `Converter`, `StringFormat`). Use them instead of `RelativeSource AncestorType` bindings.

## Do

- Keep the code-behind to the attribute and `InitializeComponent()`; logic goes in the ViewModel.
- Inject services through the primary constructor; the ViewModel is resolved from DI per navigation.
- Use records for parameters and results.
- Use `[ObservableProperty]` / `[RelayCommand]` from CommunityToolkit.Mvvm — `ViewModelBase` builds on it.

## Don't

- No `Shell`, no `NavigationPage`, no `Navigation.PushAsync`: Spine owns navigation.
- No manual page or ViewModel registration in DI; the attribute is the registration.
- No string routes. `PushMessage.Route` and widget links are strings the *app* maps to a typed `NavigateToAsync`.
- Don't push a `[NavigableTab]` page; navigating to it switches tabs.

## Documentation

- Page pattern: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/page-pattern.md
- Regions / Sheets / Tab host: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/regions.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/sheets.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/tab-host.md
- Loading states: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/loading-states.md
- Shared elements and zoom: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/transitions.md
- Lightbox (a `[NavigableLightbox]` photo viewer page with a `Lightbox`): https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/lightbox.md
- Search in the header bar: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/search.md
- Action sheets: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/menus.md#action-sheets
- Parameters / Results / Page actions: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/navigation-parameters.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/navigation-results.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/page-actions.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp/Pages
