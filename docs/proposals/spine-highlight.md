# Spine.Controls.Highlight: glow, focus and tours (proposal)

**Status:** Proposal, 2026-10-09, with the owner's answers in [Decisions](#decisions-2026-10-09). Not started. Issue: [#509](https://github.com/jonatansoderberg/Maui.Spine/issues/509), part of the roadmap [#505](https://github.com/jonatansoderberg/Maui.Spine/issues/505). Related: the screen-edge visual in Spine.Voice, [#516](https://github.com/jonatansoderberg/Maui.Spine/issues/516). Live concept: https://claude.ai/artifact/J5TCSDk8Rk1vZNti9MBM3J (private until the owner shares it). Nothing here has been built or run; claims about Spine are read from the source on `origin/master` (1ab4b45), claims about the platforms come from their documentation.
**Question:** Can one package give any view a glow that says "the model is working on this", and also point a user at a control during onboarding (once, a few times, until tapped, or as a tour of steps)? How is each kind drawn, how does it behave under Reduce Motion and with a screen reader, and how can Spine.Voice drive the same edge glow from audio levels?
**Answer:** Yes, as `Plugin.Maui.Spine.Controls.Highlight`, built on the attached-property pattern that `Tap`, `Material` and `Motion` already use. Five of the six kinds (Edge, Pulse, Sheen, Trace, Breathe) are native layers on the target's platform view: Core Animation on Apple, a `ViewOverlay` drawable on Android, a Composition child visual on Windows. They animate on the platform's compositor where it has one, cost no layout pass and need no SkiaSharp. Spotlight is different: it is a window-level overlay above the header bar, tab bar and any open sheet, drawn as a MAUI `GraphicsView` with a MAUI tip view, placed by the mechanism `LightboxOverlay` already uses. "Once per install" is a key in `Preferences` under a shared name of its own. Highlight owns the window-level edge glow itself: `IHighlights.AttachEdge(window)` returns an `IEdgeGlow` whose `Intensity` is a `Func<float>` sampled per frame. Spine.Voice does not reference Highlight and Highlight does not reference Voice; the app wires the two in one line. The core needs four small generic hooks (§6.1), none of them about glow or voice.


## Decisions (2026-10-09)

The owner answered the open questions in §10, all as recommended:

1. In a tour, a tap anywhere goes to the next step and does not run the target's action.
2. Android Auto Backup may restore "seen" keys after a reinstall. That is accepted and documented.
3. Edge defaults to the spectrum, with the app's accent as an option.
4. Tips outside Spotlight are announced to screen readers only.
5. `ReducedMotion` and `SpineOverlay` become public in the core.
6. The window edge's corners on iOS use a table of radii per device class.

---

## 1. The conclusion in short

| Kind | What it says | Drawn with (iOS / Android / Windows) | Under Reduce Motion |
|---|---|---|---|
| **Edge** | A model is working on this card | Conic `CAGradientLayer` rotated, masked to the rim / `SweepGradient` stroke in a `ViewOverlay` drawable / rotating `CompositionLinearGradientBrush` on a stroked shape | A still gradient rim, fading in and out |
| **Pulse** | "Tap here" without covering the screen | Two `CAShapeLayer` rings, scale and fade / two rings in the overlay drawable / two `ShapeVisual` rings | One still ring, faded in once |
| **Spotlight** | Look at this, with a tip; a tour is steps of it | Window overlay: MAUI `GraphicsView` (dim with an even-odd hole) and a MAUI tip view, the same on all platforms | The hole jumps between steps; dim and tip cross-fade |
| **Sheen** | Quiet "new" marker | Linear `CAGradientLayer` moving across, masked to the shape / `LinearGradient` shader clipped to the shape / `CompositionLinearGradientBrush` offset | A soft tint fades in and out once |
| **Trace** | Progress without a number | `CAShapeLayer` stroke segment moving along the path (`strokeStart`/`strokeEnd`) / `PathMeasure` segment / `CompositionGeometry.TrimStart`/`TrimEnd` | A still rim at low opacity |
| **Breathe** | A state that lasts, such as a live microphone | Shadow layer with an animated radius and opacity / halo in the overlay drawable / `DropShadow` blur radius and opacity | A still halo |

