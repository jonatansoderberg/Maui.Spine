# GitHub Copilot Instructions — Plugin.Maui.SvgImage

## Project overview

`Plugin.Maui.SvgImage` is a .NET 10 MAUI class library that renders SVG files embedded in its own assembly as bitmaps for MAUI `Image` and `ImageButton` controls. Rendering is done with **SkiaSharp** + **Svg.Skia**. Tinting, padding, and automatic light/dark theme switching are supported.

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

Add both entries to `Plugin.Maui.SvgImage.csproj`:

```xml
<None Remove="Images\MyIcon.svg" />
<!-- ... in the EmbeddedResource group: -->
<EmbeddedResource Include="Images\MyIcon.svg" />
```

After adding, the icon is immediately available by short name (e.g. `"myicon.svg"`) — no other registration is needed.

---

## XAML usage

`Plugin.Maui.SvgImage` is mapped to the global MAUI xmlns via `GlobalXmlns.cs` in the consuming app. **Do not** add an explicit `xmlns:svg` alias.

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

- Do **not** place SVG files outside `Plugin.Maui.SvgImage/Images/` — `ResourceNameCache` only scans this assembly.
- Do **not** declare `<MauiImage>` items for SVGs — they must be `<EmbeddedResource>`.
- Do **not** use an explicit `xmlns:svg` alias in XAML — the namespace is already in the global xmlns.
- Do **not** call `UseEmbeddedSvgImages()` when using `Plugin.Maui.Spine` — it is already called by `UseSpine()`.
- Do **not** pass the full resource name (e.g. `Plugin.Maui.SvgImage.Images.Fish.svg`) to `SvgImageSource.Svg` — always use the short filename.
- Do **not** call `ResourceNameCache.Initialize()` manually — it is called once during `UseEmbeddedSvgImages()`.
