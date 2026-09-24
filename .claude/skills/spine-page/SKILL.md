---
name: spine-page
description: Add or change a page in a Plugin.Maui.Spine app — the three-file page pattern, [NavigableRegion] / [NavigableSheet] / [NavigableTab], typed navigation parameters and results, page actions in the header bar, lifecycle hooks, dismiss guards, and tab badges. Use when creating pages, navigating between them, or wiring header-bar buttons. Invoke as /spine-page.
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

    [PageAction("Save")]
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

Useful attribute properties, all optional: `Lifetime` (`Transient` default; `Singleton` keeps state — tabs default to `Singleton`), `IsHeaderBarVisible`, `IsBackButtonVisible`, `TitleAlignment`, `SafeAreaEdges` (exclude an edge to draw behind it, then offset with `ViewModelBase.SafeAreaInsets`), `ScrollInset` (edges on which the page's first `ScrollView`/`CollectionView` takes that inset as a native content inset, so a list can scroll under a bar and still reach its last row; the same thing per view is `SafeArea.ScrollInset="Bottom"`). For a page on a photo or hero: `HeaderBar = HeaderBarMode.Overlay` (header floats over content that starts at the top), `HeaderBarForeground = "#FFFFFF"` (title and icons in a fixed colour) and `StatusBarStyle = StatusBarStyle.LightContent` (iOS needs `UIViewControllerBasedStatusBarAppearance` false in Info.plist); give the list `SafeArea.ScrollInset="Top"`. For an iOS-style large title: `LargeTitle = true`, put `<HeaderBarLargeTitle />` first in the page's `ScrollView`/`CollectionView` (e.g. `CollectionView.Header`) — it shows the page title in the platform's large-title size and is what Spine measures (custom titles: `HeaderBarConstants.LargeTitle…` plus `HeaderBar.CollapseDistance`); Spine gives that scroll view its top inset, fades the bar's title and background in as the label scrolls under the bar, and publishes `ViewModelBase.HeaderBarCollapseProgress`. `HeaderBar.ScrollSource="{x:Reference List}"` on the page picks the scroll view (default: the first), `HeaderBarBackground = Solid | Clear` (default `Auto`: `Clear` under `Overlay`, `Solid` otherwise) decides what is behind the bar while content scrolls under it; layout (`HeaderBar`), `LargeTitle` and `HeaderBarBackground` combine freely, each also an app-wide default in `options.RegionDefaults`/`TabDefaults`/`SheetDefaults`.

Sheets: `AllowedDetents = [SheetDetent.Compact | Medium | Expanded | FullScreen, "75%", "300px"]`, `InitialDetent`, `BackgroundPageOverlay = None | Dimmed | Blurred`. Override `OnCloseRequestedAsync` to guard dismissal; `OnDismissedAsync` runs when the user closes it without a result.

Buttons in a sheet: Save, Cancel and Done are always page actions in the sheet's header bar (`[PageAction("Save")]`, `[PageAction("Cancel", Placement = PageActionPlacement.Primary)]`), never a button stack at the bottom. A sheet's own primary action (Log in, Continue, Pay) goes in `<SpinePage.Footer>`: outside the scrolling content, pinned to the bottom of the visible sheet at every detent and following it while dragged, with the page's `BindingContext`. Don't pad the top of a sheet page to clear the close button — Spine already starts the content below the handle and the header row.

Tabs: `Order` is effectively required (assembly scan order is random; duplicates fail startup). At most five tabs. `SpineApplication`'s `x:TypeArguments` must be one of the tab pages and picks the initial tab. Re-selecting the active tab pops to root, then raises `OnTabReselectedAsync()` on the root's ViewModel. `ITabBadgeService.SetBadge<TPage>("3")` / `("")` for a dot / `(null)` to clear.

## Navigation

```csharp
await _navigation.NavigateToAsync<DetailPage>();
await _navigation.BackAsync();
await _navigation.SetRootAsync<LoginPage>();          // replaces the stack (logout); with a tab page, resets that tab
```

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

## Page actions (header bar)

An action that should open a menu rather than run a command is added in the constructor as `PageActions.Add(new PageAction(null, new MenuItems { new MenuSection("Filter:") { new MenuPicker(FilterCommand) { new MenuAction("All", "house.svg") { IsChecked = true }, … } }, new SubMenu("More") { … } }) { Svg = "more.svg" })`; the same `MenuItems` goes on a page button as `MenuButton.Items="{Binding SortMenu}"` (with `MenuButton.ShowsSelection="True"` for a pop-up whose text follows the pick). See docs/wiki/menus.md.

Put `[PageAction]` on a `[RelayCommand]` method (or an `ICommand` property); Spine adds the button once, before the page appears:

```csharp
[PageAction(Svg = "settings.svg")] [RelayCommand] private Task OpenSettingsAsync() { … }   // icon
[PageAction("Save")]               [RelayCommand] private Task SaveAsync() { … }           // text
[PageAction("Cancel", Placement = PageActionPlacement.Primary)] [RelayCommand] private Task CancelAsync() { … } // replaces the back button
```

Hand-made actions still work (`PageActions.Add(new PageAction("Save", SaveCommand) { Svg = … })`, best from the constructor). The header shows the first visible action per slot (`Primary` left, `Secondary` right). `Svg` is a short file name of an embedded SVG (the app's own or `Plugin.Maui.Spine.Svg.Icons`). On iOS 26 the header bar's buttons are Liquid Glass by default (`options.Apple.GlassHeaderActions = false` turns it off).

`PageAction` is observable: set `Text`, `Svg`, `Badge` ("3", "•"), `IsEnabled` or `IsVisible` on the instance while the page shows and the header follows. Find a declared one with `PageActions.First(a => a.Command == FilterCommand)`. Adding or removing from `PageActions` at runtime also updates the header.

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
- Parameters / Results / Page actions: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/navigation-parameters.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/navigation-results.md · https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/page-actions.md
- Sample: https://github.com/jonatansoderberg/Maui.Spine/tree/master/samples/MauiSpineSampleApp/Pages
