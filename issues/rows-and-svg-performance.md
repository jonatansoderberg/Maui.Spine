# Rows and SVG images: a settings page of sixty rows took three seconds on Windows

**Branch:** perf/rows-svg
**Status:** Completed

## Plan

Profile the navigation to AIDash Connect's Settings (about sixty `SpineRow`s in `SpineSection`s) on Windows, find what
the three seconds go to, and fix what is Spine's.

## What the profile showed

`dotnet-trace` on the UI thread, from the tap to the page (Windows at 150 %, Debug):

- Handlers, about 1.2 s. Every `SpineRow` made all its parts in its constructor: icon badge, value, value below,
  On/Off label, combo box, accessory host, status mark and its glyph, status dot, detail line, check, chevron and menu
  glyph. Most of them stayed hidden in most rows, and each one became a WinUI view.
- SVG images, 0.6–0.8 s. `SvgImageSource.ApplyValues` set eight properties on the behavior, and each one rendered:
  eight renders of combinations nobody asked for, every time one attached property changed. A row sets four or five
  of them, so a row's few glyphs rendered dozens of times on the UI thread. Each render then decoded the same bitmap
  again when the layout gave the view the size it had asked for.
- `DetailMarquee`, about 0.8 s. Every such row made an `AnimatedLabel`, which is a Skia canvas, also when its text fit.

## Changes

- **`SpineRow`:** the title and its column are made in the constructor; every other part is made the first time the row
  needs it and painted then (`Repaint` paints only the parts there are). Parts made after a menu button go under it and
  are hidden from a screen reader as before.
- **`SpineRow` detail:** with `DetailMarquee` the detail is a plain label until its text is wider than its room (checked
  after layout and when the text changes); then the row makes the `AnimatedLabel`.
- **`SvgImageSource` / `SvgImageSourceBehavior`:** `ApplyValues` sets the values with updates suspended and renders once.
  A view without a handler doesn't render; it renders once when its handler comes. A render that gives the same result
  as the one shown (same SVG, size, tint, padding, line width, theme) does nothing.

## Verified

- Windows at 150 %, AIDash Connect, profiled again: handlers 0.75 s instead of 1.2, the page's constructor 0.43 s
  instead of 0.80, SVG rendering no longer among the top entries; the navigation itself 0.55 s instead of 0.88.
- The page looks as before: switches with On/Off, the status dot, chevrons, combo boxes, values, icons.
- Not tried: iOS, Android (`#345`: an image in a collection cell renders when its handler comes, in the same pass), the
  Mac.
