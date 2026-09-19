# Issue #281 — SvgImageSource: a transparent tint makes the SVG invisible, and the tint defaults disagree with their docs

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/281
**Branch:** issue/281-svg-transparent-tint
**Status:** Completed

## Plan

Root causes:
- `SvgImageSource.LightTintColorProperty` / `DarkTintColorProperty` (src/Plugin.Maui.Spine.Svg/SvgImageSource.cs) default to `Colors.Black` / `Colors.White`; their XML docs, `docs/wiki/svg.md`'s table and the package's `.github/copilot-instructions.md` table say Transparent; `SvgImageSourceBehavior`'s own properties default to Transparent.
- `SvgBitmapLoader.RenderSvgToPng` always draws through `SKColorFilter.CreateBlendMode(tint, SrcIn)`; a colour with alpha 0 then erases every pixel. `SvgIcon.RenderPng` skips the filter only when `tintColor != Colors.Transparent` is false, a reference comparison that any other transparent `Color` instance fails.

Which default is right: the attached Black/White. Apps rely on them — the sample's `ContextItem.xaml` sets only `SvgImageSource.Svg` and gets a black/white icon; every other use in the samples, Orientera and Almanacka sets both tints explicitly. Nothing can rely on the behavior's Transparent default, because a transparent tint has never rendered anything.

1. `SvgImageSourceBehavior`: default `LightTintColor`/`DarkTintColor` to Black/White like the attached properties.
2. Fix every doc that says Transparent is the default (XML docs, docs/wiki/svg.md, copilot-instructions.md), and say that a transparent tint keeps the SVG's own colours.
3. A MAUI-free internal `SvgRasterizer` (SkiaSharp + Svg.Skia only) that renders an SVG stream to PNG and applies the tint only when its alpha is above 0. `SvgBitmapLoader` renders through it; `SvgIcon` uses its tint rule instead of the reference comparison.
4. Tests: a new `tests/Plugin.Maui.Spine.Svg.Tests` (net10.0) compiling `SvgRasterizer.cs` by link, since the Svg package targets only MAUI platforms: a two-colour SVG with a transparent tint keeps both colours; with a white tint every drawn pixel is white; any colour with alpha 0 counts as no tint. Added to Spine.slnx and ci.yml.

## Changes

- `SvgRasterizer` (src/Plugin.Maui.Spine.Svg/SvgRasterizer.cs, internal, no MAUI types): renders an SVG stream to PNG, scaled to fit and centred within padding; `TintPaint` returns no paint for a tint with alpha 0.
- `SvgBitmapLoader.RenderSvgToPng` renders through it, so `SvgImageSource` with a transparent tint shows the SVG in its own colours instead of nothing.
- `SvgIcon.RenderPng` uses `TintPaint` instead of `tintColor != Colors.Transparent`.
- `SvgImageSourceBehavior.LightTintColor`/`DarkTintColor` default to Black/White, like the attached properties.
- XML docs of both, `SvgBitmapLoader`'s `tint` parameter, docs/wiki/svg.md (the attached-properties table, a note under "Image control", a new "Full-colour SVG" section) and the package's `.github/copilot-instructions.md` table now say Black/White, and that Transparent keeps the SVG's own colours.
- New `tests/Plugin.Maui.Spine.Svg.Tests` (net10.0, `SvgRasterizer.cs` compiled by link): a red-and-blue SVG keeps both colours with `SKColors.Transparent` and with transparent black; a white tint turns what is drawn white and leaves the rest clear. Three of the four fail against the old always-tint rule. Added to Spine.slnx, Spine.Packages.slnf and ci.yml.

## Decisions

- Black/White is the right default: `SvgImageSource.Svg` alone is how the sample's `ContextItem` gets its icons, and an app that sets only `Svg` has always had them. The behavior's Transparent default never worked, since a transparent tint erased the picture, so aligning it changes nothing that rendered.
- "No tint" is decided by alpha, not by equality with `Colors.Transparent`: MAUI's `Color` has no `==` operator, and a transparent colour can be black (`#00000000`) or white (`Colors.Transparent`).
- The rendering core moved to a MAUI-free class and is linked into the test project, rather than moving it to Common: it needs SkiaSharp and Svg.Skia, which Common should not carry for the server.
