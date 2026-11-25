# Issue #880 — Android: the blur material records what is behind it on every frame

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/880
**Branch:** issue/880-android-blur-material-per-frame
**Status:** Completed

## Plan
`MaterialDrawable.RecordBehind` runs before every frame for every glass view and re-records everything behind the view, with a JNI call or an allocation for each ancestor and sibling. Make the steady state a few position checks: gather what is behind once per layout pass, record the node again only when something behind it has changed in a way its display list cannot carry, and reuse the `RenderEffect`. Find the 525×182 texture that the tablet uploads every frame.

## Changes
- What is behind (ancestor backgrounds, earlier siblings and their sizes) is gathered after each layout pass (`OnGlobalLayout`), on attach, on `Update` (theme, shape) and when the window shows again (`SetVisible`), not every frame.
- Before a frame the drawable checks the host's and each item's position in the window, the siblings' visibility and alpha, and `IsDirty` for siblings that draw themselves into the node (not view groups, texture or surface views); the node is recorded again only when one of those changed, or after 500 ms of frames as a safety net.
- The blur `RenderEffect` is created only when the radius changes (it is a node property, so no new recording), and the node's position only when the size changes.
- The drawable implements `IOnPreDrawListener` and `IOnGlobalLayoutListener` itself instead of the `PreDraw` event (no event args per frame); one `int[2]` is reused for every location.
- The clip path is kept between draws, and a `RoundRectangle` is clipped with `Path.AddRoundRect`.

## Decisions
- **Why skipping the recording is safe for what moves behind a glass view.** `View.Draw` into the node copies only a view's own drawing; a view group's children go in as references to their render nodes, and a `TextureView` as a reference to its layer. Their new content and their render-node properties (translation, alpha) reach the blur without the node being recorded again. What is copied (a leaf view's own drawing, an ancestor's background) is checked with `IsDirty` or caught by layout, `Update` or the 500 ms refresh.
- **The 525×182 upload** was the foot card's material clip: `IShape.PathForBounds` gives a generic path, which Skia (Ganesh) cannot clip analytically, so it rasterizes a coverage mask on the CPU and uploads it; the mask was redrawn every time the clipped content (the re-recorded node) changed, i.e. every frame. With the node recorded rarely it dropped to the re-records (252 → 4 per trace); with the rounded rectangle given as one (`SkRRect`, clipped on the GPU) it is gone (0).
- Measured in Almanacka on a Galaxy Tab S10 FE (90 Hz, three glass views, leaf turns): janky frames 22.2 % → 1.2–1.8 %, median frame 10 → 5 ms, main-thread `OnPreDraw` time ~3.6 → ~0.5 ms per frame, `Texture upload 525x182` every frame → none.
