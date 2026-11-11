# Issue #800 — SVG images are soft on Windows: rendered at 1× and stretched by the display scale

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/800
**Branch:** issue/800-svg-soft-on-windows
**Status:** Completed

## Plan

Reported from AIDash Connect: SVGs are sharp on the Mac, iOS and Android, soft on Windows. `SvgBitmapLoader.ScreenScale`
was 1 on Windows and MAUI's stream service decoded the PNG, so a 22-point glyph was a 22-pixel bitmap stretched to 44
pixels on a 200 % display. Render at the display's density as elsewhere and add the Windows service the other platforms
have.

## Changes

- `SvgBitmapLoader.ScreenScale`: `DeviceDisplay.MainDisplayInfo.Density` on every platform.
- `SvgBitmapImageSourceService.Windows.cs` (new): decodes the PNG into a `BitmapImage` with `DecodePixelType.Logical`
  and the bitmap's size in points (its IHDR size over `SvgBitmapImageSource.Scale`), so WinUI keeps the pixels and lays
  the image out at its point size.
- `UseEmbeddedSvgImages` registers the service on Windows too.

## Decisions

- **The point size comes from the PNG's header**, not from the caller: the loader's padding and rounding already went into
  the pixel size, and the header is 24 bytes at the start of a stream that is in memory.
- **The main display's density**, as on the other platforms. A window on a second display with another scale gets the
  main display's bitmap, which WinUI scales; the same trade-off iOS and Android make.
- Other Windows users of the loader set their own size and only get more pixels: menu icons (`BitmapIcon` from a file
  of the right pixel size, unchanged), the segmented control (`ImageIcon` with `Width`/`Height`), tab icons (96 points).

## Verified

- Windows at 200 %, AIDash Connect with this branch's `Plugin.Maui.Spine.Svg.dll`: every `SvgImageSource` image has
  twice its point size in pixels (`Image` 46 × 32 with a 92 × 64 `BitmapImage`, logos 30 × 30 with 60 × 60) and keeps
  its laid-out size; a `RenderTargetBitmap` of the window next to one with the released 0.261111.1530 shows the header
  glyphs and the logos sharp instead of soft.
- Not verified: a 100 % or 150 % display, a second display with another scale.

## Follow-up: the first icon threw before the window was active (0.261111.1536)

- **Symptom:** AIDash Connect on 0.261111.1536 started without a window: `SvgRasterizer.RenderPng` threw a
  `NullReferenceException` from `SvgImageSourceBehavior.UpdateImage` during startup.
- **Cause:** `DeviceDisplay.MainDisplayInfo.Density` is 0 on Windows until the app's window is active, so the bitmap was
  0 × 0 pixels, `SKImage.FromBitmap` gave no image and the encode threw. Windows used a fixed 1 before #800.
- **Fix:** `ScreenScale` takes `GetDpiForSystem() / 96` on Windows, which exists before any window, and falls back to 1
  for any scale that is not positive; `RenderSvgToPng` never asks for less than 1 × 1 pixels.
- **Verified:** AIDash Connect with the fixed `Plugin.Maui.Spine.Svg.dll` starts with its window, logs nothing, and its
  SVGs are sharp at 150 %.
