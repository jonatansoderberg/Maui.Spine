# Issue #304 — Feature idea: Shared-element and zoom transitions between pages

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/304
**Branch:** issue/304-feature-idea-shared-element-and-zoom-transitions-b
**Status:** In Progress

## Plan

### Starting point
A Spine page is not a `UIViewController`, a fragment or a WinUI `Frame` page. It is a view that `NavigationRegionViewModel` places in one of two layers of a single `NavigationRegion` (`_contentHostFront` / `_contentHostBack`, with `_backDragDimOverlay` between them). `NavigationRegion.PlayTransitionAsync` hands those layers to `ISpineTransitions.AnimatePushAsync` / `AnimatePopAsync`. So the native building blocks in the issue's table (`UIViewController.Transition.Zoom`, fragment shared-element transitions, `ConnectedAnimationService`) cannot be attached to a Spine push. Both modes have to be driven by Spine inside `NavigationRegion`, on every platform. That also means iOS 17 gets the full feature, not just the shared element.

### API
`Transition.Tag` is an attached string property in `Plugin.Maui.Spine.Extensions` (`Extensions/Transition.cs`), in the same style as `Tap`, `Haptics` and `Reorder`.

```xml
<!-- list page -->
<Image Source="{Binding Poster}" spine:Transition.Tag="{Binding Id, StringFormat='poster-{0}'}" />
<!-- detail page: same tag on an element → shared element -->
<Image Source="{Binding Poster}" spine:Transition.Tag="{Binding Id, StringFormat='poster-{0}'}" />
<!-- or on the detail page's root view → zoom: the page grows out of the element -->
<ContentView spine:Transition.Tag="{Binding Id, StringFormat='day-{0}'}">
```

- `INavigationService` stays unchanged. The tags pick the mode: a tag on the target page's root view means zoom, a tag on an element inside it means a shared element.
- Navigation parameters reach the view model before the region push (`NavigationService` calls `OnNavigationParameterAsync` before `NavigateRegionAsync`), so a tag bound to the parameter has resolved before the transition starts.

