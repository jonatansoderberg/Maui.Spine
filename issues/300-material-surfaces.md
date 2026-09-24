# Issue #300 — Material surfaces: glass, blur and tinted panels for any content

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/300
**Branch:** issue/300-material-surfaces
**Status:** In Progress

## Plan

### Goal
Replace Sharpnado.MaterialFrame and keep the experience the sample has today: the hero's `HeaderOverlayContent` is a `MaterialFrame` with `MaterialTheme="AcrylicBlur"` that fades in as the header collapses. On iOS that is a system blur. On Android it is a real-time blur of what is behind (a snapshot of the window), not only a tint. On Windows it is acrylic. The same material then also draws the header bar's Android/Windows stand-ins, instead of the hand-made gradient bands in `PagePresenter`.

### Decisions taken with Jonatan (2026-09-24)
- **Android blur:** real blur of what is behind from API 31 (a snapshot recorded into a `RenderNode` with `RenderEffect.CreateBlurEffect`, drawn on the GPU); a tinted surface below API 31.
- **API:** attached properties on a `Border` or `ContentView`, in the shape of `Glass.Style`. They work from a `Style`, and the shape comes from `Border.StrokeShape`.
- **Scope of this PR:** Blur/Tinted/Solid with fallbacks, Glass on iOS 26, the header bar on the material, Interactive glass and containers.

### API (`Plugin.Maui.Spine`, namespace `Plugin.Maui.Spine.Extensions`, `Material.cs`)
```xml
<Border Material.Kind="Blur" Material.Thickness="Regular" StrokeThickness="0" />
<Border Material.Kind="Glass" Material.Interactive="True" StrokeShape="RoundRectangle 22" />
<MaterialContainer Spacing="16">  <!-- iOS 26: glass surfaces inside melt together -->
    <HorizontalStackLayout> <Border Material.Kind="Glass" /> <Border Material.Kind="Glass" /> </HorizontalStackLayout>
</MaterialContainer>
```
- `MaterialKind`: `None`, `Glass`, `Blur`, `Tinted`, `Solid`.
- `Material.Thickness` (`Thin`, `Regular`, `Thick`, `Chrome`): how much of the background comes through a `Blur`.
- `Material.Tint` (`Color?`): a colour bled into the material (glass tint, a blur's overlay, the tinted surface's colour).
- `Material.Interactive` (`bool`): iOS 26 glass reacts to touch.
- `MaterialContainer` (a `ContentView` with `Spacing`): `UIGlassContainerEffect` on iOS 26; a plain container elsewhere.

### Per platform
| Kind | iOS / Mac Catalyst 26 | iOS 15–18 | Android 31+ | Android < 31 | Windows |
|---|---|---|---|---|---|
| `Glass` | `UIGlassEffect` (tint, interactive), `UICornerConfiguration` from the shape | `Blur` regular | `Blur` | `Tinted` | `Blur` |
| `Blur` | `UIBlurEffect` system material by thickness (follows light/dark) | same | Snapshot of what is behind, blurred with `RenderEffect`, with a light/dark overlay | `Tinted` | `AcrylicBrush` |
| `Tinted` | The theme's surface, see-through | same | same | same | same |
| `Solid` | The theme's surface, opaque | same | same | same | same |

- **Reduce Transparency** (iOS/Mac) and transparency effects off (Windows): the system materials go opaque on their own. `Tinted` becomes `Solid`.
- **iOS:** a mapper appended to `BorderHandler`, `ContentViewHandler` and `LayoutHandler` inserts one `UIVisualEffectView` at the back of the platform view. It fills the bounds, is clipped to the `StrokeShape`, and is removed again for `None`.
- **Android:** a `MaterialDrawable` as the platform view's background.
  - It captures the window content behind the view on pre-draw into a scaled-down `RenderNode`, skipping itself, and applies the blur effect.
  - It draws the node clipped to the shape. It stops recording while the view is detached or hidden.
- **Windows:** the platform panel's `Background` becomes an `AcrylicBrush` or `SolidColorBrush`.

### Hero collapse (HeroCollectionView)
- New `HeroCollectionView.HeaderMaterial` (`MaterialKind`, default `None`). When it is set and there is no `HeaderOverlayContent`, the hero makes the overlay itself: a `Border` with that material, faded in with the collapse exactly as the overlay is today.
- The sample's main page uses `HeaderMaterial="Blur"`. Sharpnado.MaterialFrame is removed from the sample (package reference, `MaterialFrame.xaml`, `UseSharpnadoMaterialFrame`).

### Header bar on the material

