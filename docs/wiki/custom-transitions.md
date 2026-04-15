# Custom Transitions

Spine uses an `ISpineTransitions` implementation to animate pages as they enter and leave the navigation stack. The built-in `DefaultSpineTransitions` provides platform-tuned slide animations, but you can replace or extend it with your own implementation.

---

## Default animations

The built-in transitions slide pages horizontally:

| Direction | Incoming page | Outgoing page |
|---|---|---|
| Set root | Shown immediately, no animation | — |
| Forward (push) | Slides in from the right | Hidden immediately |
| Back (pop) | Slides back from left offset | Hidden immediately |
| Interactive swipe | Tracked in real-time with the user's finger | Follows gesture |

Animation duration is platform-aware: 300 ms on iOS/Android/Mac, 250 ms on Windows.

---

## Implementing custom transitions

Create a class that implements `ISpineTransitions`:

```csharp
using Plugin.Maui.Spine.Presentation;

namespace MyApp;

public sealed class FadeTransitions : ISpineTransitions
{
    private const uint Duration = 250;

    // Initial root page — no animation by default
    public Task AnimateSetRootAsync(View view)
    {
        view.Opacity = 1;
        view.IsVisible = true;
        return Task.CompletedTask;
    }

    // Incoming page during forward navigation
    public async Task AnimateNavigateToShowAsync(View view)
    {
        view.Opacity = 0;
        view.IsVisible = true;
        await view.FadeTo(1, Duration, Easing.CubicOut);
    }

    // Outgoing page during forward navigation
    public async Task AnimateNavigateToHideAsync(View view)
    {
        await view.FadeTo(0, Duration, Easing.CubicIn);
        view.IsVisible = false;
    }

    // Incoming (previous) page during back navigation
    public async Task AnimateBackShowAsync(View view)
    {
        view.Opacity = 0;
        view.IsVisible = true;
        await view.FadeTo(1, Duration, Easing.CubicOut);
    }

    // Outgoing (current) page during back navigation
    public async Task AnimateBackHideAsync(View view)
    {
        await view.FadeTo(0, Duration, Easing.CubicIn);
        view.IsVisible = false;
    }

    // Called when the user completes an interactive back-swipe
    public async Task AnimateInteractiveBackCompleteAsync(View front, View back, double progress)
    {
        await Task.WhenAll(
            front.FadeTo(0, Duration, Easing.CubicIn),
            back.FadeTo(1, Duration, Easing.CubicOut));
        front.IsVisible = false;
    }

    // Called when the user cancels an interactive back-swipe
    public async Task AnimateInteractiveBackCancelAsync(View front, View back)
    {
        await Task.WhenAll(
            front.FadeTo(1, Duration, Easing.CubicOut),
            back.FadeTo(0, Duration, Easing.CubicIn));
        back.IsVisible = false;
    }
}
```

---

## Subclassing `DefaultSpineTransitions`

If you only want to tweak one or two animations, subclass `DefaultSpineTransitions` and override just the methods you need. All methods are `virtual` and `Duration` / `Ease` are `protected virtual` properties:

```csharp
using Plugin.Maui.Spine.Presentation;

namespace MyApp;

public sealed class SlowerTransitions : DefaultSpineTransitions
{
    // Double the animation speed on all platforms
    protected override uint Duration => base.Duration * 2;

    // Override only the push-in animation
    public override async Task AnimateNavigateToShowAsync(View view)
    {
        view.Opacity = 0;
        view.IsVisible = true;
        await Task.WhenAll(
            view.FadeTo(1, Duration, Easing.CubicOut),
            view.TranslateToAsync(0, 0, Duration, Easing.CubicOut));
    }
}
```

---

## Platform-specific implementations

Register different implementations per platform in `MauiProgram.cs`:

```csharp
builder.UseSpine(options =>
{
    options.AddAssembly(typeof(MauiProgram).Assembly);
});

#if IOS || MACCATALYST
builder.Services.AddSingleton<ISpineTransitions, MyIosTransitions>();
#elif WINDOWS
builder.Services.AddSingleton<ISpineTransitions, MyWindowsTransitions>();
#endif
```

Because `AddSingleton` is called after `UseSpine`, it overwrites the default `DefaultSpineTransitions` registration.

---

## Registering a fully custom implementation

```csharp
builder.UseSpine(options =>
{
    options.AddAssembly(typeof(MauiProgram).Assembly);
});

// Override the default ISpineTransitions with your custom implementation
builder.Services.AddSingleton<ISpineTransitions, FadeTransitions>();
```

---

## `ISpineTransitions` member reference

| Member | When called | Responsibility |
|---|---|---|
| `AnimateSetRootAsync(view)` | Initial root page | Show the first page (no-op by default) |
| `AnimateNavigateToShowAsync(view)` | Forward navigation | Animate incoming page into view |
| `AnimateNavigateToHideAsync(view)` | Forward navigation | Animate outgoing page out of view |
| `AnimateBackShowAsync(view)` | Back navigation | Animate previous page back into view |
| `AnimateBackHideAsync(view)` | Back navigation | Animate current page out of view |
| `AnimateInteractiveBackCompleteAsync(front, back, progress)` | Swipe gesture completed | Finish the pop animation from current drag offset |
| `AnimateInteractiveBackCancelAsync(front, back)` | Swipe gesture canceled | Animate both pages back to their resting positions |
| `InteractiveGestureDuration` | Swipe clip-reveal | Duration in ms for the gesture clip-reveal overlay (default: `250`) |
| `InteractiveGestureEasing` | Swipe clip-reveal | Easing for the gesture clip-reveal overlay (default: `CubicOut`) |

---

## Tips

- Always set `view.IsVisible = true` before starting an in-animation, and `false` after an out-animation completes.
- Use `Task.WhenAll` to run front and back animations in parallel for smoother results.
- The `progress` parameter in `AnimateInteractiveBackCompleteAsync` is the translation offset in device-independent units at the moment the finger was lifted — use it as a starting position for the completion animation.
- `InteractiveGestureDuration` and `InteractiveGestureEasing` control the clip-reveal overlay that tracks the back-swipe gesture. In `DefaultSpineTransitions` these mirror `Duration` and `Ease`, so overriding those two properties is sufficient to keep everything in sync.