### Finding the tagged views
- The `Transition.Tag` property-changed handler keeps a registry of tagged views as weak references. A walk down the visual tree would not find `CollectionView` cells reliably.
- At transition time Spine keeps the views that belong to the outgoing page and the views that belong to the incoming page (by walking each view's `Parent` chain up to the page view) and pairs them by tag. A source has to be on screen inside the region. A cell that has scrolled away or been recycled under another tag does not match, and the navigation falls back to the default transition.
- On pop, the source is looked up again by tag on the page coming back, because the list may have scrolled since the push.

### Geometry and timing
- Bounds are measured relative to the region with a platform helper: iOS/Mac `ConvertRectToView`, Android `GetLocationInWindow` (already used in `Platforms/Android`), Windows `TransformToVisual`.
- The incoming page must have been laid out before its target can be measured. `PlayTransitionAsync` waits for the target's first non-zero layout before it starts, with a short timeout that falls back to the default transition. The spike will show whether this costs a frame.

### Shared element (push and pop)
- A snapshot of the source is captured natively: iOS/Mac `SnapshotView(afterScreenUpdates: false)`, Android a `Bitmap` drawn from the view, Windows `RenderTargetBitmap`.
- The snapshot is placed in an overlay above both layers and below the header bar, then animated from the source rect to the target rect. In the last part of the flight it crossfades into a snapshot of the target, so a change in aspect ratio does not show as a stretch.
- Source and target stay hidden (`Opacity = 0`) during the flight and are restored in `finally`, the same way the layers are reset today.
- The page motion itself is unchanged: `ISpineTransitions.AnimatePushAsync` / `AnimatePopAsync` still run, so custom transitions keep working, and the flight plays on top with the same duration and easing. iOS runs the flight in Core Animation, like `SpineAnimation`.

### Zoom
- Push: the front layer starts scaled and clipped to the source rect, with a rounded clip, and grows to full size while the back layer stays in place under the dim. Pop runs the same motion in reverse into the source, which is found again by tag. When the source cannot be found, the page shrinks to the centre and fades.
- Interactive back-swipe: in zoom mode the edge swipe shrinks the front page as the finger moves, in place of the slide. On release the page either completes into the source or springs back to full size. This touches `NavigationRegion.OnPanUpdated` / `ApplyBackReveal`.

### Back-swipe for shared elements (v1)
The interactive swipe keeps the normal slide. The element flies only on the back button, `BackAsync` and Android's system back.

### Fallbacks
- Reduce Motion (`Core/ReducedMotion.IsOn`) → the default transition.
- Sheets: no flight from a page into a sheet or out of one. A sheet is its own native window or controller. Inside a sheet's own stack, push and pop work as in a region.
- Tab switches, `SetRootAsync` and `PopToAsync` across several pages → no flight in v1.

### Order of work
1. **Spike on iOS (iPhone 17 simulator):** registry, region-relative bounds, layout wait, and a snapshot flight on push and pop. Answer the timing question before going further.
2. Zoom on iOS, including the interactive shrink on back-swipe.
3. Android (emulator) and Mac Catalyst. Windows: compile the TFM on the Mac (EnableWindowsTargeting) with the `RenderTargetBitmap` snapshot.
4. Showcase: a "Transitions" page under `samples/MauiSpineSampleApp/Pages/Transitions/` with a grid of cards. Tapping a card does a shared-element push to a detail page, and a second section zooms into a day page. Add it to `SampleIndex` with a fitting icon and code examples on the page.
5. Docs: a section in `docs/wiki/custom-transitions.md` (or a page of its own) and a `spine-controls`/`spine-page` skill mention if it fits.

## Open Questions

- Whether `ISpineTransitions` should get a hook for the flight (for example the hero pairs on `SpineTransitionContext`) so apps can restyle it. Left out of v1 unless the spike shows a need.

## Changes

- **iOS spike (2026-10-06), shared element on push and pop works on the iPhone 17 Pro simulator:**
  - `Extensions/Transition.cs`: the `Transition.Tag` attached property and a registry of tagged views (weak references). `TaggedIn(page)` finds them by walking each view's `Parent` chain.
  - `Presentation/SharedElementFlight.cs` (shared) and `SharedElementFlight.Apple.cs`:
    - pairs tags between the outgoing page and the incoming page, measures each view against the region's container (`ConvertRectToView`) and hides both views;
    - flies a picture from the source rect to the target rect in a view inserted right above the front layer, so it stays under the header bar. The picture of the landing view fades in over the picture of the leaving view, and the views are shown again when the layers are back at rest.
    - Other platforms return no flight for now.
  - `NavigationRegion.PlayTransitionAsync` runs the flight next to `AnimatePushAsync` / `AnimatePopAsync`, with `ISpineTransitions.InteractiveGestureDuration` / `InteractiveGestureEasing`, which equal the default's `Duration` / `Ease`. The interactive back-swipe does not go through `PlayTransitionAsync`, so it slides as before (verified).
  - `SpineAnimation.CurveOf` is now internal, so the flight can use the same Core Animation curves.
  - Showcase: a temporary `Pages/Transitions` (a grid of coloured tiles and a detail page) with a `SampleIndex` entry.

- **The flight's corners morph:** both pictures are drawn on the view's own fill colour, so they are square, and the flying view clips them with a corner radius that animates from the source's to the target's (`Border` `RoundRectangle` → its radius, otherwise the layer's; circular arcs, as MAUI draws them). Before, the radius was baked into the pictures and scaled with them, and on pop the smaller corners of the picture underneath showed past the larger ones until they snapped at landing. Jonatan noticed it.
- **Scroll edge narrower than the page (found on the Transitions page, the second time after Icon set in #445):** UIKit draws the header's scroll edge effect inside the scroll view, so a list with a side margin left the bar's sides without it.
  - Spine could not fix this generally. Widening the native list and moving the margin into the content inset failed, because the `CollectionView` grid's compositional layout ignores side content insets (tiles overflowed, and the list scrolled sideways). Rewriting `Margin` from code would cut a binding to it (tested: after `SetValue`, a bound `Margin` no longer follows its view model).
  - Instead, `PagePresenter.Apple` prints a `[Spine] <Page>: …` console line once per page when the scroll source is narrower than the page (verified on Transitions with `Margin="11,0"`).
  - Guidelines in `CLAUDE.md` (Page and Sample Guidelines: the same `HeaderBarConstants.PageMargin` on every page, inside the scroll source), `docs/wiki/regions.md` (Scroll edge) and the `spine-page` skill.
  - Fixed: Transitions, Reorder's grid (`GridMargin` removed), the push sample's Log; first with item margins, then with `SafeArea.PageMargin` (below).

- **`SafeArea.PageMargin` (the follow-up task "Give CollectionView grids the page margin natively", done here on request):** a `CollectionView` lays its rows, header and footer out inside `HeaderBarConstants.PageMargin` while the list itself reaches the page's sides. A grid now keeps the page margin at its outer edges with `HorizontalItemSpacing` between the columns. With a uniform item margin, that gap was twice the outer edge.
  - iOS/Mac (`SafeAreaExtensions.Apple.cs`): the margin becomes the collection view's `DirectionalLayoutMargins` (not from the superview or the safe area), and the compositional layout's configuration refers to them (`ContentInsetsReference = LayoutMargins`). The layout-level header and footer of a list get the margin as `ContentInsets`, because they don't follow the section insets. This is applied again on `ItemsLayout` changes, which replace the layout. Verified on the iPhone 17 Pro simulator: the Transitions grid, and the Reorder list and grid with their intro header, all line up at 16 pt with the back button, with a 10 pt column gap, a full-width scroll edge and no `[Spine]` warning.
  - Android: the margin is added to the `RecyclerView` padding, on top of MAUI's negative padding for item spacing (`SpacingItemDecoration`). Spine's scroll inset used to overwrite that padding. Verified on emulator-5556 (Pixel 10 Pro): the Transitions grid measures 16 dp at both edges with a 10 dp gap, and the Reorder list and grid line up with their intro at 16 dp.
  - Windows: the margin is added to the `ListViewBase` padding (compiled on the Mac only).
  - Showcase: Transitions and both Reorder lists use `SafeArea.PageMargin="True"` and `GridItemsLayout` spacing. The push sample's Log uses it too. The warning text, `CLAUDE.md`, the `spine-page` skill and a new wiki section "Lists inside the page margin" in `regions.md` point to it.

- **Zoom on iOS (2026-10-07):** a `Transition.Tag` on the arriving page's root makes the page grow out of the view with that tag on the page under it. A pop, by button or `BackAsync`, shrinks it back into the view, which is found again by its tag. The back-swipe shrinks the page under the finger: it follows the finger and its corners round. Let go past a third of the width and it zooms into the view; otherwise it springs back.
  - `SharedElementFlight.AddZoom` / `ZoomAsync` / `Follow` / `RestoreAsync` (Apple):
    - The front layer's content is scaled with the layer's `sublayerTransform` until it covers the view, centred on it, and cut by a `CALayer` mask (the view's size and corners over the scale, because the mask is scaled with the content). Core Animation moves the scale, the mask and its corners together (450 ms, a no-bounce spring curve).
    - The view's picture lies over the small page: it fades out over the first 35 % of a push and fades in over the last 35 % of a pop.
  - `NavigationRegion`:
    - `ZoomAsync` replaces `ISpineTransitions` for a zoom. The page under it stays in place and dims, and the header bar fades in.
    - `OnPanUpdated` drives `Follow` when `FindZoomDragAsync` finds a zoom. Any other shared element is let go, and the page slides as before.
  - Without its view on screen (scrolled away), a zoom page moves as usual.
  - **Focus (Jonatan found the pop "a little choppy"):** the shrinking page was a centre-cropped miniature of itself that switched to the tile's picture at the end. The same tag on a view inside the zoom page now makes that view the page's focus, like UIKit's zoom alignment rect: the page is scaled and moved so that the focus covers the other page's view, and the mask is the view's size and corners about the focus. The page shrinks into the card as the card, and the picture's crossfade is shortened from 35 % to 20 % of the zoom, so the labels, which sit a little differently, are seen twice only briefly. Without a focus, the page's middle lines up as before.
  - **No jump at the end (Jonatan: smooth without zoom, a "margin swap" with it):** the focus and the tile differ a little (the card's padding of 20 scaled to 10 against the tile's 14; text 14 against 17). The tile's picture only faded in over the last 20 %, at the tile's place, so the content jumped there. Now, with a focus, the tile's picture rides on the focus: Core Animation moves its frame and corners from the focus's on-screen rect to the tile on the zoom's curve, and it fades in over the whole pop (out over the whole push), as a shared element's pictures do. A swipe's zoom starts the picture at the focus's place under the finger's transform. Frame diffs of the last 16 frames now fall steadily to 0, with no spike at the switch.
  - Without a focus, the short fade at the view's place stays: there the miniature looks nothing like the view.
  - Showcase: `TransitionZoomPage` (the tag on its root) and a "Zoom into the page" switch on Transitions.
  - Verified in recordings on the iPhone 17 Pro simulator: push, button pop, a cancelled swipe and a completed swipe.

- **Android (2026-10-07):** `Platforms/Android/SharedElementFlight.Android.cs` implements the same flight and zoom.
  - The pictures are bitmaps drawn from the views (`View.Draw` on the view's fill colour), shown by a small `PictureView` that crops them about their middle and clips rounded corners. It is added to the region's container right above the front layer and laid out by hand: MAUI's layout only arranges the views it knows.
  - The zoom scales and moves the front layer itself (pivot at its centre) and clips it with a rounded `ViewOutlineProvider`. MAUI lays the layer out by position and size only, so the problem iOS had with a frame set under a transform does not exist here.
  - One `ValueAnimator` drives the layer's scale and move, the outline, and the picture's frame, corners and alpha together. The flight uses the `ISpineTransitions` easing; the zoom uses `PathInterpolator(0.2, 0.9, 0.25, 1)`, the iOS curve.
  - A push waits for the arriving page's next layout pass (`OnPreDraw`, at most 100 ms) instead of iOS's single main-queue hop (`NextLayoutAsync` per platform), with the front layer hidden meanwhile.
  - Verified in `screenrecord` videos on the Pixel_Tablet emulator (emulator-5556; the Pixel_10_Pro AVD was in use by another session on 5554): shared element push and pop, zoom in and zoom out. Frame by frame, the landing picture's left edge eases into the cell at 1290 px and stays there, with no jump at the switch.
  - **The back-swipe never worked on Android (the follow-up task "Check Spine's back-swipe on Android", done here on request):**
    - Logging showed that neither `PointerPressed` nor a single pan update reached `NavigationRegion`. A page's `ScrollView` or `CollectionView` takes the touch at `ACTION_DOWN`, and MAUI's gesture recognizers on the front layer only see touches no child took. On iOS a recognizer on an ancestor sees every touch.
    - Fix: the front layer is now `BackSwipeHost` (a `ContentView`). On Android it gets `BackSwipeHostHandler`, registered in `ConfigureHandlers`, whose `BackSwipeViewGroup` (a `ContentViewGroup`) watches touches in `OnInterceptTouchEvent`. Once a drag from the leading quarter has run rightward past the touch slop, more sideways than vertical, it takes the drag, and the page gets `ACTION_CANCEL`. Distances are measured in screen coordinates because the layer moves with the finger, and the swipe starts where it was taken, so the page does not jump by the slop.
    - `NavigationRegion.OnPanUpdated` became `OnBackSwipe(status, x, y)`, fed by the pan recognizer on iOS/Mac/Windows and by the host on Android.
    - Verified on the Pixel_Tablet emulator: the plain detail page slides back; the zoom page shrinks under the finger, springs back on a short swipe and zooms into the tile on a long one. The iOS swipe is unchanged.
    - The system back gesture (the first few millimetres at the edge) is the system's own and goes back without following the finger.
- **Mac Catalyst (2026-10-07):** the iOS code runs as is. In a `screencapture -v` recording of the Showcase window, the zoom grows out of the Weather tile and shrinks back into it with the card as its focus. The tiles had to be clicked with real mouse clicks: an accessibility press on a `Tap.Command` view does nothing on the Mac (a separate matter).
- Windows (no flight; the usual transition) compiles; it has not been run.

- **Showcase page (2026-10-07):** Transitions follows the gallery pattern: an intro header with `ExampleCodeSwitch`, `PackageChips` and one `Example` ("Move a view between pages") whose "Try options" picks Shared element, Zoom, or Zoom without a focus, with matching code. One `TransitionDetailPage` takes a `TransitionTarget(Tile, Kind)` and binds `Transition.Tag` on the page root and on its card according to the kind (`TransitionZoomPage` is gone). The tiles carry a `Key` (`tile-2`), so the code reads `Transition.Tag="{Binding Key}"`. Gallery icon: a new symbol, `Expand` (a small card with an arrow to the far corner of a large frame), drawn with `/spine-symbol` (Jonatan picked variant A of four); the set now has 223.

## Spike findings

- **Measuring on push:** the arriving page's `ScrollView` gets its top inset (status bar + header bar, 116 pt) only in `Loaded`, which MAUI raises from the main queue after the page is put in the window. Measured synchronously, the target came out 116 pt too high, even after `LayoutIfNeeded` on the container, the window or the root view. Waiting a frame was not needed: a single `Task.Yield()` is enough. The front layer is hidden (`Opacity = 0`) across that hop, so a frame drawn in between shows only the page still on screen. The hop happens only when the two pages share a tag.
- **Pictures:** the source is drawn with `DrawViewHierarchy(afterScreenUpdates: false)`. The target has to be drawn with `Layer.RenderInContext`. On pop, `DrawViewHierarchy` drew the list cell empty: the page had just been moved into the back layer and UIKit had not drawn it there yet. That made a visible "hack" at the end of the back flight, which Jonatan noticed. `afterScreenUpdates: true` is not an option for the target, because on push it would commit the arriving page at rest for a frame.
- **Landing:** recorded at 60 fps, push and pop land exactly on the view (diffs of the target rect). After the pictures are removed, only one frame differs, by about 1 100 px of anti-aliasing in the 540 × 360 px cell crop.

## Decisions

- **The zoom scales the layer's content (`sublayerTransform`), not the view (`Transform`):** on a pop, Spine pads the page coming back, MAUI lays the region out again during the animation, and setting the frame of a view under a transform made the page balloon. MAUI never touches the sublayer transform or the layer's mask. The mask is scaled with the content, so its rectangle and radius are given before that scale.
- **A zoom page whose view is not on screen moves as usual**, rather than shrinking to the centre as the plan said: simpler, and it never pretends to land somewhere.

- **Spine-driven on every platform, not native page transitions:** Spine pages are views in one region, so UIKit's zoom transition, fragment transitions and `ConnectedAnimationService` have nothing to attach to. One snapshot and layer approach keeps behaviour consistent and testable on the Mac.
- **Zoom is chosen by the tag on the target page's root** (Jonatan, 2026-10-06), not by a navigation parameter or a mode on the source. No change to `INavigationService`, and pop finds the source by the same tag.
- **Named `Transition.Tag`** (Jonatan, 2026-10-06), to avoid confusion with `HeroCollectionView` and the Showcase "Hero" page.
- **Back-swipe slides as usual for shared elements in v1** (Jonatan, 2026-10-06). Zoom shrinks interactively.
- **One hidden main-queue hop before measuring on push, not a wait for layout with a timeout:** the spike showed that the late value is the list's inset from `Loaded`, not layout, and a single `Task.Yield()` settles it.
- **The landing picture fades in over the leaving picture, rather than a crossfade of both:** fading out the leaving picture at the same time made the flight see-through in the middle.
