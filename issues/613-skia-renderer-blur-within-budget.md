# Issue #613 — Skia renderer: blur within budget

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/613
**Branch:** issue/613-skia-renderer-blur-within-budget
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), step S5

## Plan
- Find where the blur costs now: since B1.2 Skia draws on Metal on iOS and Mac Catalyst, so the lab's CPU blur numbers apply to Android and Windows (`SKCanvasView`), not to the iPhone.
- Time the blurred scenes (Pip, Aurora, Aurora Motion) on the CPU raster at 320 px and at 960 px (a 320 DIP view at 3×), drawn directly and with each blurred node cached as an image.
- Keep the cache if the pictures match and it costs less.

## Changes
- `Spine2dRenderer`: on a CPU canvas (`canvas.Context` is null) a blurred node is drawn from a `BlurredLayer` image. The image holds the node's fill and stroke with its mask blur, in the node's own coordinates, at half its device scale, padded by three sigmas plus half the stroke. It is made again when the scale moves by more than a quarter, when a path node's points change and when the theme or accent changes; each frame draws it with the node's transform, opacity and blend mode. The GPU path draws as before. `CacheBlurs` (internal) turns it off for comparison.
- The node's fill and stroke drawing moved into `DrawNode`, shared by both paths.
- `Spine2dTimingTests`: each blurred scene drawn directly and cached, times to the output, pictures compared (largest channel difference at most 16) and saved to `bin/…/skia2d/`.
- Plan: the S5 row, §8.1, the backlog, Last updated.

## Measurements
Mac (Apple silicon, CPU raster, Release, 120 frames after 20 warm-up; the machine was at load average 24–36 from other work):

| Scene | Size | Direct p50 / p95 | Cached p50 / p95 | Largest channel difference |
| --- | --- | --- | --- | --- |
| Pip | 320 px | 0.58 / 0.65 ms | 0.54 / 0.59 ms | 10 |
| Pip | 960 px | 4.44 / 4.69 ms | 3.84 / 4.02 ms | 4 |
| Aurora | 320 px | 1.42 / 1.53 ms | 1.28 / 1.37 ms | 8 |
| Aurora Motion | 320 px | 2.44 / 2.61 ms | 1.86 / 1.95 ms | 9 |
| Aurora Motion | 960 px | 20.30 / 21.64 ms | 14.13 / 15.20 ms | 7 |

In an earlier, noisier run the direct path's p95 reached 90 ms for Aurora Motion at 960 px while the cached one stayed at 16 ms: a mask blur allocates and fills a mask every frame. Each layer was made once over 140 frames. Aurora Motion drawn without any blur costs 9.5 ms at 960 px, so its fills, not its blurs, are the floor on a CPU.

## Decisions
- **Cache, not lower resolution for the whole scene**: the cache leaves sharp shapes sharp, and only the blurred ones (which have no detail to lose) are made at half resolution.
- **CPU only**: on Metal a blur is a GPU pass and the cache would add uploads; the iPhone path is unchanged.
- **Thresholds**: a quarter either way in scale before remaking, so breathing and small gestures reuse the image and a large change stays sharp.
- **Aurora Motion and Pip are lab material (D5)**; no bundled avatar uses blur, so this mainly protects downloaded or third-party packages on Android and Windows.

## Not verified (NotMeasured)
- Pip and Aurora Motion at 4 ms p95 on the iPhone, the step's done-criterion (Metal; this change does not touch it).
- The cached blurs on an Android phone at 320 DIP.
