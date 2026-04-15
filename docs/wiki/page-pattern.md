# Three-File Page Pattern

Every page in a Spine application is made up of exactly three files. This document describes each file's role and how to wire them together in the project file.

---

## File layout

```
Pages/
  MyPage.cs                ? code-behind: navigation attribute + partial class
  MyPage.View.xaml         ? XAML layout: SpinePage root element
  MyPage.ViewModel.cs      ? ViewModel: ViewModelBase subclass
```

Pages in a subdirectory follow the same pattern:

```
Pages/
  Settings/
    SettingsPage.cs
    SettingsPage.View.xaml
    SettingsPage.ViewModel.cs
```

---

## `MyPage.cs` — code-behind

The minimal code-behind holds the navigation attribute and the `InitializeComponent()` call. Nothing else belongs here unless you need platform-specific code-behind logic.

**Region page:**
```csharp
namespace MyApp.Pages;

[NavigableRegion(Title = "My Page")]
public partial class MyPage { public MyPage() => InitializeComponent(); }
```

**Sheet page:**
```csharp
namespace MyApp.Pages;

[NavigableSheet(
    Title = "My Sheet",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class MyPage { public MyPage() => InitializeComponent(); }
```

---

## `MyPage.View.xaml` — XAML

The root element must always be `<SpinePage>`. Bind the ViewModel via `x:TypeArguments` and `x:DataType`:

```xml
<SpinePage
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="MyApp.Pages.MyPage"
    x:TypeArguments="MyPageViewModel"
    x:DataType="MyPageViewModel">

    <VerticalStackLayout Padding="16">
        <Label Text="{Binding Title}" />
        <Button Text="Do Something" Command="{Binding DoSomethingCommand}" />
    </VerticalStackLayout>

</SpinePage>
```

---

## `MyPage.ViewModel.cs` — ViewModel

Inherits `ViewModelBase`. Use primary-constructor injection for services:

```csharp
namespace MyApp.Pages;

public partial class MyPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? Message { get; set; }

    [RelayCommand]
    private async Task DoSomething() => await _navigation.NavigateToAsync<OtherPage>();

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Save", command: DoSomethingCommand));

        return base.OnAppearingAsync(navigationDirection);
    }
}
```

---

## Project file entries

`*.ViewModel.cs` files are excluded by a wildcard `<Compile Remove>` rule by default. You must explicitly include the ViewModel and XAML files with `<DependentUpon>` so they are grouped under the code-behind node in Solution Explorer and compiled correctly.

```xml
<!-- In MyApp.csproj -->

<MauiXaml Update="Pages\MyPage.View.xaml">
  <Generator>MSBuild:Compile</Generator>
  <DependentUpon>MyPage.cs</DependentUpon>
</MauiXaml>

<Compile Include="Pages\MyPage.ViewModel.cs">
  <DependentUpon>MyPage.cs</DependentUpon>
</Compile>
```

For a page in a subdirectory:

```xml
<MauiXaml Update="Pages\Settings\SettingsPage.View.xaml">
  <Generator>MSBuild:Compile</Generator>
  <DependentUpon>SettingsPage.cs</DependentUpon>
</MauiXaml>

<Compile Include="Pages\Settings\SettingsPage.ViewModel.cs">
  <DependentUpon>SettingsPage.cs</DependentUpon>
</Compile>
```

> **Duplicate-entry warning:** Visual Studio automatically inserts a bare `<Compile Include="...">` entry when a new `.cs` file is added, and a bare `<MauiXaml Update="...">` when a new `.xaml` file is added. Before adding the `<DependentUpon>` version, remove the bare entry. Having both causes duplicate-compilation errors.

---

## Global XAML namespaces

Add a `GlobalXmlns.cs` file in your app project to make Spine types and your page namespaces available in XAML without explicit `xmlns:` declarations:

```csharp
[assembly: XmlnsDefinition(
    "http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Core", AssemblyName = "Plugin.Maui.Spine")]
[assembly: XmlnsDefinition(
    "http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Presentation", AssemblyName = "Plugin.Maui.Spine")]
[assembly: XmlnsDefinition(
    "http://schemas.microsoft.com/dotnet/maui/global",
    "MyApp.Pages")]
[assembly: XmlnsDefinition(
    "http://schemas.microsoft.com/dotnet/maui/global",
    "MyApp.Pages.Settings")]
```

---

## Naming conventions

| Item | Convention | Example |
|---|---|---|
| Page class | `PascalCase` + `Page` suffix | `SettingsPage` |
| ViewModel class | same name + `ViewModel` suffix | `SettingsPageViewModel` |
| XAML file | `[PageName].View.xaml` | `SettingsPage.View.xaml` |
| Code-behind | `[PageName].cs` | `SettingsPage.cs` |
| Namespace (root pages) | `MyApp.Pages` | — |
| Namespace (subdir pages) | `MyApp.Pages.[FolderName]` | `MyApp.Pages.Settings` |
