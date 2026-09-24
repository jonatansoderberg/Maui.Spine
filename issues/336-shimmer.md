# Issue #336 — Plugin.Maui.Spine.Controls.Shimmer: skeleton shimmer, and Skeleton.IsActive on real layouts

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/336
**Branch:** issue/336-shimmer
**Status:** Completed
**Stage:** 3 of the app-review plan (#333), first item (it decides the style-options chain shared with #334 and #335)

## Plan

### Phase 1: `Shimmer`
Port a production-tested skeleton shimmer (plain MAUI: a `GraphicsView` overlay driven by the Animation API) as `Plugin.Maui.Spine.Controls.Shimmer`, control `Shimmer`:
- `SkeletonContent` (content property), `IsLoading`, `StyleOptions`, `RefreshPlaceholders()`.
- Placeholders: any `BoxView`, any `Border` without content, any childless element with a visible background. `Shape`s skipped entirely (a style-set `StrokeShape` is one shared instance; subscribing to it roots the page). Corner radii honoured, `ScrollView` offsets corrected, nothing found → the wave covers the whole control.
- Bounds re-collected when any skeleton element changes size, dispatched until after arrange; subscriptions dropped on `Unloaded` and when the handler goes.
- Wave only while loaded + visible + `IsLoading`, ~40 fps, clip path and gradient cached across frames. No `BoxView` with an animated `LinearGradientBrush` (solid black on Android).
- `ShimmerStyleOptions : SpineStyleOptions<ShimmerStyleOptions>`: `PlaceholderColor` and `WaveColor` (theme-aware defaults), `WaveOpacity`, `WaveWidth`, `WaveAngle`, `WaveDuration`, `CornerRadius`.
- Repaint through `SpineTheme.Track`; Reduce Motion gives static placeholders; accessibility text `Shimmer.Loading` from `SpineStrings.Current`, embedded `strings.xml` + `strings.sv.xml`, registered lazily by the controls' static constructors (no builder call, per #355).

### Phase 2: `Skeleton.IsActive`
An attached property on a layout that turns the real layout into its own skeleton, drawn by the same internal overlay and drawable as phase 1. Leaves (labels, images, buttons, …) are hidden while active and drawn as rounded blocks; containers (layouts, bordered cards) keep drawing. Fallback sizes for views that have no size without data; overrides `Skeleton.Lines`, `Skeleton.Width`, `Skeleton.Height`.

### Sample, docs
Gallery page "Shimmer" (skeleton card with a loading toggle; a list and a detail layout with `Skeleton.IsActive` that switch to content without a jump), code examples, `docs/wiki/shimmer.md`, packages table, `/spine-controls` skill.

## Open Questions

None.

## Changes

- New package `src/Plugin.Maui.Spine.Controls.Shimmer` (csproj, README, icon `assets/icons/shimmer.png` + source in `assets/logo-src/`), added to `Spine.slnx`, `Spine.Packages.slnf` (CI and release build it from there), the root README tables and `docs/wiki/packages.md`.
- `SkeletonDrawable`: fills placeholders without a colour of their own, then the wave clipped to all placeholders; clip path, fill path and gradient cached across frames.
- `SkeletonOverlay`: the one `GraphicsView` overlay behind both phases. Measures to nothing and arranges itself over its parent's whole bounds (a stack's spacing is handed back through a negative desired size), so it can be added to any layout without moving anything. Lifecycle (Loaded/Unloaded/handler loss), dispatched rescans on any size change or added/removed view, shapes skipped, ~40 fps wave on a shared clock, theme/culture repaint through `SpineTheme.Track`, Reduce Motion, accessibility ("Loading").
- `Shimmer` (phase 1): `SkeletonContent`, `IsLoading`, `StyleOptions`, `WaveWidth`, `WaveOpacity`, `RefreshPlaceholders()`; the skeleton content is excluded from the accessibility tree.
- `Skeleton` (phase 2): attached `IsActive`, `Lines`, `Width`, `Height`, `StyleOptions`, `WaveWidth`, `WaveOpacity`. Adds/removes the overlay, hides leaf views on the platform view (alpha 0 + hidden from screen readers), reserves fallback sizes, fades the blocks out on deactivation, re-adds the overlay when a `BindableLayout` clears the children.
- `ShimmerStyleOptions : SpineStyleOptions<ShimmerStyleOptions>`: `PlaceholderColor`/`WaveColor` (theme defaults), `WaveOpacity` 0.3, `WaveWidth` 0.22, `WaveAngle` 15, `WaveDuration` 1.1 s, `CornerRadius` 4.
- `ReduceMotion`: iOS/Mac Catalyst `UIAccessibility.IsReduceMotionEnabled` + change notification; Android `animator_duration_scale` + a content observer; Windows `UISettings.AnimationsEnabled`, read when an overlay (re)starts.
- Strings `Shimmer.Loading` (en, sv), registered once by `ShimmerStrings.EnsureRegistered()` from the static constructors of `Shimmer` and `Skeleton`; no `UseSpineShimmer()`.
- Sample: `Pages/Shimmer` gallery page (Shimmer card with IsLoading switch and WaveWidth/WaveOpacity sliders, a list and a detail card under `Skeleton.IsActive`, Reload button and switch, a dark-theme switch to show the runtime repaint, code examples), index row with `lightstrip.svg`.
- Docs: `docs/wiki/shimmer.md`, getting-started row, agent-skills row, `/spine-controls` skill section.

