# GitHub Copilot Instructions — Plugin.Maui.Spine.Svg

`Plugin.Maui.Spine.Svg` is one project with two halves over a shared resource cache: **images** (`SvgImageSource`, rendered into MAUI `Image`/`ImageButton`) and **icon files** (`SvgIconService`, `.ico`/`.png` for tray and window icons). The image half is documented first, the icon half under [Icon files](#icon-files).

## Project overview — images

`Plugin.Maui.Spine.Svg` is a .NET 10 MAUI class library that renders SVG files embedded in its own assembly as bitmaps for MAUI `Image` and `ImageButton` controls. Rendering is done with **SkiaSharp** + **Svg.Skia**. Tinting, padding, and automatic light/dark theme switching are supported.

---

## Technology stack

| Concern | Library |
|---|---|
| SVG → bitmap rendering | `SkiaSharp` + `Svg.Skia` |
| MAUI integration | `Microsoft.Maui.Controls` |
| DI registration | `MauiAppBuilderExtensions.UseEmbeddedSvgImages()` |

---

## Key classes

| Class | Role |
|---|---|
| `SvgImageSource` | Static class exposing attached bindable properties for XAML usage |
| `SvgImageSourceBehavior` | `Behavior<View>` — hooks into `Image`/`ImageButton`, renders SVG at view size, responds to theme/size changes |
| `SvgBitmapLoader` | Static renderer; renders SVG → PNG bytes via SkiaSharp; result cached in `ConcurrentDictionary` |
| `ResourceNameCache` | Singleton; scans embedded resources in this assembly on `Initialize()`; resolves short filenames to full resource names |
| `MauiAppBuilderExtensions` | `UseEmbeddedSvgImages()` — registers `ResourceNameCache` as singleton |

---

## SVG asset management

All SVG files live in the `Images/` folder of **this project** and are declared as `<EmbeddedResource>` in the `.csproj`. Short filenames are case-insensitively matched against the full embedded resource name at runtime by `ResourceNameCache`.

### Adding a new SVG

Add both entries to `Plugin.Maui.Spine.Svg.csproj`:

```xml
<None Remove="Images\MyIcon.svg" />
<!-- ... in the EmbeddedResource group: -->
<EmbeddedResource Include="Images\MyIcon.svg" />
```

After adding, the icon is immediately available by short name (e.g. `"myicon.svg"`) — no other registration is needed.

---

## XAML usage

`Plugin.Maui.Spine.Svg` is mapped to the global MAUI xmlns via `GlobalXmlns.cs` in the consuming app. **Do not** add an explicit `xmlns:svg` alias.

### Standard pattern — `ImageButton` with style

```xml
<ImageButton
    SvgImageSource.Svg="settings.svg"
    WidthRequest="48"
    HeightRequest="48"
    Style="{StaticResource SvgImageButtonStyle}" />
```

`SvgImageButtonStyle` (defined in the app's `Styles.xaml`) sets:
- `SvgImageSource.EnableSvg = True`
- `SvgImageSource.LightTintColor = Black`
- `SvgImageSource.DarkTintColor = White`

### Manual pattern — custom tint

```xml
<ImageButton
    SvgImageSource.Svg="settings.svg"
    SvgImageSource.EnableSvg="True"
    SvgImageSource.LightTintColor="Blue"
    SvgImageSource.DarkTintColor="LightBlue"
    WidthRequest="48"
    HeightRequest="48" />
```

### `Image` control

```xml
<Image
    SvgImageSource.Svg="logo.svg"
    SvgImageSource.EnableSvg="True"
    WidthRequest="64"
    HeightRequest="64" />
```

---

## Attached properties reference

| Property | Type | Default | Purpose |
|---|---|---|---|
| `SvgImageSource.Svg` | `string` | `null` | Short SVG filename (e.g. `"fish.svg"`) |
| `SvgImageSource.EnableSvg` | `bool` | `false` | Must be `true` to activate the behavior |
| `SvgImageSource.LightTintColor` | `Color` | `Transparent` | Tint applied in light theme |
| `SvgImageSource.DarkTintColor` | `Color` | `Transparent` | Tint applied in dark theme |
| `SvgImageSource.Padding` | `Thickness` | `5` | Padding inside the rendered bitmap |

Setting `Svg` or tint properties while `EnableSvg` is already `true` automatically re-renders.

---

## Programmatic rendering (C#)

Use `SvgBitmapLoader` directly when you need an `ImageSource` outside of XAML:

```csharp
// Resolve the full resource name first
var registry = IPlatformApplication.Current.Services.GetRequiredService<ResourceNameCache>();
var resourceName = registry.Resolve("settings.svg") ?? "settings.svg";

// Render at explicit size with optional tint and padding
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(resourceName, width: 48, height: 48, tint: Colors.Black);
// or with padding:
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(resourceName, 48, 48, Colors.Black, new Thickness(4));
```

---

## Theme awareness

`SvgImageSourceBehavior` subscribes to `Application.Current.RequestedThemeChanged` and re-renders automatically:
- `AppTheme.Light` → `LightTintColor`
- `AppTheme.Dark` → `DarkTintColor`
- `Transparent` tint means no color filter is applied (SVG renders with its original colors)

---

## DI / startup

`UseEmbeddedSvgImages()` is **called automatically by Spine's `UseSpine()`**. Do not call it again manually when the app uses `Plugin.Maui.Spine`.

For apps that do not use Spine:

```csharp
builder.UseEmbeddedSvgImages();
```

---

## Do not

- Do **not** place SVG files outside `Plugin.Maui.Spine.Svg/Images/` — `ResourceNameCache` only scans this assembly.
- Do **not** declare `<MauiImage>` items for SVGs — they must be `<EmbeddedResource>`.
- Do **not** use an explicit `xmlns:svg` alias in XAML — the namespace is already in the global xmlns.
- Do **not** call `UseEmbeddedSvgImages()` when using `Plugin.Maui.Spine` — it is already called by `UseSpine()`.
- Do **not** pass the full resource name (e.g. `Plugin.Maui.Spine.Svg.Images.Fish.svg`) to `SvgImageSource.Svg` — always use the short filename.
- Do **not** call `ResourceNameCache.Initialize()` manually — it is called once during `UseEmbeddedSvgImages()`.

---

## Icon files

### Project overview

`Plugin.Maui.Spine.Svg` is a .NET 10 class library that converts SVG files (sourced from `Plugin.Maui.Spine.Svg`'s embedded resources) into platform-specific icon files for use as window icons, tray icons, or app icons. It produces `.ico` multi-size files on Windows and `.png` files on macOS. All generated files are cached on disk.

---

### Technology stack

| Concern | Library |
|---|---|
| SVG → bitmap rendering | `SkiaSharp` + `Svg.Skia` (via `Plugin.Maui.Spine.Svg`) |
| Icon file generation | `IcoWriter` (internal, multi-size `.ico` builder) |
| Cache management | `IconCacheManager` (internal, SHA-256 keyed disk cache) |
| Concurrency guard | `KeyedLock` (internal, per-path `SemaphoreSlim`) |

---

### Key classes

| Class | Visibility | Role |
|---|---|---|
| `SvgIconService` | `public static` | Main entry point — resolves, renders, caches, and returns icon file paths |
| `PlatformIconKind` | `public enum` | `Tray` or `AppIcon` |
| `PlatformIconOptions` | `public sealed record` | Render options: `Tint`, `Padding`, `Sizes`, `PngQuality` |
| `IconCacheManager` | `internal static` | Disk cache root, SHA-256 cache key, atomic file writes |
| `IcoWriter` | `internal static` | Builds a multi-size `.ico` from `List<(int Size, byte[] Png)>` |
| `KeyedLock` | `internal static` | Per-path semaphore to prevent concurrent cache writes |

---

### SVG source

All SVGs come from `Plugin.Maui.Spine.Svg`'s embedded resources. `SvgIconService` accepts a short SVG filename (e.g. `"settings.svg"`) and resolves it via the injected `ResourceNameCache` from `Plugin.Maui.Spine.Svg`. **Do not** add SVG files to this project.

---

### Usage

#### Basic — tray icon with defaults

```csharp
var registry = services.GetRequiredService<ResourceNameCache>();

string iconPath = await SvgIconService.GetOrCreateAsync(
    "settings.svg",
    registry,
    PlatformIconKind.Tray);
```

#### With options

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

### `PlatformIconOptions` reference

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Tint` | `Color` | `Colors.Black` | Color blended over the SVG via `SrcIn` blend mode |
| `Padding` | `Thickness` | `new Thickness(0)` | Inset inside the rendered bitmap |
| `Sizes` | `IReadOnlyList<int>?` | `null` (platform default) | Override rendered pixel sizes |
| `PngQuality` | `int` | `100` | PNG compression quality (0–100) |

---

### Platform defaults

| Platform | Output format | Default sizes |
|---|---|---|
| Windows | `.ico` (multi-size) | `16, 20, 24, 32, 40, 48, 64, 128, 256` |
| macOS | `.png` (largest size) | `18, 36` |

Override defaults by setting `PlatformIconOptions.Sizes`.

---

### Disk cache

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

### Do not

- Do **not** add SVG files to this project — all SVGs must be embedded resources in `Plugin.Maui.Spine.Svg`.
- Do **not** use `SvgIconService` for inline MAUI view images — use `SvgImageSource` attached properties from `Plugin.Maui.Spine.Svg` instead.
- Do **not** modify or bypass `IconCacheManager`, `IcoWriter`, or `KeyedLock` — they are internal implementation details.
- Do **not** use the returned icon path as a MAUI `ImageSource` — it is a raw file path intended for platform-native icon APIs.
- Do **not** call `SvgIconService.GetOrCreateAsync` on the UI thread — it performs file I/O and should be awaited from a background context.
