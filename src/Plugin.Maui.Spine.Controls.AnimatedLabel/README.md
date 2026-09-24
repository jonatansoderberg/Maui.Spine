# Plugin.Maui.Spine.Controls.AnimatedLabel

`AnimatedLabel` is a SkiaSharp label for .NET MAUI that scrolls (marquee) or fades text that does not fit its width.

```bash
dotnet add package Plugin.Maui.Spine.Controls.AnimatedLabel
```

`UseSpine()` registers the control. An app without Spine calls `UseAnimatedLabel()`:

```csharp
using Plugin.Maui.Spine.Controls;

builder
    .UseMauiApp<App>()
    .UseAnimatedLabel();   // not needed with UseSpine()
```

```xml
<controls:AnimatedLabel Text="{Binding NowPlaying}" FontSize="16" />
```

Platforms: Android, iOS, Mac Catalyst, Windows.

## Documentation

- [AnimatedLabel](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/animated-label.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
