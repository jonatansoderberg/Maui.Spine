# Plugin.Maui.Spine

**Plugin.Maui.Spine** is a navigation framework for .NET MAUI that replaces Shell with a clean, code-first model built around stack-based region navigation and native bottom sheets. Pages are auto-discovered via attribute scanning — no route tables, no manual DI registration — and every navigation call is a single, strongly-typed async method.

---

## Why Spine?

| Problem with Shell | Spine's answer |
|---|---|
| Route strings are stringly-typed and easy to break | Generic `NavigateToAsync<TPage>()` — compile-time safety |
| Passing parameters requires URI encoding | `NavigateToAsync<TPage, TParam>(param)` — typed, no boxing |
| Returning data from a page is not supported natively | `NavigateToWithResultAsync<TPage, TResult>()` — awaitable result pattern |
| Shell navigation style is fixed | Pluggable `ISpineTransitions` — swap animations without touching pages |
| Bottom sheets require platform code | Declarative `[NavigableSheet]` attribute with detents, overlays, and dismiss guards |
| No shortcut / tray icon integration | Built-in shortcut pipeline for dock, jump list, and system tray |

---

## Key features

- **Region navigation** — push/pop full-screen pages with animated transitions and an interactive back-swipe gesture on mobile
- **Bottom sheets** — modal sheets with configurable snap points (detents), background overlays, nested navigation stacks, and dismiss guards
- **Typed navigation parameters** — pass strongly-typed data to any page before it appears
- **Typed navigation results** — await a page and receive a typed result when it closes
- **Header bar & page actions** — built-in header bar with back button, title, and pluggable action buttons (text or SVG icon)
- **App shortcuts** — register OS-level shortcuts (dock, jump list, tray menu) with a single handler interface
- **Windows desktop support** — window size, position persistence, tray icon, single-instance enforcement, and custom title bar
- **Platform-aware defaults** — mobile defaults differ from desktop defaults out of the box; override per-page or globally
- **Zero route registration** — Spine scans your assembly for `[NavigableRegion]` and `[NavigableSheet]` attributes at startup

---

## Platforms

| Platform | Status |
|---|---|
| Android | ✅ |
| iOS | ✅ |
| macOS Catalyst | ✅ |
| Windows (WinUI 3) | ✅ |

---

## Quick start

### 1. Install

```bash
dotnet add package Plugin.Maui.Spine
```

### 2. Register in `MauiProgram.cs`

```csharp
using Plugin.Maui.Spine.Extensions;

builder
    .UseMauiApp<App>()
    .UseSpine(options =>
    {
        options.AddAssembly(typeof(MauiProgram).Assembly);
        options.AppTitle = "My App";
    });
```

### 3. Wire up the application

```xml
<!-- App.xaml -->
<SpineApplication
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:TypeArguments="MainPage"
    x:Class="MyApp.App" />
```

### 4. Create a page (three-file pattern)

```csharp
// Pages/MainPage.cs
[NavigableRegion(Title = "Home")]
public partial class MainPage { public MainPage() => InitializeComponent(); }
```

```xml
<!-- Pages/MainPage.View.xaml -->
<SpinePage
    xmlns="http://schemas.microsoft.com/dotnet/maui/global"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Class="MyApp.Pages.MainPage"
    x:TypeArguments="MainPageViewModel"
    x:DataType="MainPageViewModel">

    <VerticalStackLayout Padding="16">
        <Button Text="Open Settings" Command="{Binding OpenSettingsCommand}" />
        <Button Text="Show Sheet"    Command="{Binding ShowSheetCommand}" />
    </VerticalStackLayout>

</SpinePage>
```

```csharp
// Pages/MainPage.ViewModel.cs
public partial class MainPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand] private async Task OpenSettings() => await _navigation.NavigateToAsync<SettingsPage>();
    [RelayCommand] private async Task ShowSheet()    => await _navigation.NavigateToAsync<MySheetPage>();
}
```

### 5. Declare a bottom sheet

