# Plugin.Maui.Spine

A code-first navigation framework for .NET MAUI that replaces Shell: stack-based region navigation, native bottom sheets, a tab host, a header bar with page actions, Liquid Glass buttons on iOS 26, app shortcuts, and window management on Windows. Pages are discovered by attribute, so there are no route tables and no manual registration.

```bash
dotnet add package Plugin.Maui.Spine
```

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

`UseSpine()` also registers every other Spine package the app references (Widgets, PushNotifications, AnimatedLabel, …), from a list the build generates: no scanning at startup. A package's own `UseXxx(o => …)` is needed only to change its options.

```csharp
[NavigableRegion(Title = "Settings")]
public partial class SettingsPage : SpinePage { }

await navigation.NavigateToAsync<SettingsPage>();
var result = await navigation.NavigateToWithResultAsync<PickerSheet, Choice>();
```

## What is in the package

- **Regions** — full-screen pages on a navigation stack with animated transitions and back-swipe.
- **Sheets** — bottom sheets with detents, overlays, nested stacks and dismiss guards, declared with `[NavigableSheet]`.
- **Tabs** — `[NavigableTab]` pages hosted in the platform's own tab bar.
- **Typed parameters and results** — `NavigateToAsync<TPage, TParam>` and `NavigateToWithResultAsync<TPage, TResult>`.
- **Header bar and page actions** — back button, title, and ViewModel-driven action buttons.
- **Glass** — `Glass.Style` on any `Button` or `ImageButton`; Liquid Glass on iOS 26, a normal button elsewhere.
- **Shortcuts** — dock, jump list and tray menu through one handler interface.
- **Windows** — window size and position, tray icon, single instance, custom title bar.

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [Getting started](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/getting-started.md)
- [Regions](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/regions.md), [Sheets](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/sheets.md), [Tab host](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/tab-host.md)
- [Page actions](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/page-actions.md), [Glass buttons](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/glass-buttons.md), [Shortcuts](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/shortcuts.md), [Windows options](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/windows-options.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
