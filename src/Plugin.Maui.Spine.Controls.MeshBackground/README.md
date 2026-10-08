# Plugin.Maui.Spine.Controls.MeshBackground

`MeshBackground` is a mesh gradient for .NET MAUI, drawn with SkiaSharp: a grid of coloured points blended smoothly into each other, still or drifting slowly. It is a background for glass and blur surfaces, heroes, onboarding and empty states. The colours come from the app's accent or a preset (`Aurora`, `Sunset`), in a light and a dark version, or from your own list.

```bash
dotnet add package Plugin.Maui.Spine.Controls.MeshBackground
```

`UseSpine()` registers the control. An app without Spine calls `UseMeshBackground()`:

```csharp
using Plugin.Maui.Spine.Controls;

builder
    .UseMauiApp<App>()
    .UseMeshBackground();   // not needed with UseSpine()
```

```xml
<Grid>
    <MeshBackground Preset="Aurora" Drift="Slow" />
    <Border Material.Kind="Glass" StrokeShape="RoundRectangle 24" Padding="20" VerticalOptions="Center">
        <Label Text="Over the aurora" />
    </Border>
</Grid>
```

While it drifts it redraws at most 30 times a second, and not at all off screen or in the background; with Reduce Motion on it stays still.

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [MeshBackground](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/mesh-background.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
