# Custom Transitions

Spine uses an `ISpineTransitions` implementation to animate pages as they enter and leave the navigation stack. The built-in `DefaultSpineTransitions` moves pages the way iOS's navigation does, on every platform, but you can replace or extend it with your own implementation.

---

## Default animations

| Direction | Upper page | Lower page |
|---|---|---|
| Set root | Shown immediately, no animation | — |
| Forward (push) | The new page slides in from the right | The current page moves a quarter of the way left and dims |
| Back (pop) | The current page slides out to the right | The previous page moves back in from the left as its dim lifts |
| Interactive swipe | Follows the finger | Follows the finger, as in a pop |

The back button and a completed back-swipe play the same motion. On iOS 26 and later the moving page has the screen's rounded corners, as UIKit's own pages do; on other platforms it stays square.

Inside a sheet the pages sit on the sheet's own surface, which a moving page cannot carry. There the upper page stays see-through and the page underneath is cut off where the upper page begins, as UIKit does, so only the sheet shows under the upper page; nothing is dimmed. On iOS 26 the upper page has the sheet's rounded corners.

Animation duration is platform-aware: 300 ms on iOS/Android/Mac, 250 ms on Windows.

To carry a view from one page to the next, or zoom a page out of the view it opens from, tag the views rather than writing a transition: see [Shared Elements and Zoom](transitions.md). A shared element flies over whatever motion the region's `ISpineTransitions` plays, at its pace.

---

## Layers

A push or a pop moves **layers**, not pages. `AnimatePushAsync` and `AnimatePopAsync` receive a `SpineTransitionContext`:

| Member | What it is |
|---|---|
| `Front` | The upper layer: the page arriving on a push, the page leaving on a pop. It hides what is under it — it carries its page's background, or in a sheet the lower page is cut off at its leading edge — and on iOS 26 it has the screen's or the sheet's rounded corners while it moves. |
| `Back` | The lower layer: the page being covered on a push, the page coming back on a pop. |
| `Dim` | A black layer between them, at opacity 0 at rest. |
| `Width` | The width of the region: the distance a page travels to leave it. |

Each layer reaches the edges of the screen, under the status bar and the home indicator, so moving a layer moves the whole page. All layers are at rest when the transition starts and are put back at rest after it (translation 0, opacity 1, the dim at 0), so a transition only has to move them.

---

## Animating on the platform's engine

`SpineTranslateToAsync` and `SpineFadeToAsync` (in `Plugin.Maui.Spine.Extensions`) take the same arguments as MAUI's `TranslateToAsync` and `FadeToAsync`. On iOS and Mac Catalyst they run in Core Animation, outside the app's main thread, so the motion keeps the display's frame rate while the page arriving is still loading. On Android and Windows, and for an easing UIKit cannot express (anything but `Linear`, the `Sin…` and the `Cubic…` easings), they fall back to MAUI's own animation. `DefaultSpineTransitions` uses them for every motion.

---

## Implementing custom transitions

Create a class that implements `ISpineTransitions` and animate the layers:

```csharp
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;

namespace MyApp;

public sealed class FadeTransitions : ISpineTransitions
{
    private const uint Duration = 250;

    public Task AnimateSetRootAsync(View view)
    {
        view.Opacity = 1;
        view.IsVisible = true;
        return Task.CompletedTask;
    }

    // The new page fades in over the current one.
    public Task AnimatePushAsync(SpineTransitionContext transition)
    {
        transition.Front.Opacity = 0;
        return transition.Front.SpineFadeToAsync(1, Duration, Easing.CubicOut);
    }

    // The current page fades out, uncovering the previous one.
    public Task AnimatePopAsync(SpineTransitionContext transition) =>
        transition.Front.SpineFadeToAsync(0, Duration, Easing.CubicIn);

    // The per-page methods are only called by the default AnimatePushAsync / AnimatePopAsync,
    // which this class replaces.
    public Task AnimateNavigateToShowAsync(View view) => Task.CompletedTask;
    public Task AnimateNavigateToHideAsync(View view) => Task.CompletedTask;
    public Task AnimateBackShowAsync(View view) => Task.CompletedTask;
    public Task AnimateBackHideAsync(View view) => Task.CompletedTask;

    // Called when the user completes an interactive back-swipe
    public Task AnimateInteractiveBackCompleteAsync(View front, View back, double progress) =>
        Task.WhenAll(
            front.SpineTranslateToAsync(front.Width, 0, Duration, Easing.CubicOut),
            back.SpineTranslateToAsync(0, 0, Duration, Easing.CubicOut));

    // Called when the user cancels an interactive back-swipe
    public Task AnimateInteractiveBackCancelAsync(View front, View back) =>
        Task.WhenAll(
            front.SpineTranslateToAsync(0, 0, Duration, Easing.CubicOut),
            back.SpineTranslateToAsync(-front.Width * 0.25, 0, Duration, Easing.CubicOut));
}
```

