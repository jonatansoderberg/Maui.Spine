# GitHub Copilot Instructions — Plugin.Maui.SvgIcon

## Project overview

`Plugin.Maui.SvgIcon` is a .NET 10 class library that converts SVG files (sourced from `Plugin.Maui.SvgImage`'s embedded resources) into platform-specific icon files for use as window icons, tray icons, or app icons. It produces `.ico` multi-size files on Windows and `.png` files on macOS. All generated files are cached on disk.

---

## Technology stack

| Concern | Library |
|---|---|
| SVG → bitmap rendering | `SkiaSharp` + `Svg.Skia` (via `Plugin.Maui.SvgImage`) |
| Icon file generation | `IcoWriter` (internal, multi-size `.ico` builder) |
| Cache management | `IconCacheManager` (internal, SHA-256 keyed disk cache) |
| Concurrency guard | `KeyedLock` (internal, per-path `SemaphoreSlim`) |

---

## Key classes

| Class | Visibility | Role |
|---|---|---|
| `SvgIconService` | `public static` | Main entry point — resolves, renders, caches, and returns icon file paths |
| `PlatformIconKind` | `public enum` | `Tray` or `AppIcon` |
| `PlatformIconOptions` | `public sealed record` | Render options: `Tint`, `Padding`, `Sizes`, `PngQuality` |
| `IconCacheManager` | `internal static` | Disk cache root, SHA-256 cache key, atomic file writes |
| `IcoWriter` | `internal static` | Builds a multi-size `.ico` from `List<(int Size, byte[] Png)>` |
| `KeyedLock` | `internal static` | Per-path semaphore to prevent concurrent cache writes |

---

## SVG source

All SVGs come from `Plugin.Maui.SvgImage`'s embedded resources. `SvgIconService` accepts a short SVG filename (e.g. `"settings.svg"`) and resolves it via the injected `ResourceNameCache` from `Plugin.Maui.SvgImage`. **Do not** add SVG files to this project.

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
        Tint    = Colors.White,
        Sizes   = [16, 32, 64, 128],
        Padding = new Thickness(2),
        PngQuality = 100
    },
    appName: "MyApp");
```

`GetOrCreateAsync` returns the absolute file path to the generated icon. The file is safe to pass directly to platform window/tray APIs.

---

## `PlatformIconOptions` reference

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Tint` | `Color` | `Colors.Black` | Color blended over the SVG via `SrcIn` blend mode |
| `Padding` | `Thickness` | `new Thickness(0)` | Inset inside the rendered bitmap |
| `Sizes` | `IReadOnlyList<int>?` | `null` (platform default) | Override rendered pixel sizes |
| `PngQuality` | `int` | `100` | PNG compression quality (0–100) |

---

## Platform defaults

| Platform | Output format | Default sizes |
|---|---|---|
| Windows | `.ico` (multi-size) | `16, 20, 24, 32, 40, 48, 64, 128, 256` |
| macOS | `.png` (largest size) | `18, 36` |

Override defaults by setting `PlatformIconOptions.Sizes`.

---

## Disk cache

Icons are cached at:
```
%LOCALAPPDATA%/SvgIconCache/<appName>/
```
(`Shared` is used when `appName` is `null`.)

The cache key is a SHA-256 hash of:
- SVG file bytes
- Source resource name
- Platform identifier (`win` / `mac`)
- `PlatformIconKind`
- `PlatformIconOptions` fingerprint (tint, padding, quality, sizes)

If the file already exists at the computed path, it is returned immediately without re-rendering.

---

## Do not

- Do **not** add SVG files to this project — all SVGs must be embedded resources in `Plugin.Maui.SvgImage`.
- Do **not** use `SvgIconService` for inline MAUI view images — use `SvgImageSource` attached properties from `Plugin.Maui.SvgImage` instead.
- Do **not** modify or bypass `IconCacheManager`, `IcoWriter`, or `KeyedLock` — they are internal implementation details.
- Do **not** use the returned icon path as a MAUI `ImageSource` — it is a raw file path intended for platform-native icon APIs.
- Do **not** call `SvgIconService.GetOrCreateAsync` on the UI thread — it performs file I/O and should be awaited from a background context.
