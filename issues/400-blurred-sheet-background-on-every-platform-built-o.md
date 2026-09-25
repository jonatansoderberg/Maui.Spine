# Issue #400 — Blurred sheet background on every platform, built on Material

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/400
**Branch:** issue/400-blurred-sheet-background-on-every-platform-built-o
**Status:** Completed

## Plan

`BackgroundPageOverlay.Blurred` gets one look on every platform: the `MaterialPreset.BlurThin` values (blur at intensity 0.55, the theme's surface as tint at 0.15), drawn with the Material implementation from #300/#399. It fades in with the sheet and out when the sheet closes. No new public API.

### Shared
- The overlay's material is described once: a detached `ContentView` with `Material.Preset="BlurThin"` serves as the owner the platform material views already read their values from (`MaterialSurfaceView`, `MaterialDrawable`, the Windows brush). That reuses `Material.Resolve`/`TintLayer`, so Reduce Transparency, the fallback tint on old Android and dark mode behave as they do for any Material surface.

### iOS / Mac Catalyst (`Presentation/BottomSheetPageExtensions.Apple.cs`)
- `AddBlurOverlay` / `RemoveBlurOverlay` replace the `SystemMaterial` `UIVisualEffectView` with a `MaterialSurfaceView` over the presenting view.
- **Fade in:** the overlay starts with no effect and moves to the BlurThin blur alongside the presentation through `sheetVc.TransitionCoordinator.AnimateAlongsideTransition`.
- **Fade out:** the same alongside the dismissal. For an interactive dismissal (dragging the sheet down) the coordinator's `NotifyWhenInteractionChanges` handles a cancelled drag so the blur comes back. The blur is removed only after the transition completes, not the moment `tcs` resolves.
- `MaterialSurfaceView` gets an internal way to animate from "no effect" to its material. Its intensity is already a paused `UIViewPropertyAnimator`, so the fade animates that animator's target instead of `Alpha`, which UIKit does not support on effect views.
- `largestUndimmedDetentIdentifier` stays as it is for `Blurred`, so the system dim does not stack on top of the blur.

### Android (`Platforms/Android/BottomSheetPageExtensions.Android.cs`)
- The sheet is its own dialog window, and Material's `RenderNode` blur can only record views in its own window. So the overlay is a plain `View` with a `MaterialDrawable` added to the **activity's** decor view (the page's window) over the content, and the dialog's `DimBehind` is cleared for `Blurred`.
- The overlay fades with `Animate().Alpha()` together with the sheet's slide-in and slide-out, and is removed when the dialog is dismissed.
- Before Android 12, `Material.Resolve` gives `None`, and the drawable draws the tint at `StandInTintOpacity`. The page is covered by a milky scrim instead of a blur, which is the fallback Material already uses. (The issue suggested the dim; the stand-in tint keeps the look closer to the blur. See Open Questions.)

### Windows (`Platforms/Windows/BottomSheetPageExtensions.Windows.cs`)
- `Blurred` already falls to acrylic through the `_` case, with hard-coded tints. It now uses the same brush `MaterialExtensions.Windows.cs` builds for a Material surface, from the BlurThin values, so the tint follows the theme and transparency effects turned off gives the opaque fallback.
- Fade: the overlay `Grid`'s `Opacity` animated from 0 to 1 with the sheet's slide, and back on close.

### Sample and docs
- The Bottom sheets sample already opens sheets with `Blurred` (`SimpleBottomSheetPage`, `FullscreenSheetPage`, `SmallSheetPage`); check them on every platform.
- `docs/wiki/sheets.md`: describe what `Blurred` draws per platform and the fallback before Android 12.
- `BackgroundPageOverlay.Blurred` doc comment: no longer "availability depends on the platform".

### Verification
- iOS simulator: open, drag to dismiss (both cancelled and completed), close with the button, in light and dark mode, and with Reduce Transparency on.
- Android emulator (API 34) and, if one is available, an image before API 31.
- Windows via CI build. There is no Windows machine for visual checks.

## Open Questions


## Changes

- `MaterialSurfaceView` (iOS) gets `Presence`, from 0 to 1: the blur and the tint scale together. While only the presence changes, the paused animator already running is moved to the new fraction, not started again.
- `MaterialDrawable` (Android) gets the same `Presence`: the blur radius and the fill scale together.
- iOS: `SheetBlurOverlay` replaces the `SystemMaterial` effect view. It is a `MaterialSurfaceView` with the `BlurThin` preset over the presenting view. A `CADisplayLink` sets its presence from the sheet's presentation layer: the part of the sheet's own height that is on screen. That covers the slide in, the slide out, a drag, and a drag that is let go, and it is 1 at every detent.
- Android: `SheetBlurOverlay` is a view with a `MaterialDrawable` added to the activity's decor view, with the dialog's dim cleared. Its presence is a 250 ms fade (the dialog's window slide, which no callback reports) times the part of the smallest detent that shows (`onSlide` and the post-layout position). Closing fades it out and removes it.
- Windows: the `Blurred` brush is `MaterialBrush` from a `BlurThin` owner (made `internal`). The overlay already faded in and out with the sheet.
- `BackgroundPageOverlay.Blurred` doc comment and `docs/wiki/sheets.md`: what `Blurred` draws per platform.
- Verified on Jonatan's iPhone (looks good), the iPhone 17 simulator (iOS 26.4: opens blurred, the blur thins frame by frame as the sheet slides away) and the Pixel 10 Pro emulator (API 37: blurred when open, weaker mid-drag and back when let go, gone after closing).

## Decisions

- **Android: Material overlay rather than the system's cross-window blur** (Jonatan's choice): it looks like Material elsewhere and follows the same values; the system's `BlurBehindRadius` is off on some devices and cannot be tuned.
- **Default: the `BlurThin` preset** (Jonatan's choice).
- **No overlay material API**: `Blurred` only; a `BackgroundPageMaterial` option can come later if an app needs it.
- **Android before 12: the material's stand-in tint** (Jonatan's choice) rather than the ordinary dim, closer to the blurred look.
- **The blur follows the sheet's position, not the transition coordinator** (iOS): one rule gives the fade in, the fade out and the drag. It also avoids animating `Alpha` on an effect view, which UIKit does not support.
- **Android fades the blur radius, not the view's alpha**: an alpha fade shows a sharp page through a blurred one mid-drag. Shrinking the radius matches iOS.
- **Constant strength**: the blur does not follow the detents; it fades in on open, out on close, and follows the finger when the sheet is dragged away on iOS.
