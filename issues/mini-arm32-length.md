# Mini: NaN inside the body on 32-bit ARM

**GitHub:** no issue; found in TV:Dash on the Chromecast with Google TV (armeabi-v7a) on 2026-11-01.
**Branch:** fix/mini-arm32-length
**Status:** In review

## What happened

On the Chromecast the Mini drew as a ring in its body colour: a band as wide as the shape's corner radius along the
outline, nothing inside, no eyes. The Android emulator (x86) and the Avatar Lab on a Mac drew it whole. On Android
the avatar runs on the CPU (SKCanvasView), and Skia's raster pipeline on 32-bit ARM has no square-root instruction:
it takes `sqrt` as `v * rsqrt(v)` from a NEON estimate, which is NaN at exactly zero. `roundBox` (the body of the
box-like shapes and every pill eye) takes `length(max(d, 0.0))`, zero over the whole inside, so every inner pixel
became NaN; the band near the edge, where `max(d, 0.0)` is not zero, was the only part drawn, and the eyes were cut
by the NaN body distance.

## Changes

- `samples/SpineAvatarLab/Tools/mini_avatars.sksl`, `mini_avatars.py`: `vlen(v) = sqrt(dot(v, v) + 1e-12)` in
  place of every `length()`; the floor moves no distance by more than a millionth.
- `samples/Avatars/mini.spineavatar` regenerated and packed (`--strip --size 192`).
- `docs/wiki/avatars.md`: the caveat under "Making a package", for anyone writing an `sksl` avatar.

## Decisions

- The fix is in the shader, not the renderer: Skia's pipeline is SkiaSharp's, and the other `sksl` avatars in
  `samples/Avatars` have not been checked on a 32-bit device; the wiki says what to look for.
