# AnimatedLabel

`Plugin.Maui.AnimatedLabel` provides a SkiaSharp-based label control that automatically scrolls (marquee) when the text is wider than the available space. It includes configurable scroll speed, pause duration, fade effects, and text-change animations.

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

Call `UseAnimatedLabel()` in your `MauiProgram.cs` builder chain. This registers the SkiaSharp renderers required by the control.

```csharp
using Plugin.Maui.AnimatedLabel;

builder
    .UseMauiApp<App>()
    .UseAnimatedLabel();
```

---

## XAML usage

```xml
<AnimatedLabel
    Text="{Binding SongTitle}"
    TextColor="White"
    FontSize="16"
    FontFamily="OpenSans-Regular"
    ScrollSpeedDpPerSecond="40"
    PauseAtEndsMs="1500"
    HeightRequest="24" />
```

> Make sure `Plugin.Maui.AnimatedLabel` is included in your global XAML namespace or add an explicit `xmlns` for the namespace.

---

## How it works

1. The label measures the text width against the control width.
2. If the text overflows by more than `ScrollThresholdDp`, a marquee animation starts automatically — the text scrolls from end to start and back, pausing at each end for `PauseAtEndsMs`.
3. When `EnableFadeOnTextChange` is `true`, the control cross-fades between the old and new text over `FadeDurationMs`.
4. Fade edges on the left and right mask text that is about to scroll in or out of view. The width of these edges is controlled by `FadeEdgeWidthDp`.

---

## Property reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string` | `null` | The text to display |
| `TextColor` | `Color` | `null` | Text colour |
| `FontSize` | `double` | `14` | Font size in scaled pixels |
| `FontFamily` | `string` | `null` | Font family name |
| `FontAttributes` | `FontAttributes` | `None` | Bold, italic, or both |
| `ScrollSpeedDpPerSecond` | `double` | `38` | Horizontal scroll speed (dp/s) |
| `PauseAtEndsMs` | `int` | `2000` | Pause duration at each end of the scroll (ms) |
| `FadeDurationMs` | `int` | `120` | Cross-fade duration on text change (ms) |
| `EnableScrolling` | `bool` | `true` | Enable or disable the marquee animation |
| `EnableFadeOnTextChange` | `bool` | `true` | Enable cross-fade when `Text` changes |
| `ResetOnTextUpdate` | `bool` | `true` | Reset scroll position when `Text` changes |
| `ScrollThresholdDp` | `double` | `2` | Minimum overflow (dp) before scrolling starts |
| `EndPaddingDp` | `double` | `2` | Extra padding at the end of the text before the scroll reverses |
| `FadeEdgeWidthDp` | `double` | `8` | Width of the left/right fade edges (dp) |

---

## Tips

- Set `EnableScrolling="False"` if you only want the fade-on-change effect without marquee scrolling.
- Use `ScrollSpeedDpPerSecond` and `PauseAtEndsMs` together to control the feel — a lower speed with a longer pause gives a gentler effect.
- The control renders via SkiaSharp (`SKCanvasView`), so it does not participate in the standard MAUI label layout. Set an explicit `HeightRequest` for best results.
