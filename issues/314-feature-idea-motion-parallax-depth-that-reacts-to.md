# Issue #314 — Feature idea: Motion parallax — depth that reacts to device tilt

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/314
**Branch:** issue/314-feature-idea-motion-parallax-depth-that-reacts-to
**Status:** In Review

## Plan

An attached `Motion.Depth` (points, `double`, default 0) in `Plugin.Maui.Spine.Extensions`, in the core package, with nothing to register. It follows the pattern of `ContextMenu.Items` and `Tap.Command`: a per-view state object that follows the view's handler and hooks the platform view.

```xml
<Image Source="leaf.png" Motion.Depth="12" />     <!-- floats above the screen -->
<Border Motion.Depth="-6" ... />                  <!-- sits behind it -->
```

- **Sign:** a positive depth floats above the screen and moves toward the edge that tilts away from the viewer; a negative depth sits behind it and moves the other way. Layers with different depths give parallax.
- **iOS:** a `UIMotionEffectGroup` of two `UIInterpolatingMotionEffect`s (`center.x`, `center.y`, from `-Depth` to `+Depth`) on the view's container view or platform view. The system runs it, recentres it and turns it off under Reduce Motion.
- **Android:** one shared `SensorEventListener` on the game rotation vector (rotation vector as the fallback; nothing on a device without either). It is registered only while at least one view with a depth is attached to a window, the app is started (`Platform.ActivityStateChanged`), and animations are not removed (`ReducedMotion.IsOn`). The tilt is measured against a neutral attitude that slowly follows the device (so lying on a table or held upright both rest at the centre), smoothed, mapped to screen axes for the display rotation, and applied as an extra platform translation on top of the view's own `TranslationX`/`TranslationY`.
- **The maths** live in a MAUI-free `TiltTracker` (Core) so it can be unit tested in `tests/Plugin.Maui.Spine.Core.Tests`.
- **Mac Catalyst and Windows:** no tilt, so a no-op.
- **Highlight sweep on materials:** composed from the same property — a soft gradient inside a material `Border` with its own depth, clipped by the border. No new API in v1; the Showcase and the wiki show the recipe.
- **Showcase:** a "Motion" page in `samples/MauiSpineSampleApp/Pages/Motion/` with layered cards (background, card, foreground) and a glass card with a moving highlight; `SampleIndex`, `GlobalXmlns.cs`, csproj.
- **Docs:** `docs/wiki/motion.md`, README rows, `/spine-controls` skill line.

## Open Questions

None blocking; decisions are recorded below for review.

## Changes