| Question | Answer | Evidence |
|---|---|---|
| Skia or native? | **Native per kind, no Skia.** A Skia overlay needs a MAUI view in a layout, which `Skeleton` can only add to a `Layout` and not to a `Button`; it redraws a CPU bitmap per frame, which is why `MeshBackground` draws at half resolution and caps its frame rate. | `Skeleton.cs:110-114,127-135`, `MeshBackground.cs:19-24,70` |
| Where do the layers go? | **On the target's platform view, above its content**, as Tap's press overlay does. | `Tap.Apple.cs:143-146`, `Tap.Windows.cs:133-147`, `Tap.Android.cs:30-31` |
| How does Spotlight cover the header and tab bars? | **A view on the window**: key-window subview on Apple, a see-through `Dialog` on Android (sheets are dialogs), a `Popup` on the `XamlRoot` on Windows. | `LightboxOverlay.Apple.cs:15-25`, `LightboxOverlay.Android.cs:20-53`, `BottomSheetPageExtensions.Windows.cs:179-181` |
| Where is "seen" stored? | **`Preferences` with the shared name `spine.highlight`**, so a reset clears only highlight keys. Android Auto Backup can restore it after a reinstall (§8). | `BackgroundTaskService.cs:253,263`, `StringsSetup.cs:37` |
| Reduced motion? | **One public helper in the core** instead of the three copies that exist today. | `Core/ReducedMotion.cs:4`, `Controls.Shimmer/ReduceMotion.cs:12`, `Controls.MeshBackground/ReduceMotion.cs:8` |
| Voice's screen edge? | **The same Edge renderer, wired by the app**: `highlights.AttachEdge(window).Intensity = () => voice.Frame.OutputLevel;`. Neither package references the other, and the core has no contract for it. | §4, §7 |

---

## 2. What Spine already has

