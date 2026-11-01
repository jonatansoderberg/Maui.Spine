# Avatar: an `sksl` shader on the GPU on Android

**GitHub:** no issue; found in TV:Dash on the Chromecast with Google TV on 2026-11-01.
**Branch:** feat/avatar-gl-android (on fix/mini-arm32-length)
**Status:** In review (opt-in; not yet right on the Chromecast)

## What happened

On the TV the Mini looked soft and stepped. On Android the avatar drew on the CPU through an `SKCanvasView`, and a
shader on the CPU renders into a buffer of at most 112 pixels on a side (`AvatarShaderRenderer.MaxPixels`) that is
scaled up with linear sampling: the assistant card on the TV is 350–575 device pixels, a 3–5× upscale of the
anti-aliased edge. The box's GPU (a Mali-G31) sat unused, and the CPU (four Cortex-A53, 32-bit) could not afford a
bigger buffer: the raster pipeline runs the shader per pixel, interpreted.

## Changes

- `Surfaces/SkiaAvatarSurface.cs`: on Android an `AvatarShaderRenderer` draws through an `SKGLView` (OpenGL ES,
  `HasRenderLoop = false`, painted on `InvalidateSurface()`) at full size, `MaxPixels = null`, renderer name
  `skia (OpenGL ES)`. Vector scenes and the Skia glTF renderer stay on the `SKCanvasView`. The GL view paints on its
  own thread, so `Render` (the frame copy, theme colours, options) and `Paint` (evaluate, draw) take one lock, and
  `Dispose` marks the renderer gone under it.
- `SpineAvatarOptions.GpuOnAndroid`, off by default after the Chromecast (below); on, a shader takes the GL view.
- `docs/wiki/avatars.md`: the renderer sentence and the 32-bit ARM caveat.

## Decisions

- Shaders only: a `spine2d` scene draws its paths anti-aliased at full size on the CPU already, and its blurred
  layers are rasterised on the CPU and cached; the shader is the one representation that loses detail there.
- SkiaSharp's MAUI handler makes the Android GL view a `TextureView` with `SetOpaque(false)`, so the avatar's
  transparent background composes in a layout, and MAUI animations on the card (fade, slide) work as before.
- The `MaxPixels` buffer and `vlen` (PR #730) stay for the CPU path: Windows, and Android with the option off.

## On the Chromecast (Mali-G31, Android 14), 2026-11-01 evening

TV:Dash with this package: the first opening of the assistant card drew the Mini crisp at full size, but with the
shape and eyes of the option defaults (ball, round) although the preset and the app's choice say tall; the second
opening, after the card had been hidden and shown again, drew nothing, and the log shows no new GL thread (the Mali
driver's start-up lines came only at the first opening). The Google TV emulator (SwiftShader) draws every frame and
honours the options, both openings. The reading so far: on the device the texture view drew its first frames and
stopped, before the preset binding and the app's `SetOption` reached it, and did not come back after the surface
was destroyed and recreated. Not confirmed: there is no instrumentation in the Release build. Until it is understood
the option is off by default and TV:Dash sets it off explicitly; the CPU path with the `vlen` fix is what the box runs.
Next: a build that logs the GL view's paint count, surface size and `GRContext` changes, read over adb on the box.
