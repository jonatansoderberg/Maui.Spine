# Issue #300 — Material surfaces: glass, blur and tinted panels for any content

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/300
**Branch:** issue/300-material-surfaces
**Status:** Completed

## Plan

### Goal
Replace Sharpnado.MaterialFrame and keep the experience the sample has today: the hero's `HeaderOverlayContent` is a `MaterialFrame` with `MaterialTheme="AcrylicBlur"` that fades in as the header collapses. On iOS that is a system blur. On Android it is a real-time blur of what is behind (a snapshot of the window), not only a tint. On Windows it is acrylic. The same material then also draws the header bar's Android/Windows stand-ins, instead of the hand-made gradient bands in `PagePresenter`.

#- Android 12+ real blur (`MaterialDrawable`):
  - Before each frame (a `PreDraw` listener while the view is attached), what is behind the view is recorded into a `RenderNode`. That is the ancestors' backgrounds and the siblings drawn before it, from the window down, each at its place relative to the view.
  - The node is blurred with `RenderEffect.CreateBlurEffect` (radius 8–40 dp by thickness).
  - It is drawn in the view's background, clipped to the shape, with the surface colour (or `Tint`) over it.
  - A fade is applied with a `SaveLayer` and a `DstIn` gradient. Before API 31 the surface colour is drawn.
- Glass (iOS 26) is shaped with `UICornerConfiguration`: a capsule for an ellipse or a fully rounded rectangle, fixed corners otherwise. It is not masked.
- `MaterialContainer` (new): a `ContentView` with `Spacing`. On iOS / Mac Catalyst 26 its content is moved into a `UIVisualEffectView` with `UIGlassContainerEffect`, so glass inside merges.
- Header bar stand-ins on Android, tuned against iOS 27 on the phone:
  - Soft: the blur darkened as iOS 26 does (22 % light, 40 % dark), with the fade starting inside the bar and ending further below it.
  - Hard: a `Regular` blur with 40 % of the page colour and a hairline.
- The sample:
  - Sharpnado.MaterialFrame is replaced by `<Border Material.Kind="Blur" />` in the hero.
  - A new **Materials** page (every kind, thickness and tint over a photo and over colour; interactive glass; a `MaterialContainer` with a gap slider; a live code example).
  - A taller start-page hero: `SystemBarInsets.Top + 270`, so the photo's S starts below the Dynamic Island. The collapsed hero is a 36-point bar under the status bar, with the title (lifted 3.5 points) and the gear centred on one line; measured equal to 0.2 pt on iOS and Android.
- `HeroCollectionView` resizes its header, spacer and overlay layouts when `HeaderMaxHeight` changes after the header was built.
- Docs:
  - `docs/wiki/materials.md` (new).
  - `regions.md`: the stand-ins and the iOS 27 stretch.
  - `hero-collection-view.md`, and the README tables.
  - The `/spine-controls` skill.

## Verified

- **iPhone 17 simulator (iOS 26.4):**
  - Every kind and thickness and a tint, light and dark.
  - Glass merging in a `MaterialContainer` at gaps of 12, 4 and 0 points, apart at 40.
  - The hero collapse with the Material blur.
  - The collapsed-hero alignment.
- **Jonatan's iPhone 16 Pro (iOS 27.0),** driven with the harness through `devicectl`:
  - `SoftEdge` measured against iOS 26.4, band by band, in light and dark.
  - Native `UINavigationController` references for soft, hard and automatic.
- **Pixel 10 Pro emulator (Android 16):**
  - Blur behind panels over a photo and over colour.
  - The hero's compact header blurring the photo.
  - `SoftEdge`, `SoftStatusBar` and `HardEdge` with real blur, light and dark.
  - The collapsed-hero alignment.
- Windows compiles in CI only. Android before 12 was not run: no emulator image; the code path is the tinted drawable from step 1.

