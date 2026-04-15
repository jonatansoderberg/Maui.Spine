# Getting Started with Plugin.Maui.Spine

This guide walks you through setting up a new .NET MAUI app with Spine from scratch.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 17.13+ with the **.NET MAUI** workload installed
- A target platform: Android (API 21+), iOS 15+, macOS Catalyst, or Windows 10 (19041+)

---

## 1. Install the NuGet package

```bash
dotnet add package Plugin.Maui.Spine
```

---

## 2. Register Spine in `MauiProgram.cs`

Call `UseSpine` on the `MauiAppBuilder` and point it at your app assembly:

```csharp
using Plugin.Maui.Spine.Extensions;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSpine(options =>
            {
                options.AddAssembly(typeof(MauiProgram).Assembly);
                options.AppTitle = "My App";
            });

        return builder.Build();
    }
}
```

> **Android note:** `Assembly.GetEntryAssembly()` returns `null` on Android. Always use `typeof(MauiProgram).Assembly` (or any type in your app assembly) instead.

---

## 3. Set up the application class

Replace the standard MAUI `Application` with `SpineApplication<TRootPage>`, passing your first page as the type argument. The simplest way is to do this in XAML:

**`App.xaml`**
```xml
<SpineApplication
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:TypeArguments="MainPage"
    x:Class="MyApp.App">
    <SpineApplication.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/Styles/Colors.xaml" />
                <ResourceDictionary Source="Resources/Styles/Styles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </SpineApplication.Resources>
</SpineApplication>
```

**`App.xaml.cs`**
```csharp
namespace MyApp;

public partial class App
{
    public App() => InitializeComponent();
}
```

---

## 4. Add global usings (optional but recommended)

Create a `GlobalUsings.cs` file in your app project:

```csharp
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Plugin.Maui.Spine.Core;
```

---

## 5. Create your first page

Every Spine page is made up of three files. Create these in a `Pages/` folder:

**`Pages/MainPage.cs`** — code-behind with the navigation attribute
```csharp
namespace MyApp.Pages;

[NavigableRegion(Title = "Home")]
public partial class MainPage { public MainPage() => InitializeComponent(); }
```

**`Pages/MainPage.View.xaml`** — XAML layout
```xml
<SpinePage
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="MyApp.Pages.MainPage"
    x:TypeArguments="MainPageViewModel"
    x:DataType="MainPageViewModel">

    <VerticalStackLayout Padding="16">
        <Label Text="Welcome to Spine!" />
    </VerticalStackLayout>

</SpinePage>
```

**`Pages/MainPage.ViewModel.cs`** — ViewModel
```csharp
namespace MyApp.Pages;

public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{
}
```

---

## 6. Register the page files in the project

Spine auto-discovers pages via assembly scanning — no manual DI registration needed. However, the `.csproj` must explicitly include the XAML and ViewModel files with `<DependentUpon>` grouping (ViewModels are excluded by a wildcard by default):

```xml
<!-- In MyApp.csproj -->

<MauiXaml Update="Pages\MainPage.View.xaml">
  <Generator>MSBuild:Compile</Generator>
  <DependentUpon>MainPage.cs</DependentUpon>
</MauiXaml>

<Compile Include="Pages\MainPage.ViewModel.cs">
  <DependentUpon>MainPage.cs</DependentUpon>
</Compile>
```

> Visual Studio may auto-add a bare `<Compile Include="...">` entry when you create the file. Remove the bare entry and keep only the `<DependentUpon>` version to avoid duplicate-compilation errors.

---

## Next steps

| Topic | Guide |
|---|---|
| Region (stack) navigation | [Regions](regions.md) |
| Bottom-sheet navigation | [Sheets](sheets.md) |
| Passing parameters between pages | [Navigation Parameters](navigation-parameters.md) |
| Returning results from pages | [Navigation Results](navigation-results.md) |
| Header bar & page actions | [Page Actions](page-actions.md) |
| App shortcuts & tray icon | [Shortcuts](shortcuts.md) |
| Windows desktop options | [Windows Platform Options](windows-options.md) |
| Custom page transitions | [Custom Transitions](custom-transitions.md) |