## Decisions

- **One overlay for both phases.** The issue asks for one implementation; the difference between the phases is only which elements become blocks (placeholder rules vs. every leaf of a real layout) and whether the leaves are hidden.
- **The skeleton layout's overlay is a child of the layout itself**, not a platform subview or a window overlay. It overrides `MeasureOverride` (zero; minus the spacing in a stack) and `ArrangeOverride` (the parent's bounds), which is cross-platform and scrolls/clips with the layout. Trade-offs, documented: `Children` has one more entry while active; a space-distributing `FlexLayout` gives it a share.
- **Leaves are hidden on the platform view** (UIView alpha / Android View alpha / WinUI Opacity, plus `AccessibilityElementsHidden` / `ImportantForAccessibility` / `AccessibilityView.Raw`), not through `Opacity`: setting a bindable property from code would drop a binding the app put on it. Restored through the Opacity mapper, so a label with `Opacity="0.7"` comes back at 0.7 (verified).
- **Fallback sizes use `MinimumHeightRequest`/`MinimumWidthRequest`, only where the app has not set them**, and are cleared on deactivation. Label line height is `FontSize × 1.2` (or × `LineHeight`); on iOS the loaded card measured the same height, on Android within ~1 dp.
- **Placeholders without a colour are filled with `PlaceholderColor`**, and the fill is drawn whenever the Shimmer is shown, not only while `IsLoading`. The source relied on the app colouring its placeholders from a brand palette; Spine's defaults must follow the theme, so an empty `Border` (the issue's own example) needs a themed fill. Consequently bounds are re-collected on size changes while attached, not only while the wave runs.
- **All overlays read one clock** (`Environment.TickCount64 % duration`), so rows of a list sweep in step.
- **Default wave (Jonatan's request for subtler):** `WaveOpacity` 0.3 (the source had 0.5). Light: white at 0.3 over `#E5E5EA` peaks at about `#EDEDF0` (measured 237 vs 229 on the simulator), soft but visible. Dark: kept at 0.3 but the band colour moved from `#636366` to `#8E8E93`; `#636366` at 0.3 would peak at about `#3F3F41` over `#2C2C2E`, too close to see, while `#8E8E93` peaks at about `#49494B` (measured 73 vs 44). Judged on the iPhone simulator in both themes and on the Pixel emulator in dark. `WaveWidth`/`WaveOpacity` are also direct properties (`Shimmer`) and attached properties (`Skeleton`), winning over the options chain; the sample's sliders run 0–1 for both.
- **Reduce Motion on Android reads `animator_duration_scale` itself** rather than `ValueAnimator.AreAnimatorsEnabled()`, which lags behind the content observer. On any change the wave is stopped and restarted: MAUI's Android ticker stops ticking a committed animation while animations are off and does not resume it.
- **No builder call** (#355): the only setup was the default strings, now registered lazily and once.
- **`Skeleton.IsActive` throws on a non-layout** with a message saying where to put it (make failures visible), rather than silently doing nothing.
- **`SpineStyleOptions` unchanged.** One limitation found and documented in `shimmer.md`: the effective copy is cached per `SpineTheme.Version`, so mutating an options object's properties after it has been used shows at the next theme change; assign a new object instead. Left as is because the cache is the point of the design and #334/#335 build on it; worth a note for those ports.
- **Rescans until the reserved sizes are laid out.** On iOS, when a `BindableLayout` rebuilt its rows at the moment the skeleton came on, the rows kept the measure they had without text (height 0) even after the labels got their minimum height, and no size change followed; the bars ended up stacked. A scan that finds a view smaller than the minimum it was given now invalidates the measure of the view and its containers up to the layout and scans again after 50 ms, at most ten times. Found by toggling Reload in the sample.
- **The overlay survives a `BindableLayout` reset.** The reset removes it and `Skeleton` puts it straight back; the `Unloaded` of the removal can arrive after the re-add, so `Unloaded` is ignored while the overlay is still loaded, and the re-add re-attaches explicitly. Overlays still fading out are not treated as leaves of the next skeleton.
- **Android Remove-animations observer:** the content observer is a Java object, so a bare `Notify()` inside it bound to `java.lang.Object.notify()` and crashed the app when the setting changed; the helper is named `NotifyListeners` and called qualified. Listeners are told 500 ms after the change, because the app's animator scale follows the setting later than the observer fires; a wave restarted at once froze. Verified live: wave stops at scale 0 and runs again at 1.
- **Windows:** reduce-motion changes are picked up when an overlay (re)starts; `UISettings.AnimationsEnabledChanged` is not available on the minimum SDK.

## Design with #302

`Skeleton.IsActive` is a plain `bool` on a layout, so TaskState/StateView need nothing from the Shimmer package beyond a boolean to bind to:

1. **`TaskState` should expose `bool IsLoading`** (true only for `Status == Loading`; not for `Refreshing`, which keeps the old `Result` on screen). Then a page without a StateView writes `Skeleton.IsActive="{Binding Competitions.IsLoading}"` on the layout that shows the result: no converter, no dotted indexer path.
2. **`TaskState<T>` should accept an optional placeholder value** (`placeholder: () => Enumerable.Repeat(Competition.Empty, 5).ToList()`) and expose `Value` = `Result` while it has one, the placeholder while loading. A list bound to `Competitions.Value` then has as many rows while loading as it will have loaded, which is what makes the skeleton list keep its height (the sample page does this by hand).
3. **StateView's default `LoadingTemplate`** should not need the Shimmer package: an `ActivityIndicator` in the core. The skeleton path is: when the TaskState has a placeholder, StateView shows the **`ContentTemplate`** during `Loading` (bound to `Value`, i.e. the placeholder) and keeps that same view when `Success` arrives, so nothing is rebuilt and nothing jumps. It exposes `bool IsLoading` on itself; the content root opts in with `Skeleton.IsActive="{Binding IsLoading, Source={RelativeSource AncestorType={x:Type StateView}}}"`. StateView should **not** set `Skeleton.IsActive` itself: the core cannot reference the Shimmer package (the package depends on the core), and a static hook would bring back the registration step #355 removes.
4. `Refreshing` must not activate the skeleton (pull-to-refresh keeps the content); `Empty` and `Error` replace the content as usual.
5. `EmptyTemplate`/`ErrorTemplate` defaults can use `SpineStrings` keys under a `State.` prefix, the way `Shimmer.Loading` is done here.
