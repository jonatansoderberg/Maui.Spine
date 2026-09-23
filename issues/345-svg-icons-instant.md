# Issue #345 — SVG icons appear a second after their page on Android

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/345
**Branch:** fix/svg-icons-instant
**Status:** In Progress

## Plan
Timing logs put every SVG step at a few milliseconds; the lag is the drawable reaching the `ImageView` after the RecyclerView cell's first layout. Three changes in `Plugin.Maui.Spine.Svg`:
1. `SvgImageSourceBehavior.UpdateImage` sets the source directly when already on the main thread.
2. `SvgBitmapImageSourceService` (Android) decodes the in-memory PNG synchronously, so MAUI's handler continues inline and the drawable is set before the first layout.
3. `SvgBitmapLoader.WarmUp` runs on a background thread from `UseEmbeddedSvgImages`, loading Svg.Skia and SkiaSharp ahead of the first icon.

## Changes

- As planned; verified by screenshotting the Android launch every half second: rows and icons now appear in the same frame.

## Decisions

- No disk cache of rasterized PNGs: rendering is milliseconds once the engine is loaded, so the warm-up is enough and nothing has to be invalidated on updates.