```csharp
[NavigableSheet(
    Title = "Options",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public partial class MySheetPage { public MySheetPage() => InitializeComponent(); }
```

---

## Core concepts

| Concept | Description | Guide |
|---|---|---|
| **Navigable** | Interface/attribute that marks a page as discoverable by Spine | [Page Pattern](docs/wiki/page-pattern.md) |
| **Region** | Full-screen stack-navigation page (`[NavigableRegion]`) | [Regions](docs/wiki/regions.md) |
| **Sheet** | Bottom-sheet modal page (`[NavigableSheet]`) | [Sheets](docs/wiki/sheets.md) |
| **Navigation parameters** | Pass typed data into a page | [Navigation Parameters](docs/wiki/navigation-parameters.md) |
| **Navigation results** | Await a typed result from a page | [Navigation Results](docs/wiki/navigation-results.md) |
| **Page actions** | Header bar buttons driven by the ViewModel | [Page Actions](docs/wiki/page-actions.md) |
| **Shortcuts** | OS dock/jump-list/tray menu integration | [Shortcuts](docs/wiki/shortcuts.md) |
| **Windows options** | Window chrome, tray, single-instance | [Windows Options](docs/wiki/windows-options.md) |
| **Custom transitions** | Replace the built-in slide animation | [Custom Transitions](docs/wiki/custom-transitions.md) |

---

## Documentation

| Guide | Description |
|---|---|
| [Getting Started](docs/wiki/getting-started.md) | Full setup walkthrough from scratch |
| [Three-File Page Pattern](docs/wiki/page-pattern.md) | Code-behind, XAML, and ViewModel explained |
| [Regions](docs/wiki/regions.md) | Stack navigation, lifecycle hooks, back-swipe gesture |
| [Sheets](docs/wiki/sheets.md) | Bottom sheets, detents, overlays, dismiss guards |
| [Navigation Parameters](docs/wiki/navigation-parameters.md) | Pass typed data to a page |
| [Navigation Results](docs/wiki/navigation-results.md) | Await a typed result from a page |
| [Page Actions](docs/wiki/page-actions.md) | Header bar buttons (text and SVG icons) |
| [Shortcuts](docs/wiki/shortcuts.md) | App shortcuts and tray menu |
| [Windows Platform Options](docs/wiki/windows-options.md) | Window size, tray, single-instance, title bar |
| [Custom Transitions](docs/wiki/custom-transitions.md) | Replace the default slide animation |

---

## Sample app

The `samples/MauiSpineSampleApp` project demonstrates all of the above features:

| Demo | Page |
|---|---|
| Region navigation | `MainPage` → `SettingsPage` |
| Bottom sheet with multiple detents | `MainPage` → `SamplePage` (medium + 75% + fullscreen) |
| Singleton sheet with blur overlay | `MainPage` → `SimpleBottomSheetPage` |
| Fullscreen sheet | `MainPage` → `FullscreenSheetPage` |
| Compact (small) sheet | `MainPage` → `SmallSheetPage` |
| Navigation parameter | `MainPage` → `PersonDetailPage` |
| Navigation result | `MainPage` → `FullscreenSheetPage` (awaits `FullscreenSheetResult`) |
| App shortcut → navigation | `ShortcutHandler` → `SettingsPage` |
| Windows tray icon + close-to-background | `MauiProgram.cs` options |

### Run the sample

```bash
# Android
dotnet build samples/MauiSpineSampleApp -t:Run -f net10.0-android

# Windows
dotnet build samples/MauiSpineSampleApp -t:Run -f net10.0-windows10.0.19041.0
```

Or open the solution in Visual Studio 2022 and press **F5**.

---

## Dependencies

| Package | Purpose |
|---|---|
| `CommunityToolkit.Mvvm` | Source-generated MVVM (`[ObservableProperty]`, `[RelayCommand]`) |
| `Plugin.Maui.SvgImage` | SVG image support for page action icons |
| `AsyncAwaitBestPractices` | Safe fire-and-forget async helpers |

---

## License

MIT

