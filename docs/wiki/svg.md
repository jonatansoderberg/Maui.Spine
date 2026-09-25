# SVG

```bash
dotnet add package Plugin.Maui.Spine.Svg
```

`Plugin.Maui.Spine.Svg` does two things with SVG files embedded in an app, or in any assembly it is told about:

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
| Mac Catalyst | ✅ Supported | `.png` (largest size): 18, 36 |

---

## Registration

Call `UseEmbeddedSvgImages()` in your `MauiProgram.cs` builder chain:

```csharp
using Plugin.Maui.Spine.Svg;

builder
    .UseMauiApp<App>()
    .UseEmbeddedSvgImages();
```

This registers the `ResourceNameCache` singleton that resolves short SVG filenames to full embedded resource names. The whole file name must match, case-insensitively: `lock.svg` finds `App.Images.Lock.svg` and never `Clock.svg` or `Unlock.svg`. A name found in more than one assembly or folder resolves to the shortest resource name and is reported in the debug output.

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

`builder.UseSpine()` calls both for you. With Spine, call `UseSvgIcon(o => …)` only to change the options — before or after `UseSpine()`, every call configures the same `SvgIconOptions`. `UseEmbeddedSvgImages(...)` is additive: a later call scans only assemblies not seen before.

---

## Adding SVG assets

Embed the app's own SVG files and pass the assembly to `UseEmbeddedSvgImages` (or to `UseSpine`'s `AddAssembly`, which does the same):

```xml
<!-- MyApp.csproj -->
<EmbeddedResource Include="Resources\Svg\*.svg" />
```

After that an icon is available by short name (e.g. `"myicon.svg"`) — no other registration is needed, and both the image source and the icon service can find it. The name is matched against the end of the manifest resource name, so the folder it sits in does not matter.

### The built-in icon set

`Plugin.Maui.Spine.Svg.Icons` carries 220 ready-made icons — UI glyphs, status and content symbols, rooms and appliances, media controls, weather symbols and status badges:

```bash
dotnet add package Plugin.Maui.Spine.Svg.Icons
```

Referencing it is all it takes: `Plugin.Maui.Spine.Svg` loads the assembly by name at startup and its files resolve like the app's own. `SpineIcons` lists every file name as a constant (`SpineIcons.Bell` is `"Bell.svg"`); plain strings work just as well. `SpineIcons.All` holds every name, for a picker or a gallery. The package is `net10.0` and holds nothing but the SVG files and those constants; for a trimmed build its targets root the assembly so it is kept.

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

The image is tinted black in the light theme and white in the dark one, which suits a monochrome
icon.

### Tint and the SVG's own colours

An SVG that paints with `currentColor` takes the tint only there; every other colour in it stays.
The weather symbols in `Plugin.Maui.Spine.Svg.Icons` work this way: the outline follows the theme
(or `TintColor`) and the sun stays yellow.

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 50 50">
  <circle cx="18" cy="18" r="8" stroke="currentColor" stroke-width="2" fill="#fcff00" />
  <path d="M20 38h18a7 7 0 0 0 0-14" stroke="currentColor" stroke-width="2" fill="none" />
</svg>
```

An SVG with no `currentColor` is tinted whole, as above. Without a tint (`Transparent`),
`currentColor` is black.

### Full-colour SVG

A logo, a flag or any SVG that should keep its own colours needs both tints set to `Transparent`.
Any colour with an alpha of 0 counts as no tint:

```xml
<Image
    SvgImageSource.Svg="club-logo.svg"
    SvgImageSource.LightTintColor="Transparent"
    SvgImageSource.DarkTintColor="Transparent"
    SvgImageSource.Padding="0"
    WidthRequest="64"
    HeightRequest="64" />