| Part | Where | What it means here |
|---|---|---|
| Attached property + per-view state that follows the handler | `Tap.cs:23-107` (`CommandProperty`, `StateProperty`), `TapState` `:113-243` (`HandlerChanging`/`HandlerChanged`, `ConnectPlatform`/`DisconnectPlatform` partials) | `HighlightState` takes the same shape: created on first set, connects to the platform view when there is one, disposes when cleared. |
| Mapper-driven attached properties | `Material.cs:89-92` (`MapperKey`, `Remap`), `MaterialExtensions.Apple.cs:23-25` (`AppendToMapping` on Border, ContentView, Layout) | The alternative for kinds that must survive a handler re-map. Highlight attaches to any `View`, including `Button`, so it follows the handler events like Tap instead of appending to every handler's mapper. |
| A layer above the content | `Tap.Apple.cs:143-146` (`CALayer` with `ZPosition = 10_000` on the host's layer), `Tap.Windows.cs:133-147` (`SetElementChildVisual`), `Tap.Android.cs:30-31` (`host.Foreground` ripple) | Highlight uses sublayers on Apple, `ViewOverlay` on Android (not `Foreground`, which Tap owns) and a Composition visual on Windows. **Windows allows one child visual per element**: Tap and Highlight on the same view would replace each other's. A shared container in the core fixes that (§6.1). |
| Shimmer's sweep | `SkeletonOverlay.cs:17` (`GraphicsView`), `:62-63` (input transparent, `ZIndex = 10_000`), `:372-379` (`Animation` at rate 24, one shared clock), `:85-89` (abort on handler loss: "MAUI can drop the page without raising Unloaded") | The lesson carries over: every running highlight stops on handler loss and detach, not only on `Unloaded`. The sweep itself runs on MAUI's ticker on the UI thread; Highlight's per-view kinds run on the compositor instead. |
| `Skeleton.IsActive` on real layouts | `Skeleton.cs:24-25,108-136` | Bindable to a busy flag, the model for `Highlight.IsActive`. It throws a clear message on a non-layout (`:112-113`); Highlight has no such limit because it draws in platform layers. |
| Material and glass | `Material.cs:5-24` (None, Blur, Glass), `MaterialExtensions.Apple.cs:92-112` (iOS 26 glass container moves the children into its `ContentView`), `MaterialExtensions.Android.cs:186-217` (blurred `RenderNode` snapshot of ancestors and earlier siblings), `MaterialExtensions.Windows.cs:76` (`AcrylicBrush`) | Highlight's sublayer stays on the host's own layer, outside the glass container's subviews, so it is drawn over the glass. On Android a glow on a view behind a blurred surface should end up in the snapshot (not verified). |
| MeshBackground | `MeshBackground.cs:5-8,35` (an `SKCanvasView` in a `ContentView`), `:65-68` (`FrameRate`, 30 by default) | A background that highlighted cards sit on. No interaction beyond drawing order. Its Skia dependency stays in its own package. |
| Reduced motion | `Core/ReducedMotion.cs:4-45` (internal, read at most once a second), Shimmer's copy with listeners and a 500 ms settle delay for Android (`ReduceMotion.cs:12-82`), MeshBackground's copy | Three copies of the same read. Highlight would be the fourth; the core should own one public helper with a change event (§6.1). |
| Motion and "give it room" | `Motion.cs:13-17` ("a view that moves 12 points needs 12 points to spare on each side"), `:52-59` (`IsSupported`, `IsEnabled`) | The same wording applies to halos: Pulse, Breathe and Edge's outer glow draw outside the view and are clipped by a parent that clips. |
| Haptics | `Haptics.cs:123` (`Play(Haptic)`) | A tour step can play `Haptic.Selection` when it moves on. |
| Window-level overlays | `LightboxOverlay.cs:28-48`, `.Apple.cs:15-25`, `.Android.cs:16-53`, `NavigationService.cs:404-411` | The attach code Spotlight needs exists, but is tied to a `NavigationRegion`. It should become a small public helper (§6.1). |
| Header actions and tabs as targets | `PageActionView.cs:17` (internal `ContentView` per `PageAction`), `SpineTabbedHostPage.Apple.cs:14-15` (`UITabBarController`), `SpineTabbedHostPage.Android.cs:10` (`BottomNavigationView`) | A tour step that points at "+" in the header bar or at a tab has no public view to point at. The core needs to answer "where is this action / tab on screen" (§6.1). |
| Strings and theme | `ShimmerStrings.cs:13-17` (`AddDefaults` from embedded XML), `SpineTheme.Track` (`SpineTheme.cs:16`), `SpineTheme.GetAccent` (`:28`) | Tour strings ("Next", "Skip", "Done", "Step 1 of 3") the same way; colours follow the theme and accent. |
| Screen reader | `Semantic.cs:32` (`Semantic.Merge`), `SkeletonOverlay.cs:337-349` (description "Loading") | No announce helper exists in Spine; MAUI's `SemanticScreenReader.Announce` and `SetSemanticFocus` are used directly. |
| Module registration | `MeshBackgroundExtensions.cs:12-22` (idempotent `UseMeshBackground`), `build/*.props` `<SpineModule>`, `SpineModules.Run` (`MauiAppBuilderExtensions.cs:158`) | `UseHighlights()` registers `IHighlights`; `UseSpine()` calls it. |

---

## 3. The platforms' building blocks

**iOS and Mac Catalyst.** `CAGradientLayer` has had `type = .conic` since iOS 12, so the Edge rim is a conic gradient in a square layer rotated by a `transform.rotation.z` animation, masked by a `CAShapeLayer` that strokes the rounded rect. Core Animation runs it in the render server: no C# per frame. iOS has no public layer blur (`CALayer.filters` is macOS only), so the soft outer glow is the same rotating gradient masked by a blurred ring image, computed once per size with Core Image and kept until the size or corner radius changes. Trace animates `strokeStart`/`strokeEnd` on a path, which moves at an even speed along a non-square shape where a rotating conic would not. Breathe is `shadowRadius` and `shadowOpacity` with a `shadowPath`, so no offscreen pass is needed. Known UIKit behaviour, to be checked in Spine: animations are removed when a layer leaves the window and can be gone after the app returns from the background, so every highlight re-adds its animations in `MovedToWindow` and on `WillEnterForeground`.

**Android.** `View.getOverlay()` (API 18) draws a drawable after the view's content and children without touching layout or `Foreground`. The drawable uses `SweepGradient` for Edge, `LinearGradient` for Sheen and `PathMeasure.getSegment` for Trace, driven by one `ValueAnimator` per highlight that invalidates only the drawable. Outer glow: `RenderEffect.createBlurEffect` on a `RenderNode` from API 31 (the route `MaterialExtensions.Android.cs:191` already takes); `BlurMaskFilter` is supported by the hardware renderer from API 28; below that the rim is drawn without the glow. A parent with `clipChildren` (the default) clips anything outside the view, so halos may draw into the parent's `ViewGroupOverlay` at the child's offset instead (to be tried). `ValueAnimator` jumps to its end when animations are removed, so Highlight reads the setting itself rather than relying on that.

**Windows.** Composition runs animations off the UI thread. `CompositionSpriteShape` strokes a rounded rectangle with any brush; `CompositionGradientBrush.RotationAngleInDegrees` is animatable, which gives a rotating linear gradient for Edge (Composition has no conic gradient, so it looks close to but not the same as the others). `CompositionGeometry.TrimStart`/`TrimEnd`/`TrimOffset` give Trace. `DropShadow` on a `LayerVisual` gives Breathe and a single-colour outer glow for Edge; a multi-colour blurred glow would need Win2D effects, which Spine does not reference. `UISettings.AnimationsEnabled` is the reduced-motion setting, already read in the core.

**All platforms.** `SemanticScreenReader.Default.Announce(text)` and `view.SetSemanticFocus()` in MAUI; `Preferences.Default.Get/Set(key, value, sharedName)` and `Clear(sharedName)`.

---

## 4. Alternatives and trade-offs

| | A. Native layers per kind (recommended) | B. Skia overlay for all kinds | C. MAUI `GraphicsView` overlay, as Shimmer |
|---|---|---|---|
| Attaches to any view (`Button`, `ImageButton`) | Yes, through the platform view | No, needs a layout parent to add a canvas to | No, same reason (`Skeleton.cs:110-114`) |
| Cost per frame | Compositor (Apple, Windows); one drawable invalidate (Android) | CPU raster of the whole canvas | UI-thread draw of the whole view |
| Outer glow | Blurred mask / RenderEffect / DropShadow | Easy (`SKImageFilter.CreateBlur`) | No blur in Microsoft.Maui.Graphics |
| Dependency | None beyond the core | SkiaSharp, several MB of native libraries per RID | None |
| Code | Three platform files per kind | One file | One file |
| Same look on every platform | Close; Windows Edge differs (§3) | Identical | Identical |

A is chosen because the edge glow runs while a model works, which can be many seconds over a scrolling list, and the issue's main use (onboarding hints on buttons) needs any view. B was rejected for the dependency and the layout limit. C is right for Spotlight only: it is one overlay per window, drawn for a few hundred milliseconds when the hole moves, and still otherwise.

**Voice and the edge renderer.**
1. *Voice references Highlight (rejected).* Simple, but Voice then always carries Highlight, and a hard reference for one of six visuals goes against the package-per-feature rule.
2. *A contract in the core, implemented by Highlight (rejected).* An `IEdgeGlowProvider` in the core that Voice resolves from DI would keep the two packages apart, but it puts a feature-shaped interface in the core, which should only get small generic hooks. It also hides the wiring: whether the edge shows would depend on which packages happen to be referenced.
3. *A third, shared renderer package (rejected).* Adds a package with no use of its own.
4. *Highlight owns the API, the app wires it (chosen).* `IHighlights.AttachEdge(window)` returns an `IEdgeGlow` whose `Intensity` is a `Func<float>` that Highlight samples on every frame. Voice already exposes its levels (`voice.Frame.OutputLevel`), so the app connects them in one line. Neither package references the other, the core gets nothing, and an app can drive the same glow from anything else that has a level (a download, a timer, a sensor).

---

## 5. Proposed API surface

```csharp
namespace Plugin.Maui.Spine.Controls;

public enum HighlightKind { None, Edge, Pulse, Spotlight, Sheen, Trace, Breathe }

/// <summary>How often a highlight plays: once, a number of times, until stopped, or until the view is tapped.</summary>
[TypeConverter(typeof(HighlightRepeatConverter))] // "Once", "3", "Forever", "UntilTapped"
public readonly record struct HighlightRepeat
{
    public static HighlightRepeat Once { get; } = Times(1);
    public static HighlightRepeat Forever { get; } = new() { Count = -1 };
    public static HighlightRepeat UntilTapped { get; } = new() { Count = -1, EndsOnTap = true };
    public static HighlightRepeat Times(int count) => new() { Count = Math.Max(1, count) };

    public int Count { get; private init; }
    public bool EndsOnTap { get; private init; }
}

public static class Highlight
{
    public static readonly BindableProperty KindProperty;      // HighlightKind, default None
    public static readonly BindableProperty RepeatProperty;    // HighlightRepeat, default Once (Edge, Trace, Breathe: Forever)
    public static readonly BindableProperty IsActiveProperty;  // bool, default true: plays when set; bind to a busy flag
    public static readonly BindableProperty KeyProperty;       // string?: plays only until this key has been seen once
    public static readonly BindableProperty TipProperty;       // string?: Spotlight's tip; announced for the other kinds
    public static readonly BindableProperty IntensityProperty; // double 0–1, default 1: width, opacity and glow scale with it
    public static readonly BindableProperty ColorsProperty;    // IReadOnlyList<Color>?: unset, Edge's spectrum or the accent
    public static readonly BindableProperty OutsetProperty;    // double, default 12: room halos may use outside the view

    public static HighlightKind GetKind(BindableObject view) => (HighlightKind)view.GetValue(KindProperty);
    public static void SetKind(BindableObject view, HighlightKind value) => view.SetValue(KindProperty, value);
    // … Get/Set for each property, as in Tap and Skeleton

    /// <summary>Raised when a highlight on <paramref name="view"/> ends by itself or by a tap, not when it is cleared.</summary>
    public static event EventHandler<HighlightEndedEventArgs>? Ended;
}
```

```xml
<!-- plays once per install, stops when tapped -->
<Button Text="Add" Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" Highlight.Key="onboarding.add" />

<!-- bound to the model's busy state -->
<Border Highlight.Kind="Edge" Highlight.IsActive="{Binding Summary.IsRunning}" Highlight.Tip="Summarising" />
```

```csharp
public interface IHighlights
{
    /// <summary>Spotlights each step in order over the whole window. Returns at once with AlreadySeen when the key has been seen.</summary>
    Task<TourResult> TourAsync(string key, params IEnumerable<TourStep> steps);

    /// <summary>Plays a highlight on a view from code, for a hint that does not belong in XAML.</summary>
    Task PlayAsync(VisualElement view, HighlightKind kind, HighlightRepeat repeat = default, CancellationToken cancellationToken = default);

    bool HasSeen(string key);
    void MarkSeen(string key);

    /// <summary>Forgets one key, or every highlight key when <paramref name="key"/> is null ("Show tips again").</summary>
    void Reset(string? key = null);

    /// <summary>An edge glow round the whole window, above everything and transparent to touches, until disposed.</summary>
    IEdgeGlow AttachEdge(Window window);
}

/// <summary>A running window edge glow (§7).</summary>
public interface IEdgeGlow : IDisposable
{
    /// <summary>Sampled on every frame, 0–1. <see langword="null"/>: Highlight's own Edge animation.</summary>
    Func<float>? Intensity { get; set; }

    /// <summary>Unset: the same colours as <see cref="Highlight.ColorsProperty"/> would give.</summary>
    IReadOnlyList<Color>? Colors { get; set; }
}

/// <summary>A tour step. Target: a <see cref="VisualElement"/>, a <see cref="PageAction"/>, or a tab's page type.</summary>
public sealed record TourStep(object Target, string Tip)
{
    public string? Title { get; init; }
    public double Padding { get; init; } = 8;

    public static implicit operator TourStep((VisualElement Target, string Tip) step) => new(step.Target, step.Tip);
}

public enum TourOutcome { Completed, Skipped, AlreadySeen, Cancelled }

/// <param name="Missing">Steps not shown, with the reason ("not on screen", "no view for PageAction 'Add'").</param>
public sealed record TourResult(TourOutcome Outcome, IReadOnlyList<(int Step, string Reason)> Missing);
```

```csharp
await highlights.TourAsync("onboarding",
    (addButton, "Add an event"),
    new TourStep(ViewModel.FilterAction, "Filter by calendar"),
    new TourStep(typeof(SettingsPage), "Your calendars live here"));
```

**Semantics.** `Repeat` counts plays of one cycle (Edge 2.4 s, Pulse 1.6 s, Sheen 1.3 s, as in the concept); `Forever` runs while `IsActive` is true. A `Key` is marked seen when the highlight ends by itself or by a tap, and not when the page goes away mid-play, so a hint interrupted by navigation shows again. A tour's key is marked seen on Completed or Skipped. `UntilTapped` ends on `Button.Clicked`, `ImageButton.Clicked`, and, for every other view (a `Tap.Command` view included), a non-consuming recogniser on Apple, which coexists with Tap's because Tap's recognises simultaneously (`Tap.Apple.cs:188`), and a MAUI `TapGestureRecognizer` elsewhere (whether it coexists with Tap's Android click listener is not verified; if it does not, a `Tap.Executed` event in the core is the fallback, and would be a fifth hook). A step whose target is not visible is scrolled into view when it is inside a `ScrollView`, otherwise left out and reported in `Missing`, and logged with its index and reason; it is never dropped silently.

