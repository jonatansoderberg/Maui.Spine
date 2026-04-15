# SvgImage

`Plugin.Maui.SvgImage` renders SVG files embedded in the library assembly as bitmaps for MAUI `Image` and `ImageButton` controls. Rendering is powered by **SkiaSharp** + **Svg.Skia** and supports tinting, padding, and automatic light/dark theme switching.

---

## Platforms

| Platform | Status |
|---|---|
| Android | ✅ Supported |
| Windows (WinUI 3) | ✅ Supported |
| iOS | 🚧 In progress |
| macOS Catalyst | 🚧 In progress |

---

## Registration

Call `UseEmbeddedSvgImages()` in your `MauiProgram.cs` builder chain:

```csharp
using Plugin.Maui.SvgImage;

builder
    .UseMauiApp<App>()
    .UseEmbeddedSvgImages();
```

This registers the `ResourceNameCache` singleton that resolves short SVG filenames to full embedded resource names.

---

## Adding SVG assets

All SVG files live in the `Images/` folder of the `Plugin.Maui.SvgImage` project and are declared as `<EmbeddedResource>` entries in the `.csproj`:

```xml
<None Remove="Images\MyIcon.svg" />
<!-- In the EmbeddedResource group: -->
<EmbeddedResource Include="Images\MyIcon.svg" />
```

After adding, the icon is available by short name (e.g. `"myicon.svg"`) — no other registration is needed.

---

## XAML usage

`Plugin.Maui.SvgImage` is mapped to the global MAUI xmlns via `GlobalXmlns.cs` in the consuming app, so no explicit `xmlns` alias is needed.

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

// Render at explicit size with optional tint and padding
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(
    resourceName, width: 48, height: 48, tint: Colors.Black);

// With padding
ImageSource? source = SvgBitmapLoader.LoadFromEmbedded(
    resourceName, 48, 48, Colors.Black, new Thickness(4));
```

---

## Theme awareness

The `SvgImageSourceBehavior` automatically re-renders when the app theme changes between light and dark. Set both `LightTintColor` and `DarkTintColor` for seamless theme transitions.

---

## Key classes

| Class | Role |
|---|---|
| `SvgImageSource` | Static class with attached bindable properties for XAML |
| `SvgImageSourceBehavior` | `Behavior<View>` — hooks into `Image`/`ImageButton`, renders SVG at view size |
| `SvgBitmapLoader` | Static renderer; SVG → PNG via SkiaSharp with in-memory cache |
| `ResourceNameCache` | Singleton; scans embedded resources and resolves short filenames |