```

### Dark tones for the SVG's own colours

An SVG's own colours are usually chosen for a light background. In the dark theme,
`AdjustColorsForDark` gives them dark tones; the tint is never changed.

- **Your own pairs first.** `DarkColors` maps a light colour to the exact dark tone you want, the way
  `SpineAccent` pairs a light and a dark accent. Colours match on red, green and blue; the SVG's alpha
  is kept.
- **The automatic rule for the rest.** Dark neutrals flip to light, so black ink becomes white.
  Dark colours lift to a mid lightness with their hue kept, so navy becomes a clear blue rather than a
  pastel. Every colour also loses `DarkColorMuting` of its chroma (default `0.15`), so a vivid red turns
  more matte.

Set the app-wide default and the pairs in `MauiProgram`, after `UseSpine()`:

```csharp
builder.UseEmbeddedSvgImages(options =>
{
    options.AdjustColorsForDark = true;
    options.DarkColors[Color.FromArgb("#D32F2F")] = Color.FromArgb("#C0605C");
    options.DarkColorMuting = 0.15;
});
```

Then turn it on or off per image, which overrides the app-wide default:

```xml
<Image SvgImageSource.Svg="crest.svg" SvgImageSource.AdjustColorsForDark="True" />
```

Only colours the SVG sets itself change (`fill`, `stroke`, `color`, gradient `stop-color`, from
attributes or styles), plus the default black of an unfilled shape. An SVG with no `currentColor` is
tinted whole, so it has no own colours to adjust. Tray and window icons take the same option through
`SvgIconOptions.AdjustColorsForDark`.

### Line width

The strokes scale with the image, so an icon drawn with 2-unit lines in a 50-unit view box has
hairlines at 16 points and heavy lines at 96. `LineWidthScale` multiplies every stroke width: above
`1` for a small icon, below for a large one.

```xml
<Image SvgImageSource.Svg="Settings.svg" WidthRequest="20" HeightRequest="20" SvgImageSource.LineWidthScale="1.5" />
<Image SvgImageSource.Svg="Settings.svg" WidthRequest="96" HeightRequest="96" SvgImageSource.LineWidthScale="0.6" />
```

It covers widths set in attributes and styles and the default width of 1. `SvgIconOptions.LineWidthScale`
does the same for tray and window icons.

---

## Attached properties reference

| Property | Type | Default | Description |
|---|---|---|---|
| `SvgImageSource.Svg` | `string` | `null` | Short SVG filename (e.g. `"fish.svg"`) |
| `SvgImageSource.EnableSvg` | `bool` | `false` | Must be `true` to activate SVG rendering |
| `SvgImageSource.LightTintColor` | `Color` | `Black` | Tint applied in light theme; `Transparent` keeps the SVG's own colours |
| `SvgImageSource.DarkTintColor` | `Color` | `White` | Tint applied in dark theme; `Transparent` keeps the SVG's own colours |
| `SvgImageSource.Padding` | `Thickness` | `5` | Padding inside the rendered bitmap |
| `SvgImageSource.AdjustColorsForDark` | `bool?` | `null` | Dark tones for the SVG's own colours in the dark theme; `null` follows `SvgImageOptions.AdjustColorsForDark` |
| `SvgImageSource.LineWidthScale` | `double` | `1` | Multiplies every stroke width |

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
| `Tint` | `Color` | `Colors.Black` | Colour of the SVG's `currentColor` parts, or of the whole SVG when it has none |
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
| `SvgImageOptions` | App-wide options: `AdjustColorsForDark`, `DarkColors`, `DarkColorMuting` |
| `ResourceNameCache` | Singleton; scans embedded resources and resolves short filenames |
| `SvgIconService` | Resolves, renders, caches, and returns icon file paths |
| `PlatformIconKind` | `Tray` or `AppIcon` |
| `PlatformIconOptions` | Render options for icon files: tint, padding, sizes, quality |

---

## Do not

- Do **not** use a returned icon path as a MAUI `ImageSource` — it is a raw file path for platform-native icon APIs.
- Do **not** call `SvgIconService.GetOrCreateAsync` on the UI thread — it performs file I/O and should be awaited from a background context.
- Do **not** use `SvgIconService` for inline MAUI view images — use the `SvgImageSource` attached properties instead.
- Do **not** call `UseEmbeddedSvgImages()` or `UseSvgIcon()` when the app uses `Plugin.Maui.Spine` except to pass options or extra assemblies — `UseSpine()` already calls both.
