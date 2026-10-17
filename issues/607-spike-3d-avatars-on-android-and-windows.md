# Issue #607 — Spike: 3D avatars on Android and Windows

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/607
**Branch:** issue/607-spike-3d-avatars-on-android-and-windows
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step S2, decision D1

## Plan
- Try route (a) first, as D1 recommends: a Skia renderer for GLB avatars on the CPU (the shared `GltfAvatarRig` poses the model; morph, skin, light per vertex; back-face cull; painter's sort; one `DrawVertices`).
- Measure it on the Mac (unit test, CPU raster) and on the Android emulator (scratch app outside the repo, the renderer registered through the renderer door, then built in).
- Route (b), Filament, only if (a) cannot hold 30 fps at 320 DIP.
- Write the result into D1 before the renderer lands in the package for Android and Windows.

## Changes
- `Skia/GltfSkiaRenderer.cs`: `GltfSkiaModel` (with `TryCreate`, which refuses models with alpha-masked materials) and `GltfSkiaRenderer`. Per frame: rig, world matrices, joint matrices, vertices morphed, skinned, transformed and lit into colours through an sRGB table, triangles culled and sorted by depth (blended ones last), one `SKVertices`. `LastPhases` gives posing, sorting and rasterising times.
- `Surfaces/SkiaAvatarSurface.cs`: `GltfSkiaAvatarRenderer`, the built-in renderer for `native3d` GLB where SceneKit is not; `AvatarView` adds it on Android and Windows and compiles its model off the UI thread, skipping a representation it refuses.
- `AvatarSchemas`: pattern regexes are `NonBacktracking` with no timeout (the 100 ms timeout failed Nova's load on a busy emulator).
- Tests: `GltfSkiaRendererTests` (which models are taken; Nova at 320 and 960 px, Puff, Pebble Bot, Robot Expressive timed over 120 frames, pictures in `bin/…/skia3d/`); a `Measurements` collection that runs the timing and allocation tests alone; tiered compilation off in the test project.
- Showcase: the gallery's glTF cards say "glTF · Skia" off Apple.
- Docs: `docs/wiki/avatars.md`, the package README and the `spine-controls` skill name the Skia glTF route and its limits; the plan's S2 row, D1, §3, §4.5, §8.1 and backlog.

## Measurements
Mac (Apple silicon, CPU raster, Release, 120 frames after 20 warm-up):

| Model | Size | p50 | p95 | Phases of the last frame (pose / sort / raster) | Per frame |
| --- | --- | --- | --- | --- | --- |
| Nova | 320 px | 4.3 ms | 4.5–7.4 ms | 0.23 / 1.35 / 2.55 ms | 80 B |
| Nova | 960 px | 6.0 ms | 8.6 ms | 0.23 / 1.36 / 4.12 ms | 80 B |
| Puff | 320 px | 1.2 ms | 1.3 ms | 0.07 / 0.33 / 0.84 ms | 80 B |
| Pebble Bot | 320 px | 1.9–2.3 ms | 2.1–3.7 ms | 0.05 / 0.54 / 1.29 ms | 80 B |
| Robot Expressive | 320 px | 0.4 ms | 0.4 ms | 0.07 / 0.07 / 0.27 ms | 80 B |

Before the optimisations (sRGB table, cull before sort, keyed `Array.Sort`, collapsed arrays) Nova was 15.7 ms in Debug. The 80 B is the `SKVertices` wrapper object.

Android emulator (Pixel 10 Pro image, arm64, Release, 320 DIP = 960 px): all four draw correctly (posed, lit, culled; screenshot checked), but at 2–4 fps: Nova p50 200–260 ms, Robot Expressive 14–17 ms. The host was saturated by other work (load average 25–33): the 2D Pip ran at 3 fps and 114 ms in the same run, the same sort of 20 k floats took 6 to 168 ms from one round to the next, and a plain float loop ran 200–450× slower than on the Mac. Those numbers measure the host, not the renderer. A GL-backed canvas (`SKGLView`) gave no better numbers under the same load.

## Decisions
- **D1 is (a)**, the plan's recommendation: one Skia path for Android and Windows, SceneKit kept on Apple. Filament (b) is not started; it starts only if C3's phone measurement shows Nova under 30 fps at 320 DIP.
- **Built in, not a package or a door renderer**: it uses only SkiaSharp, which the package already has, and takes the existing `native3d` id, so neither the format nor the public API changes. The renderer door stays `[Experimental]` until a renderer package uses it.
- **Refused: alpha-masked materials, not textures in general.** The plush keep their look from the base colour (checked on Puff), but a masked material's shape is its texture (Kairo's hair cards, MPFB's lashes), so those models show their posters.
- **The validator's regexes are linear (`NonBacktracking`) with no timeout.** The patterns are the embedded schemas', all simple; a timeout guarded nothing and failed a valid load on a slow device.
- **Measurement tests run alone and without tiering.** Zero-allocation asserts began to fail now and then (8 KB, one allocation context) once the renderer test allocated heavily beside them. After both changes: 13 full runs, one failure, in an older, unrelated test (`AvatarShaderTests.VoiceGrowsTheRibbons`, a brightness comparison that passes when run on its own).

## Not verified (NotMeasured)
- Nova, Puff and Robot Expressive at 30 fps at 320 DIP on an Android phone, and the per-frame cost there.
- The renderer on Windows: compiled, never run.
- The emulator's frame rate on an idle Mac.
