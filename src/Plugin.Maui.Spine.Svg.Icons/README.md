# Plugin.Maui.Spine.Svg.Icons

A set of 166 embedded SVG icons for `Plugin.Maui.Spine.Svg`: UI glyphs (arrows, close, settings, refresh, plus, minus, edit, delete), rooms and appliances, media controls, weather symbols and status badges. Reference the package and the icons resolve by file name, the same way as SVGs embedded in your own app; no registration call is needed.

```bash
dotnet add package Plugin.Maui.Spine.Svg.Icons
```

```xml
<ImageButton
    SvgImageSource.Svg="Bell.svg"
    SvgImageSource.EnableSvg="True"
    WidthRequest="48" HeightRequest="48" />
```

```csharp
using Plugin.Maui.Spine.Svg.Icons;

var icon = svgIconService.FromEmbeddedSvg(SpineIcons.Bell);   // "Bell.svg"
```

`SpineIcons` lists every file name as a constant. Plain strings work just as well.

The package targets `net10.0` and carries no code beyond the constants, so it adds nothing but the SVG files to an app. `Plugin.Maui.Spine.Svg` loads it by assembly name at startup; for a trimmed build the package's targets root the assembly so it is kept.

## Documentation

- [SVG](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/svg.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
