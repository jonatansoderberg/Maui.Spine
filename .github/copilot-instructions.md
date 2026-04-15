# GitHub Copilot Instructions

## Project overview

This workspace contains the **Plugin.Maui.Spine** library (a .NET 10 MAUI navigation framework) and a sample application that consumes it. The library provides region (stack) navigation and bottom-sheet navigation for MAUI apps. The sample app uses **Plugin.Maui.Spine** for navigation and **CommunityToolkit.Mvvm** for MVVM. All pages are auto-discovered via assembly scanning — there is no manual DI registration required for pages.

---

## Technology stack

| Concern | Library |
|---|---|
| MVVM source generation | `CommunityToolkit.Mvvm` |
| Navigation / shell | `Plugin.Maui.Spine` |
| SVG images | `Plugin.Maui.SvgImage` |
| Async helpers | `AsyncAwaitBestPractices` |

---

## Page structure — the three-file pattern

Every page in `MauiBottomSheetPoc/Pages/` is made up of exactly three files grouped under a single logical node in the project file:

```
Pages/
  MyPage.cs                  ← code-behind (partial class + attribute)
  MyPage.View.xaml           ← XAML layout (SpinePage root)
  MyPage.ViewModel.cs        ← ViewModel (partial class : ViewModelBase)
```

Subdirectory pages follow the same pattern under their own folder, e.g. `Pages/Settings/`.

### `MyPage.cs` — code-behind

Minimal partial class. The navigation attribute (`[NavigableRegion]` or `[NavigableSheet]`) lives here.

```csharp
namespace App.Pages;

[NavigableRegion(Title = "My page")]
public partial class MyPage { public MyPage() => InitializeComponent(); }
```

For a bottom-sheet page use `[NavigableSheet]`:

```csharp
namespace App.Pages;

[NavigableSheet(
    Title = "My sheet",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    InitialDetent = SheetDetent.Medium,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class MyPage { public MyPage() => InitializeComponent(); }
```

`SheetDetent` values are `string` constants: `SheetDetent.Compact`, `SheetDetent.Medium`, `SheetDetent.Expanded`, `SheetDetent.FullScreen`. Custom sizes use `"50%"` (percentage) or `"300px"` (absolute) string format.

Optional attribute properties available on both `[NavigableRegion]` and `[NavigableSheet]`:
- `Lifetime` — `ServiceLifetime.Transient` (default) or `ServiceLifetime.Singleton`
- `IsHeaderBarVisible` / `IsBackButtonVisible` / `TitleAlignment` / `TitlePlacement`

Additional property on `[NavigableRegion]` only:
- `IsTitleBarVisible` — whether the native window title bar is shown when this page is active (desktop only)

### `MyPage.View.xaml` — XAML

Root element is always `<SpinePage>`. Bind the ViewModel via `x:TypeArguments` and `x:DataType`.

```xml
<SpinePage
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="App.Pages.MyPage"
    x:TypeArguments="MyPageViewModel"
    x:DataType="MyPageViewModel">

    <!-- content here -->

</SpinePage>
```

The global MAUI xmlns already includes `Plugin.Maui.Spine.Core`, `Plugin.Maui.Spine.Presentation`, `Plugin.Maui.SvgImage`, `App.Pages`, and `App.Pages.Settings` (see `GlobalXmlns.cs`).

### `MyPage.ViewModel.cs` — ViewModel

Inherits `ViewModelBase`. Use primary-constructor injection for services. Use `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm.

```csharp
namespace App.Pages;

public partial class MyPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? SomeValue { get; set; }

    [RelayCommand]
    private async Task DoSomething() => await _navigation.NavigateToAsync<OtherPage>();

    // Override lifecycle hooks as needed:
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // populate PageActions here if needed
        return base.OnAppearingAsync(navigationDirection);
    }
}
```

---

## Registering a new page in the project file

`Pages/**/*.ViewModel.cs` files are **excluded by default** via a wildcard `<Compile Remove>` rule. Each ViewModel and each XAML file must be explicitly included inside the main `<ItemGroup>` that contains the other page registrations, using `<DependentUpon>` to group all three files under the code-behind node.

```xml
<!-- In MauiBottomSheetPoc.csproj -->

<MauiXaml Update="Pages\MyPage.View.xaml">
  <Generator>MSBuild:Compile</Generator>
  <DependentUpon>MyPage.cs</DependentUpon>
</MauiXaml>

<Compile Include="Pages\MyPage.ViewModel.cs">
  <DependentUpon>MyPage.cs</DependentUpon>
</Compile>
```

For a page in a subdirectory (e.g. `Pages\Settings\`):

```xml
<MauiXaml Update="Pages\Settings\MyPage.View.xaml">
  <Generator>MSBuild:Compile</Generator>
  <DependentUpon>MyPage.cs</DependentUpon>
</MauiXaml>

<Compile Include="Pages\Settings\MyPage.ViewModel.cs">
  <DependentUpon>MyPage.cs</DependentUpon>