- `src/Plugin.Maui.Spine/Extensions/Motion.cs`: `Motion.Depth` and the per-view `MotionState` (follows the handler, hooks the container view when MAUI wraps the view).
- `src/Plugin.Maui.Spine/Extensions/Motion.Apple.cs` (iOS only): a `UIMotionEffectGroup` of two `UIInterpolatingMotionEffect`s, `center.x`/`center.y` from `-Depth` to `+Depth`, replaced when the depth changes and removed when it goes to 0 or the handler goes.
- `src/Plugin.Maui.Spine/Platforms/Android/Motion.Android.cs`: one shared listener on the game rotation vector (rotation vector fallback) at 60 Hz, registered while a view with a depth is attached to a window, the app is started and animations are not removed; the offset is added to the platform translation, and `ViewHandler.ViewMapper`'s `TranslationX`/`TranslationY` mappings are appended (from `UseSpine`, in the Android `ConfigureHandlers`) so MAUI's own translation keeps the offset.
- `src/Plugin.Maui.Spine/Core/TiltTracker.cs`: the MAUI-free maths (neutral attitude that follows the device, viewer direction, display rotation, clamp, smoothing), with `tests/Plugin.Maui.Spine.Core.Tests/TiltTrackerTests.cs` (directions against UIKit's viewer offset, clamp, recentring, the three turned displays).
- Showcase page **Motion** (`samples/MauiSpineSampleApp/Pages/Motion`): three layers over a photo, a highlight that sweeps over a `BlurThin` panel, and a slider to try a depth; `SampleIndex`, `GlobalXmlns.cs`, csproj.
- Docs: `docs/wiki/motion.md`, a line in `docs/wiki/materials.md`, README feature and guide rows, the `/spine-controls` skill.

## Decisions

- **Sign: positive floats above the screen.** It moves toward the edge that tilts away from the viewer, like the common `-d…+d` `UIInterpolatingMotionEffect` idiom; negative sits behind it (the issue's `MeshBackground Motion.Depth="-6"`). UIKit documents its viewer offset as positive when the screen points to the viewer's right or down, which gives this direction; Android's `TiltTracker` is derived to match and the tests pin it.
- **Mac Catalyst: no-op, no pointer-follow.** Following the pointer would make the layers move whenever the mouse crosses the window, which is a different effect from tilt and easy to find distracting on a desktop. Left as a follow-up; the iOS file is compiled for iOS only, so nothing is attached on the Mac.
- **Android: full depth at ~20° (sine 0.35), recentring with a 3 s time constant, 60 ms smoothing, 60 Hz sampling.** Chosen to feel close to iOS, whose numbers are not documented. Recentring makes a phone held upright or lying flat rest at the centre, as iOS does.
- **Android: no accelerometer-only fallback.** Gravity cannot see a turn about the vertical axis, and building an attitude from gravity alone has a singular pose; devices without both rotation sensors (no gyroscope and no magnetometer) are rare, and there the views simply stay put.
- **Android: background detection from `Platform.ActivityStateChanged`** (Started/Resumed/Stopped of the current activity), so the listener needs nothing from `UseSpine`. Reduce Motion (animator duration scale 0) is read through the existing `ReducedMotion.IsOn` whenever the listener is re-evaluated (a view attaches or leaves, the app comes back); changing the setting means leaving the app, so that covers it.
- **Android: the offset is a translation on the platform view (container if wrapped), added to MAUI's own.** The alternative, `View.setAnimationMatrix`, is API 29+. Appending to the global `ViewMapper` translation mappings keeps a running `TranslateTo` from dropping the offset between sensor events.
- **Highlight sweep: a recipe, not an API.** A radial-gradient `Border` with a deep negative depth inside a material panel gives the moving reflection with what v1 already has, on iOS and Android alike. A built-in `Motion.Highlight` (or a material option) can follow if the recipe proves common.
- **The translation mapping is appended in `UseSpine`, not when the first depth is set.** Appending lazily looked right (`GetProperty` returned the new chain) but a handler mapper that has already run keeps calling its cached action, so a `TranslationX` change dropped the offset until the next sensor event. Found on the emulator with the harness.

## Verification

- **iPhone 17 Pro simulator (harness):** every view with a depth carries one `UIMotionEffectGroup`; `keyPathsAndRelativeValues` gives `center.x/y = +depth` at viewer offset (+1, +1), `-depth` at (-1, -1) and half the depth at 0.5. The card with a shadow gets it on MAUI's `WrapperView`. Changing the depth replaces the effects, setting it to 0 removes them. Light and dark screenshots with the layers moved by hand, since the simulator has no motion.
- **Android emulator (Pixel_Tablet on emulator-5556, portrait on a landscape-native display, so `ROTATION_90`):** turning the virtual gyroscope (`adb emu sensor set gyroscope-uncalibrated`) about the device's x axis moved the badge (+16) right by 12 points and the photo (-14) left, the vertical axis moved them up and down, and the layers settled back over a few seconds. `dumpsys sensorservice` shows the listener registered at 60 Hz when the page opens, unregistered on Back and on Home, registered again on return, and not registered with `animator_duration_scale 0` (the views stay at 0). A `TranslationX` set through MAUI keeps the tilt offset on top.
- **Builds:** `Plugin.Maui.Spine` for Android, iOS, Mac Catalyst and (compile only) Windows; the Showcase for Android and iOS; `Plugin.Maui.Spine.Core.Tests` (37 passed).
- **Not verified:** real tilt on an iPhone or an Android phone (direction and feel, iOS vs Android agreement), the rotation-vector fallback, the Mac Catalyst and Windows no-op at run time.
- **Seen in passing:** on this emulator the Android material blur draws a blurred band at the top of a panel and the surface colour below it, on the Materials page too (not caused by this change).
