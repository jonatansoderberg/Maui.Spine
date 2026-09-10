# SVG

`Plugin.Maui.Spine.Svg` does two things with the SVG files embedded in its assembly:

- **`SvgImageSource`** renders them as bitmaps for MAUI `Image` and `ImageButton` controls, with tinting, padding, and automatic light/dark theme switching.
- **`SvgIconService`** converts them into platform-specific icon files — multi-size `.ico` on Windows and `.png` on macOS — for tray and window icons.

Both are powered by **SkiaSharp** + **Svg.Skia** and share one resource cache, so a short filename like `"settings.svg"` means the same thing to either.

---

## Platforms

| Platform | Images | Icon files |
|---|---|---|
| Android | ✅ Supported | — |
| Windows (WinUI 3) | ✅ Supported | `.ico` (multi-size): 16, 20, 24, 32, 40, 48, 64, 128, 256 |
| iOS | ✅ Supported | — |
| macOS Catalyst | 🚧 In progress | `.png` (largest size): 18, 36 |

---

## Registration

Call `UseEmbeddedSvgImages()` in your `MauiProgram.cs` builder chain:

```csharp
using Plugin.Maui.Spine.Svg;

builder
    .UseMauiApp<App>()
    .UseEmbeddedSvgImages();
```

This registers the `ResourceNameCache` singleton that resolves short SVG filenames to full embedded resource names.

`UseSvgIcon()` registers `ISvgIconService` on top of that, optionally with render defaults:

```csharp
builder
    .UseMauiApp<App>()
    .UseSvgIcon(options =>
    {
        options.PaddingPercent = -0.08f;
        options.LineWidthScale = 1.4f;
    });
```

`builder.UseSpine()` calls both for you — do not call them again when the app uses `Plugin.Maui.Spine`.

---

## Adding SVG assets

All SVG files live in the `Images/` folder of the `Plugin.Maui.Spine.Svg` project and are declared as `<EmbeddedResource>` entries in the `.csproj`:

```xml
<None Remove="Images\MyIcon.svg" />
<!-- In the EmbeddedResource group: -->
<EmbeddedResource Include="Images\MyIcon.svg" />
```

After adding, the icon is available by short name (e.g. `"myicon.svg"`) — no other registration is needed, and both the image source and the icon service can find it.

---

## XAML usage

`Plugin.Maui.Spine.Svg` is mapped to the global MAUI xmlns via `GlobalXmlns.cs` in the consuming app, so no explicit `xmlns` alias is needed.

### ImageButton with style

```xml
<ImageButton
    SvgImageSource.Svg="settings.svg"
    WidthRequest="48"
    HeightRequest="48"
    Style="{StaticResource SvgImageButtonStyle}" />
```

`SvgImageButtonStyle` (defined in the app's `Styles.xaml`) sets `SvgImageSource.EnableSvg`, `SvgImageSource.LightTintColor`, and `SvgImageSource.DarkTintColor`.

### Manual tint

```xml
<ImageButton
    SvgImageSource.Svg="settings.svg"
    SvgImageSource.EnableSvg="True"
    SvgImageSource.LightTintColor="Blue"
    SvgImageSource.DarkTintColor="LightBlue"
    WidthRequest="48"
    HeightRequest="48" />
```

### Image control

```xml
<Image
    SvgImageSource.Svg="logo.svg"
    SvgImageSource.EnableSvg="True"
    WidthRequest="64"
    HeightRequest="64" />
```

---

## Attached properties reference

| Property | Type | Default | Description |
|---|---|---|---|
| `SvgImageSource.Svg` | `string` | `null` | Short SVG filename (e.g. `"fish.svg"`) |
| `SvgImageSource.EnableSvg` | `bool` | `false` | Must be `true` to activate SVG rendering |
| `SvgImageSource.LightTintColor` | `Color` | `Transparent` | Tint applied in light theme |
| `SvgImageSource.DarkTintColor` | `Color` | `Transparent` | Tint applied in dark theme |
| `SvgImageSource.Padding` | `Thickness` | `5` | Padding inside the rendered bitmap |

Setting `Svg` or tint properties while `EnableSvg` is already `true` automatically re-renders the image.

---

## Programmatic rendering (C#)

Use `SvgBitmapLoader` directly when you need an `ImageSource` outside of XAML:

```csharp
var registry = IPlatformApplication.Current.Services.GetRequiredService<ResourceNameCache>();
var resourceName = registry.Resolve("settings.svg") ?? "settings.svg";

// Render at explicit size (points) with optional tint and padding
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(
    resourceName, width: 48, height: 48, tint: Colors.Black);

// With padding
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(
    resourceName, 48, 48, Colors.Black, new Thickness(4));
```

---

## Density

Sizes are points (dp on Android). `SvgBitmapLoader` renders the bitmap for the screen it will be shown on and returns a `SvgBitmapImageSource` that carries the scale: a 44-point icon on a 3× iPhone is a 132-pixel PNG decoded back to 44 points, so every pixel is its own. `UseEmbeddedSvgImages()` registers the image source service that does the decoding on iOS, Mac Catalyst and Android. Windows renders at 1×, because a WinUI `BitmapImage` decoded from a stream shows its own pixels.

---

## Theme awareness

The `SvgImageSourceBehavior` automatically re-renders when the app theme changes between light and dark. Set both `LightTintColor` and `DarkTintColor` for seamless theme transitions.

---

## Icon files

Generated icons are cached on disk using SHA-256 keys, so repeated calls return instantly.

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

### `PlatformIconKind`

| Value | Description |
|---|---|
| `Tray` | System tray / dock icon |
| `AppIcon` | Application window icon |

### `PlatformIconOptions` reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Tint` | `Color` | `Colors.Black` | Colour blended over the SVG via `SrcIn` blend mode |
| `Padding` | `Thickness` | `new Thickness(0)` | Inset inside the rendered bitmap |
| `Sizes` | `IReadOnlyList<int>?` | `null` (platform default) | Override rendered pixel sizes |
| `PngQuality` | `int` | `100` | PNG compression quality (0–100) |

### Disk cache

Icons are cached at:

```
%LOCALAPPDATA%/SvgIconCache/<appName>/
```

(`Shared` is used when `appName` is `null`.)

The cache key is a SHA-256 hash of the SVG file bytes, resource name, platform identifier, icon kind, and render options (tint, padding, quality, sizes). If the file already exists at the computed path, it is returned immediately without re-rendering.

---

## Key classes

| Class | Role |
|---|---|
| `SvgImageSource` | Static class with attached bindable properties for XAML |
| `SvgImageSourceBehavior` | `Behavior<View>` — hooks into `Image`/`ImageButton`, renders SVG at view size |
| `SvgBitmapLoader` | Static renderer; SVG → PNG via SkiaSharp with in-memory cache |
| `ResourceNameCache` | Singleton; scans embedded resources and resolves short filenames |
| `SvgIconService` | Resolves, renders, caches, and returns icon file paths |
| `PlatformIconKind` | `Tray` or `AppIcon` |
| `PlatformIconOptions` | Render options for icon files: tint, padding, sizes, quality |

---

## Do not

- Do **not** use a returned icon path as a MAUI `ImageSource` — it is a raw file path for platform-native icon APIs.
- Do **not** call `SvgIconService.GetOrCreateAsync` on the UI thread — it performs file I/O and should be awaited from a background context.
- Do **not** use `SvgIconService` for inline MAUI view images — use the `SvgImageSource` attached properties instead.
- Do **not** call `UseEmbeddedSvgImages()` or `UseSvgIcon()` when the app uses `Plugin.Maui.Spine` — `UseSpine()` already calls both.
