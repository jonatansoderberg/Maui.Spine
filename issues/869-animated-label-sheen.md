# Issue #869 — AnimatedLabel: a metal sheen with a glint, and a leading icon

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/869
**Branch:** issue/869-animated-label-sheen
**Status:** In Progress

## Plan
Hockeymatch shows the supporter tier as a crown and "GULD · APPSUPPORTER" in the tier's metal and wants it to shine. AnimatedLabel already pre-renders its text into an image with SkiaSharp, so:
- the metal is a gradient blended SrcIn over that image when it is built;
- the glint is a slanted white band drawn SrcATop over the image in a layer, only while a sweep runs;
- sweeps are scheduled with one `DispatchDelayed` each, so nothing ticks between them;
- the icon is drawn into the same image with Spine.Svg, which gets a public `SvgDrawing.Draw`.
The Showcase shows gold, silver and bronze with options before an app uses it.

## Changes
- `AnimatedLabelSheen` (None, Glint, Metal); `Sheen`, `SheenIntervalMs`, `SheenDurationMs`, `Glint()`.
- `Icon`, `IconSize`, `IconSpacing`, `IconLineWidthScale`; `CharacterSpacing`.
- `Plugin.Maui.Spine.Svg.SvgDrawing.Draw(canvas, name, bounds, tint, lineWidthScale)`.
- AnimatedLabel references Spine.Svg.
- Showcase: "Metal that catches the light" on the AnimatedLabel page, with options and Glint now.
- docs/wiki/animated-label.md, packages.md, the package README.

## Decisions
- The sweep is a band, not a continuous shimmer: a constant wave reads as "loading" (skeletons).
- The band lights only the glyphs (SrcATop in a layer the size of the text image), so it works on any background.
- Reduce Motion keeps the metal and drops the sweep; hidden or detached labels do not sweep.
- The icon is aligned by its ink, measured from a render, on the middle of an "H": its SVG box has room around the shape, so centring the box left the crown about a point low.
- Icon, metal and spacing apply in Marquee mode; a rolling number keeps its own drawing.