**iOS 27 finding (2026-09-24, native reference on Jonatan's iPhone, iOS 27.0, and the iOS 26.4 simulator).** A `UINavigationController` over a `UITableView`, with `TopEdgeEffect.Style` soft, hard and automatic:
- On iOS 26.4 the soft style blurs the status bar and the whole bar, and automatic is soft.
- On iOS 27.0 the soft style only blurs behind the status bar; text behind the title stays sharp. The hard style is a lighter frosted band over the whole bar, and automatic is hard.
- Making the element UIKit sizes the effect to taller (0–90 points past the bar) changes nothing on 27.

So Spine's `SoftEdge` on iOS 27 looks the same as `SoftStatusBar`: UIKit no longer has a soft style for the whole header.

**Step 1 of this issue, as built:** on iOS and Mac Catalyst 27, `SoftEdge` stays UIKit's soft style, and Spine gives it back its iOS 26 reach (`SoftEdgeStretch`, see Decisions).
- A first attempt with a `Blur` material and a fade mask looked like a frosted slab, not like iOS 26, and was dropped for iOS.
- `SoftStatusBar` keeps UIKit's soft style on every version; on 27 that is the native soft.

The header bar's stand-ins where UIKit does not draw the scroll edge (Android, Windows, and iOS before 26 for `Solid`) are built on the material. The `_barBackground` `BoxView` and the gradient code in `PagePresenter.ApplyBarBackgroundColor` give way to a material surface with an internal bottom fade (an alpha gradient mask on Android; on Windows the acrylic ends at the bar and a short tinted fade follows):

| Value | Android 31+ / Windows | Android < 31 |
|---|---|---|
| `SoftEdge` | Blur, fading out below the bar | Tinted band as today |
| `SoftStatusBar` | Blur behind the status bar, fading out | Tinted band as today |
| `HardEdge` | Thick blur down to the bar's edge, with a hairline | Nearly opaque band with hairline as today |

### Sample and docs
- **Sample:** a "Materials" page (symbol from the set, or a new one with `/spine-symbol`):
  - every kind and thickness over a photo and over colourful content;
  - interactive glass and a container with merging glass;
  - a code example per choice.
- **Docs:** `docs/wiki/materials.md` (new; the table per platform, screenshots, the HIG note that glass is for floating controls and blur/tinted for content panels, never glass on glass), `hero-collection-view.md`, `regions.md` (stand-ins), `packages.md` if needed, and the `/spine-controls` skill.

### Verification
- iPhone 17 simulator (iOS 26.4): every kind and thickness, light and dark, Reduce Transparency, interactive glass, a merging container, the hero collapse (compared with MaterialFrame in the current sample), the header bar stand-ins unaffected.
- Pixel 10 Pro emulator (API 36): real blur behind a panel and behind the collapsing hero while scrolling (smoothness, no feedback loop), the header bar with blur, light and dark. Below API 31: the fallback, by forcing it in a debug build, or on an older emulator image if one is installed.
- Windows compiles in CI only.

## Open Questions

None.

## Changes

- `Material` (new, `Extensions/Material.cs`): `MaterialKind`, `MaterialThickness`, the attached `Kind`, `Thickness`, `Tint` and `Interactive`, and the internal `Fade` and `EdgeLine`. `Resolve` picks what a kind is drawn as on the platform (glass → blur → tinted → solid).
- iOS / Mac Catalyst (`MaterialExtensions.Apple.cs`): `MaterialSurfaceView`, a `UIVisualEffectView` at the back of a `Border`, `ContentView` or layout. It draws glass (26), a system blur material by thickness, or a tinted or solid surface. It is masked through `MaskView` (the shape, or the fade) and has an optional hairline.
- Android (`MaterialExtensions.Android.cs`): `MaterialDrawable` layered under the view's own background: the surface colour, clipped to the shape, with the fade and the hairline. Real blur comes in step 2.
- Windows (`MaterialExtensions.Windows.cs`): `AcrylicBrush`, `SolidColorBrush`, or a `LinearGradientBrush` for the fade and hairline, painted on a Border's shape path or on the panel.
- The header bar's background (`PagePresenter._barBackground`) is a `ContentView` with a material instead of a `BoxView` with its own gradients: `Solid` for Solid, and `Blur` with a fade (soft) or a hairline (hard) for the stand-ins.
- `SoftEdgeStretch` (iOS / Mac Catalyst 27): finds UIKit's `ScrollEdgeEffectView` in the list's scroll view and the `CABackdropLayer` with the `variableBlur` filter inside it. It keeps that layer as tall as the header plus 36 points (KVO on its `bounds`, since UIKit sets it back), and adds a black gradient over it (22 % light, 40 % dark, over the top 65 %, then fading out).

## Decisions

- **Why UIKit's own soft edge is stretched on iOS 27 rather than drawn by Spine.** Dumps of the view and layer tree:
  - iOS 26.4 soft is a 181-point `ScrollEdgeEffectView`: a `variableBlur` backdrop masked by a shadow of the header's elements with a radius of about 65 points, plus a colour matrix and a luminance layer.
  - iOS 27 soft keeps the effect view at 156 points but cuts the `variableBlur` backdrop to the status bar (62 points), and drops the colour adjustments.
  - Made taller, the backdrop stretches its blur mask with it and gives the progressive blur of iOS 26. A `UIVisualEffectView` with a gradient mask blurs evenly and fades, which reads as a frosted slab.
- **How the stretch and the darkening were chosen.** The Header bar page was compared at the same scroll offset, row by row, with iOS 26.4's `SoftEdge` in the simulator: edge sharpness (mean of an edge filter per 8-point band) and luminance.
  - A tail of 36 points below the bar matched best; tails of 20, 40, 60 and 80 were measured.
  - The darkening matched iOS 26 to within a few levels in both themes (light 132/132, 139/137; dark 71/71, 86/87).
  - The unblurred rows are identical between the simulator and the device, so the images compare directly.
- **Found by structure.** The class and layer names are private, but no private API is called: the code looks up views and layers, and sets a layer's frame and adds a sublayer. On a system that builds the edge differently nothing is found, and the soft edge stays UIKit's own (status bar only on 27).