### Per-page methods

An implementation that only implements the per-page methods (`AnimateNavigateToShowAsync`, `AnimateNavigateToHideAsync`, `AnimateBackShowAsync`, `AnimateBackHideAsync`) keeps working: the default `AnimatePushAsync` and `AnimatePopAsync` call them with the page views inside the layers, and leave the upper layer transparent and square so a page hidden in it uncovers the page under it. These methods move a page inside its layer, which is inset by the safe area, so they cannot move the status bar area with the page.

---

## Subclassing `DefaultSpineTransitions`

If you only want to tweak the default motion, subclass `DefaultSpineTransitions`. `AnimatePushAsync`, `AnimatePopAsync` and the interactive methods are `virtual`; `Duration`, `Ease`, `Parallax` (how far the lower page moves aside, 0.25) and `DimOpacity` are `protected virtual` properties:

```csharp
using Plugin.Maui.Spine.Presentation;

namespace MyApp;

public sealed class SlowerTransitions : DefaultSpineTransitions
{
    // Half the speed on all platforms
    protected override uint Duration => base.Duration * 2;

    // No parallax: the covered page stays where it is
    protected override double Parallax => 0;
}
```

The per-page methods on `DefaultSpineTransitions` are marked `[Obsolete]`: Spine no longer calls them there, so overriding one has no effect, and the compiler says so.

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
| `AnimatePushAsync(transition)` | Forward navigation | Move the layers; by default calls the two per-page forward methods |
| `AnimatePopAsync(transition)` | Back navigation, back button, pop to a page | Move the layers; by default calls the two per-page back methods |
| `AnimateNavigateToShowAsync(view)` | From the default `AnimatePushAsync` | Animate the incoming page into view |
| `AnimateNavigateToHideAsync(view)` | From the default `AnimatePushAsync` | Animate the outgoing page out of view |
| `AnimateBackShowAsync(view)` | From the default `AnimatePopAsync` | Animate the previous page back into view |
| `AnimateBackHideAsync(view)` | From the default `AnimatePopAsync` | Animate the current page out of view |
| `AnimateInteractiveBackCompleteAsync(front, back, progress)` | Swipe gesture completed | Finish the pop from the current drag offset |
| `AnimateInteractiveBackCancelAsync(front, back)` | Swipe gesture canceled | Move both layers back to their resting positions |
| `InteractiveGestureDuration` | Swipe released | Duration in ms of the dim's fade (default: `250`) |
| `InteractiveGestureEasing` | Swipe released | Easing of the dim's fade (default: `CubicOut`) |

---

## Tips

- Move layers with `SpineTranslateToAsync` / `SpineFadeToAsync` rather than MAUI's methods to get the native engine on Apple platforms.
- Use `Task.WhenAll` to run the layers' animations together.
- The `front` and `back` of the interactive methods are the same layers as in `SpineTransitionContext`. `progress` is the translation offset in device-independent units at the moment the finger was lifted.
- In `DefaultSpineTransitions`, `InteractiveGestureDuration` and `InteractiveGestureEasing` mirror `Duration` and `Ease`, so overriding those two keeps the swipe's release in step with the layers.
