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

    [RelayCommand] private async Task Save() => await _navigation.BackAsync();

    public override Task OnAppearingAsync(NavigationDirection direction)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Save", command: SaveCommand));
        return base.OnAppearingAsync(direction);
    }
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

Useful attribute properties, all optional: `Lifetime` (`Transient` default; `Singleton` keeps state — tabs default to `Singleton`), `IsHeaderBarVisible`, `IsBackButtonVisible`, `TitleAlignment`, `SafeAreaEdges` (exclude an edge to draw behind it, then offset with `ViewModelBase.SafeAreaInsets`).

Sheets: `AllowedDetents = [SheetDetent.Compact | Medium | Expanded | FullScreen, "75%", "300px"]`, `InitialDetent`, `BackgroundPageOverlay = None | Dimmed | Blurred`. Override `OnCloseRequestedAsync` to guard dismissal; `OnDismissedAsync` runs when the user closes it without a result.

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
| `OnBackRequestedAsync` → `bool` | Return `false` to cancel back (unsaved changes) |
| `OnCloseRequestedAsync` → `bool` | Same, for a sheet's close |
| `OnTabReselectedAsync` | The active tab tapped again at root (scroll to top) |

Load data in `OnAppearingAsync`; keep constructors cheap. Guard `PageActions` with `Count == 0` so a `Back` does not add duplicates.

## Page actions (header bar)

```csharp
PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand) { Svg = "settings.svg" });   // icon
PageActions.Add(new PageAction(text: "Save", command: SaveCommand));                                  // text
PageActions.Add(new PageAction(text: "Cancel", command: CancelCommand) { Placement = PageActionPlacement.Primary }); // replaces the back button
```

`Svg` is a short file name of an embedded SVG (the app's own or `Plugin.Maui.Spine.Svg.Icons`). On iOS 26 the header bar's buttons are Liquid Glass by default (`options.Apple.GlassHeaderActions = false` turns it off). To change visibility later, `PageActions.Clear()` and re-add.

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
