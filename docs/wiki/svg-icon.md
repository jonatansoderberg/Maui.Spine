# SvgIcon

`Plugin.Maui.SvgIcon` converts SVG files (sourced from `Plugin.Maui.SvgImage`'s embedded resources) into platform-specific icon files — multi-size `.ico` on Windows and `.png` on macOS. Generated icons are cached on disk using SHA-256 keys, so repeated calls return instantly.

---

## Platforms

| Platform | Output format | Default sizes |
|---|---|---|
| Windows | `.ico` (multi-size) | 16, 20, 24, 32, 40, 48, 64, 128, 256 |
| macOS | `.png` (largest size) | 18, 36 |

---

## Usage

### Basic — tray icon with defaults

```csharp
var registry = services.GetRequiredService<ResourceNameCache>();

string iconPath = await SvgIconService.GetOrCreateAsync(
    "settings.svg",
    registry,
    PlatformIconKind.Tray);
```

### With options

```csharp
string iconPath = await SvgIconService.GetOrCreateAsync(
    "logo.svg",
    registry,
    PlatformIconKind.AppIcon,
    options: new PlatformIconOptions
    {
        Tint     = Colors.White,
        Sizes    = [16, 32, 64, 128],
        Padding  = new Thickness(2),
        PngQuality = 100
    },
    appName: "MyApp");
```

`GetOrCreateAsync` returns the absolute file path to the generated icon. The path is safe to pass directly to platform window or tray APIs.

---

## `PlatformIconKind`

| Value | Description |
|---|---|
| `Tray` | System tray / dock icon |
| `AppIcon` | Application window icon |

---

## `PlatformIconOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Tint` | `Color` | `Colors.Black` | Colour blended over the SVG via `SrcIn` blend mode |
| `Padding` | `Thickness` | `new Thickness(0)` | Inset inside the rendered bitmap |
| `Sizes` | `IReadOnlyList<int>?` | `null` (platform default) | Override rendered pixel sizes |
| `PngQuality` | `int` | `100` | PNG compression quality (0–100) |

---

## SVG source

All SVGs come from `Plugin.Maui.SvgImage`'s embedded resources. `SvgIconService` accepts a short SVG filename (e.g. `"settings.svg"`) and resolves it via the `ResourceNameCache` from `Plugin.Maui.SvgImage`. **Do not** add SVG files to this project — manage them in `Plugin.Maui.SvgImage` instead.

---

## Disk cache

Icons are cached at:

```
%LOCALAPPDATA%/SvgIconCache/<appName>/
```

(`Shared` is used when `appName` is `null`.)

The cache key is a SHA-256 hash of the SVG file bytes, resource name, platform identifier, icon kind, and render options (tint, padding, quality, sizes). If the file already exists at the computed path, it is returned immediately without re-rendering.

---

## Key classes

| Class | Visibility | Role |
|---|---|---|
| `SvgIconService` | `public static` | Main entry point — resolves, renders, caches, and returns icon file paths |
| `PlatformIconKind` | `public enum` | `Tray` or `AppIcon` |
| `PlatformIconOptions` | `public sealed record` | Render options: tint, padding, sizes, quality |

---

## Do not

- Do **not** add SVG files to this project — all SVGs must be embedded resources in `Plugin.Maui.SvgImage`.
- Do **not** use the returned icon path as a MAUI `ImageSource` — it is a raw file path for platform-native icon APIs.
- Do **not** call `SvgIconService.GetOrCreateAsync` on the UI thread — it performs file I/O and should be awaited from a background context.
- Do **not** use `SvgIconService` for inline MAUI view images — use `SvgImageSource` attached properties from [SvgImage](svg-image.md) instead.
