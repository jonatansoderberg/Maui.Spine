# Windows Platform Options

Spine exposes a rich set of Windows-specific settings under `SpineOptions.Windows`. All properties only take effect on the Windows platform and are silently ignored on other targets.

---

## Configuration

Set options in `MauiProgram.cs`:

```csharp
builder.UseSpine(options =>
{
    options.AddAssembly(typeof(MauiProgram).Assembly);
    options.AppTitle = "My App";

    options.Windows.Backdrop = WindowBackdrop.Mica;
    options.Windows.BottomSheetBackdrop = WindowBackdrop.Acrylic;
    options.Windows.InitialWidth  = 500;
    options.Windows.InitialHeight = 800;
    options.Windows.MinWidth      = 400;
    options.Windows.MinHeight     = 400;
    options.Windows.IsResizable   = true;
    options.Windows.IsMaximizable = true;
    options.Windows.PersistWindowPosition = true;
    options.Windows.StartupPosition = WindowStartupPosition.Center;
    options.Windows.ShowInTaskbar = true;
    options.Windows.ShowTrayIcon  = true;
    options.Windows.TrayIconSvg   = "app_icon.svg";
    options.Windows.CloseToBackground = true;
    options.Windows.TrayIconTooltip = "My App";
});
```

---

## Window size and position

| Property | Type | Default | Description |
|---|---|---|---|
| `InitialWidth` | `int` | `500` | Initial window width in pixels |
| `InitialHeight` | `int` | `800` | Initial window height in pixels |
| `MinWidth` | `int` | `0` | Minimum window width (0 = no minimum) |
| `MinHeight` | `int` | `0` | Minimum window height (0 = no minimum) |
| `PersistWindowPosition` | `bool` | `false` | Save and restore position/size across sessions |
| `StartupPosition` | `WindowStartupPosition` | `Center` | Where the window opens on first run |

### `WindowStartupPosition` values

| Value | Description |
|---|---|
| `TopLeft` | Top-left corner of the screen |
| `TopCenter` | Horizontally centered at the top |
| `TopRight` | Top-right corner of the screen |
| `CenterLeft` | Vertically centered on the left edge |
| `Center` | Centered on the screen (default) |
| `CenterRight` | Vertically centered on the right edge |
| `BottomLeft` | Bottom-left corner of the screen |
| `BottomCenter` | Horizontally centered at the bottom |
| `BottomRight` | Bottom-right corner of the screen |

---

## Window chrome

| Property | Type | Default | Description |
|---|---|---|---|
| `IsResizable` | `bool` | `true` | Whether the window border can be dragged to resize |
| `IsMaximizable` | `bool` | `false` | Whether the maximize button is shown |
| `IsMinimizable` | `bool` | `true` | Whether the minimize button is shown |
| `IsAlwaysOnTop` | `bool` | `false` | Keep the window above all other windows |

---

## Taskbar and tray

| Property | Type | Default | Description |
|---|---|---|---|
| `ShowInTaskbar` | `bool` | `true` | Whether the window appears in the taskbar and Alt+Tab |
| `ShowTrayIcon` | `bool` | `false` | Show a system-tray icon |
| `TrayIconSvg` | `string?` | `null` | SVG filename (e.g. `"app_icon.svg"`) rendered at runtime via `Plugin.Maui.SvgIcon`. Takes precedence over `TrayIconPath` when set |
| `TrayIconPath` | `string` | `"Resources/Raw/light_theme.ico"` | Path to a pre-built tray icon (relative to app package). Used when `TrayIconSvg` is null |
| `TrayIconTooltip` | `string` | `""` | Tooltip text when hovering the tray icon |
| `CloseToBackground` | `bool` | `false` | Hide the window instead of exiting when closed |

### Tray icon setup

Enable the tray icon and pair it with `CloseToBackground` to allow the user to restore the app from the tray.

**Preferred approach — SVG rendered at runtime** (requires `Plugin.Maui.SvgIcon`):

```csharp
options.Windows.ShowTrayIcon      = true;
options.Windows.TrayIconSvg       = "app_icon.svg";
options.Windows.TrayIconTooltip   = "My App";
options.Windows.CloseToBackground = true;
```

**Alternative — pre-built `.ico` file:**

```csharp
options.Windows.ShowTrayIcon      = true;
options.Windows.TrayIconPath      = "Resources/Raw/tray.ico";
options.Windows.TrayIconTooltip   = "My App";
options.Windows.CloseToBackground = true;
```

Tray menu items are populated automatically from shortcuts registered via `options.Shortcuts.UseHandler<MyHandler>()` where `showInTray = true`. See [Shortcuts](shortcuts.md).

---

## Window backdrop

Spine supports Windows system backdrop materials (Mica and Acrylic) that adapt automatically to light/dark theme.

| Property | Type | Default | Description |
|---|---|---|---|
| `Backdrop` | `WindowBackdrop` | `Mica` | System backdrop for the main app window |
| `BottomSheetBackdrop` | `WindowBackdrop` | `None` | System backdrop for the bottom sheet surface |

### `WindowBackdrop` values

| Value | Effect |
|---|---|
| `None` | No system backdrop; the window uses a standard opaque background |
| `Mica` | Mica material (Windows 11+). Subtle desktop tint that follows the wallpaper |
| `Acrylic` | Desktop Acrylic material (Windows 10 1903+). Translucent frosted-glass look |

```csharp
options.Windows.Backdrop = WindowBackdrop.Mica;               // main window
options.Windows.BottomSheetBackdrop = WindowBackdrop.Acrylic;  // sheet surface
```

---

## Single-instance enforcement

| Property | Type | Default | Description |
|---|---|---|---|
| `AllowMultipleInstances` | `bool` | `false` | When `false`, a second launch redirects to the existing instance |

By default, only one instance of the app is allowed to run. A second launch activates the running instance and exits immediately. Set `AllowMultipleInstances = true` to allow side-by-side instances.

---

## Title bar (desktop header)

When `IsTitleBarVisible = true` (the default on desktop), Spine renders a custom `TitleBar` control that:

- Displays `SpineOptions.AppTitle` as the window title
- Shows the current page's `Title` (from the ViewModel) as the subtitle
- Hosts page-action buttons from the active page's `PageActions` collection

To globally disable the custom title bar in favor of Spine's in-page header bar:

```csharp
options.RegionDefaults.IsTitleBarVisible  = false;
options.RegionDefaults.IsHeaderBarVisible = true;
options.RegionDefaults.TitlePlacement     = TitlePlacement.HeaderBar;
```
