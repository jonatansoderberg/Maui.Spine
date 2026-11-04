# Plugin.Maui.Spine.Symbols.Tool

The `spine-symbols` command: turns monochrome SVGs into SF Symbols custom symbols, so an app's own icons can stand in a Control Center control on iOS, which draws only symbols. Spine's own icons are already built into the widget extension.

```bash
dotnet tool install -g Plugin.Maui.Spine.Symbols.Tool
```

```bash
spine-symbols Platforms/iOS/AppSymbols.xcassets Resources/Images/puck.svg Resources/Images/whistle.svg
```

- Each SVG becomes `<name>.symbolset` in the catalog: strokes are outlined and joined with the fills, and written as the variable template (Ultralight, Regular and Black, small) that Control Center needs.
- An SVG that draws nothing is reported and skipped, and the command exits with 1.
- Point the app at the catalog with `<SpineWidgetsAssetCatalog>Platforms/iOS/AppSymbols.xcassets</SpineWidgetsAssetCatalog>`. Asset names are case-sensitive.

Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md#controls
