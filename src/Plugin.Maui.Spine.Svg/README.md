# Plugin.Maui.Spine.Svg

SVG rendering for .NET MAUI on SkiaSharp: embedded SVG files as image sources with tinting and light/dark switching, an icon service that hands the same SVG to the header bar and the tab bar, and SVG-to-icon conversion (`.ico`, `.png`) for tray and window icons. `Plugin.Maui.Spine` depends on this package; it also works on its own.

```bash
dotnet add package Plugin.Maui.Spine.Svg
```

```csharp
using Plugin.Maui.Spine.Svg;

builder
    .UseMauiApp<App>()
    .UseEmbeddedSvgImages(typeof(MauiProgram).Assembly);   // UseSpine() does this for you
```

```xml
<!-- MyApp.csproj -->
<EmbeddedResource Include="Resources\Svg\*.svg" />

<!-- Resolved by file name across every registered assembly -->
<ImageButton
    SvgImageSource.Svg="settings.svg"
    SvgImageSource.EnableSvg="True"
    SvgImageSource.LightTintColor="Black"
    SvgImageSource.DarkTintColor="White"
    WidthRequest="48" HeightRequest="48" />
```

```csharp
var icon = svgIconService.FromEmbeddedSvg("settings.svg");
```

A ready-made icon set is in `Plugin.Maui.Spine.Svg.Icons`; reference it and its files resolve like the app's own.

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [SVG](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/svg.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