---

## 6. Package, hooks and composition

**Package.** `Plugin.Maui.Spine.Controls.Highlight`, the four MAUI frameworks. Depends on `Plugin.Maui.Spine` only and pulls in nothing else: no SkiaSharp, no native libraries. `build/Plugin.Maui.Spine.Controls.Highlight.props` declares `<SpineModule Register="Plugin.Maui.Spine.Controls.HighlightExtensions.UseHighlights" />`; the attached properties work without registration (as Tap's do), `IHighlights` (tours and `AttachEdge`) needs it. It references no other feature package, and no feature package references it; Spine.Voice in particular does not (§4). Strings in `Resources/Strings/strings.xml` and `strings.sv.xml`.

### 6.1 Small hooks in the core

1. **`ReducedMotion` public**, in `Plugin.Maui.Spine.Core`, with `IsOn` and a `Changed` event (Shimmer's listener and Android settle delay moved in). Shimmer and MeshBackground drop their copies later.
2. **`SpineOverlay.Show(View content, bool passTouches = false)` → `IDisposable`**: the platform attach of `LightboxOverlay` without the region, above sheets (Android dialog, Windows popup). `passTouches` is for the edge glow: a view with `UserInteractionEnabled = false` on Apple, a window with `FLAG_NOT_TOUCHABLE` on Android, a popup that is not hit-test visible on Windows (none of them tried).
3. **`SpineTargets.TryGetFrame(object target, out Rect frame)`**: window coordinates of a `VisualElement`, of the view a `PageAction` is drawn in (`PageActionView` stays internal), and of a tab's item (the iOS tab bar's item views are private, so the frame is computed from the bar; iOS 26's floating bar needs a check).
4. **One Composition child container per element on Windows**, which Tap and Highlight both add their visuals to (`SetElementChildVisual` takes only one).

All four are generic: overlays, targets, motion and Windows visuals that other packages can use too. Nothing about glow, tours or voice goes into the core.

### 6.2 Composition with other Spine features

- **Tap.** Highlight layers sit just below Tap's press overlay (`ZPosition` 9 000 against 10 000), so a press still shows on a pulsing button.
- **Material and glass.** On iOS 26 the glass container takes the host's subviews (`MaterialExtensions.Apple.cs:107-111`) but not its sublayers, so Highlight is drawn over the glass. A glow *under* the glass, refracted by it, is not in v1.
- **MeshBackground and Shimmer.** Edge on a card over a mesh needs nothing special. Edge and `Skeleton.IsActive` on the same layout are allowed: the skeleton says "no data yet", the edge "a model is working on it".
- **Lists.** In a `CollectionView` template, `IsActive` bound per item follows recycling through the bindable property; a `Key` in a template is one key for all rows, which is what a one-off hint wants.

---

## 7. The window edge, and Voice

```csharp
// The app, where it starts a voice session. Neither package references the other.
using var edge = highlights.AttachEdge(window);
edge.Intensity = () => voice.Frame.OutputLevel;
```

`AttachEdge` puts the Edge renderer from §3 round the whole window through `SpineOverlay.Show(…, passTouches: true)`, so it sits above the header bar, tab bar and sheets and never takes a touch. With `Intensity` null it runs Highlight's own Edge animation (the "model is working" look over the whole screen). With a function, Highlight calls it once per frame from its frame callback (`CADisplayLink`, `Choreographer`, `CompositionTarget.Rendering`) on the main thread and maps the value to rim width, opacity and glow radius, eased so a level read at 20–60 Hz does not flicker. The caller never dispatches per frame and never touches the bindable-property system; the function must be cheap and must not throw (an exception stops the glow and is logged with the window and the exception, not swallowed). On Windows the frame callback is on the UI thread too, so the Composition animation for Edge is replaced by per-frame property sets while a function is attached.

Under Reduce Motion the colour sweep stops and the intensity drives only opacity, which still shows that someone is speaking. `Dispose` fades the glow out and removes the overlay. The concept page's bottom band while the user speaks is not in v1; the app can approximate it by attaching to a smaller view with the attached `Highlight.Intensity`. The window edge needs the display's corner radius: Android has `WindowInsets.getRoundedCorner` (API 31); iOS has no public API, so a radius per device class or a square edge (open question 6).

---

## 8. Accessibility, motion and persistence

- **Tips.** When a highlight with a `Tip` starts, the tip is announced once with `SemanticScreenReader.Announce`. For a lasting kind (Edge, Trace, Breathe while `IsActive`) the tip is also set as the view's semantic hint while it runs and the app's own hint is restored after.
- **Tours.** Each step moves screen-reader focus to the target (`SetSemanticFocus`) and announces "Step 2 of 3. Filter by calendar". The tip's Next and Skip are real buttons; the dim is out of the accessible tree; Back (Android) and Escape (Windows, Mac) skip the tour.
- **Reduced motion** per kind is in §1; highlights listen to `ReducedMotion.Changed` and switch while running.
- **Off screen and background.** Every highlight pauses when its view is detached, hidden or the app is in the background, and stops on handler loss (the Shimmer lesson, `SkeletonOverlay.cs:85-89`).
- **"Once per install".** Keys are stored under the shared name `spine.highlight`. iOS deletes them with the app. On Android, Auto Backup restores `SharedPreferences` after a reinstall when the app allows backup, so a hint can stay seen for the same Google account. Windows unpackaged apps keep them in the app's local data.

---

## 9. Implementation steps

1. The core hooks in §6.1, each with its own small PR; Tap moves to the shared Windows container first, and is checked with VoiceOver, TalkBack and Narrator, because it carries `Semantic`.
2. The package: attached properties, `HighlightState` following the handler like `TapState`, `HighlightRepeat` and its converter, keys in `Preferences`, `IHighlights` without tours.
3. Apple kinds: Edge (with the blurred-ring mask), Pulse, Sheen, Trace, Breathe; re-adding animations on window and foreground changes.
4. Android kinds in one `ViewOverlay` drawable, with the API 31 / 28 / older glow levels.
5. Windows kinds on Composition.
6. Spotlight and `TourAsync` on `SpineOverlay` and `SpineTargets`, strings, haptic per step, accessibility.
7. `IHighlights.AttachEdge` and `IEdgeGlow` on `SpineOverlay` with `passTouches`, the per-frame `Intensity` sampling, and the one-line wiring in the Voice sample and in both packages' wiki pages (no package reference either way).
8. A Showcase gallery page with every kind and repeat mode, `docs/wiki/highlight.md`, the `spine-controls` skill. Device checks: iPhone (iOS 26 glass, Reduce Motion), the Android emulator (Remove animations, API 28 and 31+), Windows (animation effects off), a tour with a sheet open, a highlight in a recycled list.

---

## 10. Open questions for the owner

1. **Tap through in a tour.** v1 advances on a tap anywhere and does not run the target's action. Passing the tap through the hole is easy on Apple, hard on Android while a sheet's dialog window is under the overlay. Is "the tour shows, the user then acts" enough?
2. **Auto Backup.** Accept that Android may restore "seen" keys after a reinstall, or exclude `spine.highlight` from backup in the build step?
3. **Edge's default colours.** The spectrum from the concept (Apple Intelligence style), or the app's accent with the spectrum as an option?
4. **Tips outside Spotlight.** Only announced (this proposal), or also a small coach-mark bubble next to a pulsing view?
5. **Core hooks.** Is it acceptable to make `ReducedMotion` public and turn `LightboxOverlay`'s attach into a public `SpineOverlay`, both of which other packages could then use?
6. **The window edge's corners on iOS.** There is no public API for the display's corner radius. Use a table of radii per device class (kept up to date by hand), or draw the window edge square and let the screen's own corners cut it?

---

## 11. Sources

Read in the repo: `Extensions/Tap.cs`, `Tap.Apple.cs`, `Platforms/Android/Tap.Android.cs`, `Platforms/Windows/Tap.Windows.cs`, `Extensions/Material.cs`, `MaterialExtensions.Apple.cs`, `Platforms/Android/MaterialExtensions.Android.cs`, `Platforms/Windows/MaterialExtensions.Windows.cs`, `Extensions/Motion.cs`, `Extensions/Haptics.cs`, `Extensions/Semantic.cs`, `Extensions/SpineAnimation.cs`, `Core/ReducedMotion.cs`, `Core/SpineTheme.cs`, `Presentation/LightboxOverlay*.cs`, `Presentation/PageActionView.cs`, `Presentation/SpineTabbedHostPage*.cs`, `Services/NavigationService.cs`, the Shimmer and MeshBackground packages, `docs/wiki/packages.md`, the concept page's source. The platform URLs below were not reopened for this proposal.

- Apple, `CAGradientLayerType.conic`: https://developer.apple.com/documentation/quartzcore/cagradientlayertype/conic
- Apple, `CAShapeLayer.strokeEnd`: https://developer.apple.com/documentation/quartzcore/cashapelayer/strokeend
- Apple, `UIAccessibility.isReduceMotionEnabled`: https://developer.apple.com/documentation/uikit/uiaccessibility/isreducemotionenabled
- Apple, HIG Motion: https://developer.apple.com/design/human-interface-guidelines/motion
- Android, `ViewOverlay`: https://developer.android.com/reference/android/view/ViewOverlay
- Android, `SweepGradient`: https://developer.android.com/reference/android/graphics/SweepGradient
- Android, `RenderEffect`: https://developer.android.com/reference/android/graphics/RenderEffect
- Android, hardware acceleration, supported drawing operations: https://developer.android.com/develop/ui/views/graphics/hardware-accel
- Android, `WindowInsets.getRoundedCorner`: https://developer.android.com/reference/android/view/WindowInsets#getRoundedCorner(int)
- Android, Auto Backup: https://developer.android.com/identity/data/autobackup
- Windows App SDK, `CompositionGradientBrush`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositiongradientbrush
- Windows App SDK, `CompositionGeometry.TrimEnd`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositiongeometry.trimend
- Windows App SDK, `ElementCompositionPreview.SetElementChildVisual`: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.elementcompositionpreview.setelementchildvisual
- .NET MAUI, accessibility (`SemanticScreenReader`, `SetSemanticFocus`): https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/accessibility
- .NET MAUI, Preferences (`sharedName`): https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences
