# Shortcuts

**Shortcuts** let users launch common app actions from outside the app — via the OS dock/jump-list (Android, iOS, Windows) and, on Windows, via the system-tray context menu.

---

## Concepts

| Term | Description |
|---|---|
| `SpineShortcut` | A record representing one shortcut: an `Id`, a `Title`, and an optional `ShowInTray` flag |
| `IShortcutBuilder` | Fluent builder used at startup to declare shortcuts before the DI container is fully built |
| `IShortcutHandler` | Your handler class — declares shortcuts statically and handles invocations at runtime via DI |

---

## 1. Implement `IShortcutHandler`

Create a class that implements `IShortcutHandler`. The `Configure` method is `static abstract` so Spine can call it at startup without needing a DI-constructed instance:

```csharp
using MyApp.Pages.Settings;

namespace MyApp;

public class ShortcutHandler(INavigationService _navigation) : IShortcutHandler
{
    // Called once at startup — declare shortcut ids and labels here
    public static void Configure(IShortcutBuilder builder)
    {
        builder.Add(id: "settings", title: "Settings");
        builder.Add(id: "new-item", title: "New Item", showInTray: false);
    }

    // Called at runtime when the user activates a shortcut
    public Task InvokeAsync(string shortcutId) =>
        shortcutId switch
        {
            "settings" => _navigation.NavigateToAsync<SettingsPage>(),
            "new-item"  => _navigation.NavigateToAsync<NewItemPage>(),
            _           => Task.CompletedTask
        };
}
```

---

## 2. Register the handler in `MauiProgram.cs`

```csharp
builder.UseSpine(options =>
{
    options.AddAssembly(typeof(MauiProgram).Assembly);
    options.Shortcuts.UseHandler<ShortcutHandler>();
});
```

---

## 3. Enable the tray icon on Windows and Mac (optional)

Shortcuts are also projected as tray menu items when the tray icon is on and the shortcut has `ShowInTray = true` (the default). On Windows that is the notification-area icon; on Mac Catalyst it is a status item in the menu bar. Both take an SVG for the icon (`TrayIconSvg`, rendered to `.ico` / `.png` by `Plugin.Maui.Spine.Svg`) and can keep the app running when its window closes:

```csharp
builder.UseSpine(options =>
{
    options.Shortcuts.UseHandler<ShortcutHandler>();

    options.Windows.ShowTrayIcon = true;
    options.Windows.TrayIconSvg = "logo.svg";
    options.Windows.TrayIconTooltip = "My App";
    options.Windows.CloseToBackground = true; // hide the window instead of exiting on close

    options.MacOS.ShowTrayIcon = true;
    options.MacOS.TrayIconSvg = "logo.svg";
    options.MacOS.CloseToBackground = true;
});
```

A shortcut declared with `showInTray: false` stays out of the menu but keeps its place in the jump list on Windows and the long-press menu on Android.

---

## `IShortcutBuilder.Add` parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `id` | `string` | — | Stable identifier routed to `InvokeAsync` |
| `title` | `string` | — | Human-readable label shown by the OS and in the tray menu |
| `showInTray` | `bool` | `true` | Whether to include this shortcut in the Windows tray context menu |

---

## Platform behavior

| Platform | Where shortcuts appear |
|---|---|
| Android | App long-press menu (app shortcuts) |
| Windows | Jump list + tray context menu (when tray is enabled) |
| iOS | Not implemented: iOS has no dock or tray, and Home Screen quick actions are not wired up yet |
| Mac Catalyst | Tray menu (when tray is enabled) |

---

## Keeping `Configure` side-effect-free

Because `Configure` is called before the DI container is ready, it must not access services or the database. Use only literal values for `id` and `title`.

Runtime navigation (e.g. `_navigation.NavigateToAsync<...>()`) belongs in `InvokeAsync`, which is resolved from DI and has full access to injected services.