## Decisions taken with Jonatan (2026-09-24)
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
- The compact header's blur is a `Border` with `Material.Kind="Blur"` as `HeaderOverlayContent`, which the hero already fades in with the collapse. (A `HeaderMaterial` property was planned, and dropped: see Decisions.)
- Sharpnado.MaterialFrame is removed from the sample: the package reference, `MaterialFrame.xaml` and `UseSharpnadoMaterialFrame`.

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
- **No `HeaderMaterial` on HeroCollectionView.** The hero package depends on `Plugin.Maui.Spine.Svg` only, and `Material` lives in the core. `HeaderOverlayContent` already fades a view in as the header collapses, so a `Border` with a material is one line in the app and adds no dependency.
- **Android blur records what is behind, not the window.** A software snapshot of the whole window (what Sharpnado's AcrylicBlur did) redraws everything on the CPU each frame, and fails on hardware bitmaps. Recording the ancestors' backgrounds and the earlier siblings into a `RenderNode` stays on the GPU. It also never includes the view itself, so there is no feedback loop.
  - The view's display list keeps pointing at the same node, so only the node is recorded again as content moves.
  - The cost is that content inside the view is not blurred behind itself (it is not behind it), and system surfaces outside the view tree (`SurfaceView`) do not show.
- **Android blur only from 12 (API 31),** as decided with Jonatan: `RenderEffect` needs it, and a CPU blur for older devices was not worth the code and the battery.
- **Glass is shaped, not masked.** A mask on a `UIGlassEffect` view cuts off its edge highlights; `UICornerConfiguration` lets UIKit draw them.
- **The start page's collapsed title is lifted with `TranslationY`.** It sits in an `AbsoluteLayout` by its bottom edge, where a margin does not move it.

## Follow-up: the showcase review (after the merge)

Feedback from the Materials page: thickness did nothing on glass, the thickness scale read in the wrong order (Chrome looked thinner than Thick), the blurs were nearly opaque, and Interactive did nothing. `Material` was not in a released package yet, so its API was reworked.

- **Two layers.** What is done to what is behind: `Material.Kind` (`None`, `Blur`, `Glass`), as frosted as `Material.Intensity` (0–1). A colour over it: `Material.Tint` (the theme's surface when unset) as opaque as `Material.TintOpacity` (0–1). `MaterialThickness`, `Tinted` and `Solid` are gone: a tinted panel is a tint opacity without a kind, a solid one is tint opacity 1.
- **Intensity on iOS** is the animation from one effect to the other, paused part of the way (`UIViewPropertyAnimator.FractionComplete`), restarted when the view comes back on screen or the app to the foreground. A blur goes from none to the ultra-thin system material; glass morphs from UIKit's clear glass to its regular glass (pixel-identical at the ends).
- **Presets** (`Material.Preset`: `GlassClear`, `GlassRegular`, `BlurUltraThin`, `BlurThin`, `BlurRegular`, `BlurThick`) are named sets of Kind, Intensity and TintOpacity that a view's own values override; `Material.Values(preset)` returns them.
- **Interactive glass** holds the view's content inside its effect view while it is interactive: UIKit only lets glass react to touches on views inside it, and MAUI's content lay beside it.
- The header bar's scroll edge keeps the system thin and standard materials it is tuned against, through an internal `SystemBlur`.

Decisions:

- **Thickness became tint opacity.** Apple's thicker materials blur about as much as the ultra-thin one and differ mostly in milk (measured over the sample photo: thin, regular and thick are ultra-thin plus about 50, 75 and 90 % of the surface). That is why only ultra-thin read as a blur, and why the milk is its own dial.
- **The blur presets are a scale of their own, not copies of Apple's.** Copies made thin, regular and thick nearly solid on the phone; Jonatan wanted blur and milk to grow together (ultra-thin 0.3/0, thin 0.55/0.15, regular 0.8/0.3, thick 1/0.45).
- **Chrome has no preset**: it is the system bars' material and sits between regular and thick.
- **The sample hero uses `BlurUltraThin`**, chosen on the phone.