</Compile>
```

> The `.cs` code-behind file is picked up automatically — only the XAML and the ViewModel need explicit entries.

> **Duplicate-entry warning:** Visual Studio automatically inserts a bare `<Compile Include="...">` entry whenever a new `.cs` file is added, and a bare `<MauiXaml Update="...">` entry whenever a new `.xaml` file is added. Before adding the correct `<DependentUpon>` versions, **check that no bare entry for the same file already exists** and remove it. The final `.csproj` must contain exactly one entry per file — the `<DependentUpon>` version shown above. Having both a bare entry and a `<DependentUpon>` entry for the same file will cause duplicate-compilation errors.

---

## Navigation

Navigation is handled by `INavigationService` (injected via constructor). No routes need to be registered — Spine discovers all `[NavigableRegion]` and `[NavigableSheet]` classes from the assembly registered in `MauiProgram.cs`.

```csharp
// Navigate to a region page (push onto navigation stack)
await _navigation.NavigateToAsync<SomePage>();

// Navigate to a bottom-sheet page
await _navigation.NavigateToAsync<SomeSheetPage>();
```

---

## Global usings

The following usings are available everywhere without an explicit `using` statement (`GlobalUsings.cs`):

```csharp
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Plugin.Maui.Spine.Core;
```

---

## Page action buttons (header bar)

Add `PageAction` entries inside `OnAppearingAsync` to show buttons in the header bar. Only add them when `PageActions.Count == 0` to avoid duplicates on re-navigation.

```csharp
public override Task OnAppearingAsync(NavigationDirection navigationDirection)
{
    if (PageActions.Count == 0)
    {
        PageActions.Add(new PageAction(text: "Save", command: SaveCommand));
        // or with an SVG icon:
        PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand) { Svg = "settings.svg" });
    }
    return base.OnAppearingAsync(navigationDirection);
}
```

`PageAction` key properties: `Text`, `Svg`, `Command`, `CommandParameter`, `IsVisible`, and `Placement` (`PageActionPlacement.Secondary` trailing/right by default, or `PageActionPlacement.Primary` for the leading/left slot).

---

## Navigation parameters

To pass typed data to a page, implement `INavigableWithParameter<TParam>` on the page and `IReceivesNavigationParameter<TParam>` on its ViewModel. `OnNavigationParameterAsync` is called before `OnAppearingAsync`.

```csharp
// Page code-behind
[NavigableRegion(Title = "Detail")]
public partial class DetailPage : INavigableWithParameter<MyData>
{
    public DetailPage() => InitializeComponent();
}

// ViewModel
public partial class DetailPageViewModel : ViewModelBase, IReceivesNavigationParameter<MyData>
{
    public Task OnNavigationParameterAsync(MyData param)
    {
        // apply param before OnAppearingAsync fires
        return Task.CompletedTask;
    }
}

// Caller
await _navigation.NavigateToAsync<DetailPage, MyData>(new MyData(...));
```

---

## Navigation results

To receive a result back from a page, implement `INavigableWithResult<TResult>` on the page. The caller uses `NavigateToWithResultAsync` and gets a `NavigationResult<TResult>`.

```csharp
// Page code-behind
[NavigableSheet(Title = "Confirm")]
public partial class ConfirmSheet : INavigableWithResult<bool>
{
    public ConfirmSheet() => InitializeComponent();
}

// ViewModel — deliver the result
[RelayCommand]
private async Task Confirm() => await _navigation.ReturnAsync(true);

// Caller
var result = await _navigation.NavigateToWithResultAsync<ConfirmSheet, bool>();
if (result.HasValue) { /* result.Value */ }
```

---

## ViewModelBase lifecycle hooks

Override any of these in a ViewModel as needed:

| Method | When called |
|---|---|
| `OnAppearingAsync(NavigationDirection)` | Page becomes visible (navigated to or returned back to) |
| `OnDisappearingAsync(NavigationDirection)` | Page is about to be hidden |
| `OnDismissedAsync()` | Page dismissed without an explicit result via `ReturnAsync` |
| `OnBackRequestedAsync()` | Back gesture requested — return `false` to cancel |
| `OnCloseRequestedAsync()` | Sheet close requested — return `false` to cancel |

---

## Naming conventions

| Item | Convention | Example |
|---|---|---|
| Page class | `PascalCase` + `Page` suffix | `SettingsPage` |
| ViewModel class | same name + `ViewModel` suffix | `SettingsPageViewModel` |
| XAML file | `[PageName].View.xaml` | `SettingsPage.View.xaml` |
| Code-behind | `[PageName].cs` | `SettingsPage.cs` |
| Namespace (root pages) | `App.Pages` | — |
| Namespace (subdir pages) | `App.Pages.[FolderName]` | `App.Pages.Settings` |
| Commands | `[Verb][Noun]Command` (generated) | `OpenSettingsCommand` |
| Observable properties | `public partial T Prop { get; set; }` | `public partial string? Name { get; set; }` |

---

## Do not

- Do **not** manually register pages in DI — Spine auto-discovers them via `options.AddAssembly(...)`.
- Do **not** omit `<DependentUpon>` — without it the ViewModel file will not compile (it is removed by the wildcard exclude).
- Do **not** use a XAML root other than `<SpinePage>` for app pages.
- Do **not** add `using` statements for namespaces already covered by `GlobalUsings.cs` or `GlobalXmlns.cs`.
- Do **not** leave bare `<Compile Include>` or `<MauiXaml Update>` entries in the `.csproj` alongside the `<DependentUpon>` versions — VS auto-creates bare entries when files are added; always remove the bare entry before (or at the same time as) adding the `<DependentUpon>` entry.
- Do **not** reference or suggest `internal` or `private` library types to consumers — these are implementation details of `Plugin.Maui.Spine` and are not part of its public API. Only types that are explicitly `public` in the library should appear in sample-app code, instructions, or wiki pages.
