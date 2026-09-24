# Issue #344 — Symbols: SVG icons the framework and the samples still lack

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/344
**Branch:** issue/344-symbols-svg-icons-the-framework-and-the-samples-st
**Status:** Completed

## Plan

Draw the icons on the issue's wish list in the style of `src/Plugin.Maui.Spine.Svg.Icons/Images`, show them for approval, then add the approved ones.

**Matching the set.** The existing files were measured and the new ones copy them:
- `viewBox="0 0 50 50"`, one `<g>` of `<path>` elements only, with the same attribute order and spacing as the existing files (`stroke="#000000" stroke-width="2" stroke-opacity="1" fill-opacity="0"`).
- Stroke 2 for the glyph and 1 for secondary detail (the header rule in `Calendar`, the latitude lines in `Globe`, the sand in `Hourglass`), as `CalendarDay` and `Bell` do.
- Filled dots and modules are `stroke-width="0" fill="#000000" fill-opacity="1"`, as in `More`, `Error` and `Refresh`.
- Points sit on the set's 1.5625 grid (50/32: 4.69, 6.25, 7.81 …) and the content stays within about 4.69–45.31.
- Circles are four cubic quarter arcs starting at the right, and rounded corners are cubics too, the way the existing files do it. No `A`, `circle` or `rect` elements.
- The existing files set no `stroke-linejoin` or `stroke-linecap`, so the new ones don't either. The issue text mentions round joins, but the set uses the defaults.

The icons are made by a small Python script that writes that exact format. The script stays out of the repo; only the SVGs go in.

**Icons drawn** (53 files, including alternatives): Search, Menu, ChevronRight/Left/Up/Down, Check, Sort, Share, ExternalLink, Copy, Info, Warning, Help, Calendar, Image, Grid, List, Star, Heart, Eye, EyeOff, Notification (the existing `Bell` with a filled dot), Timer, Hourglass, Download, Upload, Cloud, Keyboard, Globe, Bluetooth, Battery, QrCode, Map, Compass, Tag, Flag, Chat, Phone, Mail. There are two or three alternatives each for Filter, Grip, Text, Link, Layers/Sheet and Theme/Palette.

**After approval:**
1. Copy the chosen SVGs into `src/Plugin.Maui.Spine.Svg.Icons/Images/` and name each one after its symbol.
2. Add the constants to `SpineIcons.cs` in alphabetical order.
3. Update the icon count and the list of icon kinds in `src/Plugin.Maui.Spine.Svg.Icons/README.md`, `docs/wiki/svg.md`, `docs/wiki/packages.md` and the `spine-controls`/`spine-setup` skills where they mention them.
4. Replace the stand-ins in `samples/MauiSpineSampleApp/Pages/MainPage.ViewModel.cs`: `up` becomes Sheet/Layers, `return` becomes Link or a new result glyph, `wired` becomes Link, `more` becomes Menu for page actions, `water` becomes Theme or keeps glass, and `horizontal` becomes Text. `fish` stays on the SVG sample, where it is the set's showcase.
5. Update the SVG sample's icon list (`SvgIconsPage.ViewModel.cs`) to show a few of the new icons.

## Open Questions


## Changes

- Reviewed the gallery (all proposals with alternatives at 90 px, 28 pt and 24 pt, light and dark).
- Choices: Filter A (funnel); Grip A as `GripVertical` plus a new `GripHorizontal`; Grip B's short lines became `Menu`; Text A; Link B (two halves and a bar); Layers A, B as `Stack` and C as `Sheet`, all three kept; both `Theme` and `Palette` kept.
- Battery comes in six variants instead of one: `Battery0` (a sliver, nearly empty), `Battery25`, `Battery50`, `Battery75`, `Battery100` and `BatteryUnknown` (a question mark in the body).
- Mail: the V now starts in the frame's rounded corners, with no gap at the top.
- Chevrons are centred on x/y 25; `ChevronLeft` is the same size as `ArrowLeft`.
- Added 55 SVGs to `src/Plugin.Maui.Spine.Svg.Icons/Images/` and regenerated `SpineIcons.cs` (ordinal order as before).
- The back button (`NavigationRegionViewModel`) uses `chevronleft.svg`; the framework embeds its own copy in `src/Plugin.Maui.Spine/Resources/Svg/` in place of `arrowleft.svg`.
- Removed the replaced icons: `Done`, `Error` and `ArrowLeft` from the icon package and `SpineIcons`, and `arrowleft.svg` from the framework's resources. The set is 166 − 3 + 55 = 218. `MenuElements` now uses `check.svg` as its example.
- Sample start page: sheets → `sheet`, page binding → `link`, page actions → `menu`, AnimatedLabel → `text`, Calendar → `calendar`, DataGrid → `grid`, Theming → `theme`, Strings → `globe`. Glass sample back button → `chevronleft`. Menus sample: Favourites → `star`, Media types → `image`. The SVG sample lists 16 of the new icons too.
- Added the `/spine-symbol` skill: name or describe a symbol, get lettered alternatives in the set's format, pick one and it is installed.
- Updated the count and description in the package README, `README.md`, `docs/wiki/svg.md`, `docs/wiki/packages.md`, and the `spine-controls` and `spine-setup` skills.

## Decisions

- `Check`, `Warning` and `ChevronLeft` replace `Done`, `Error` and `ArrowLeft`; the old files are removed rather than kept. Jonatan confirmed nothing uses them in production, so the break for NuGet users is accepted.
- The only glyphs Spine uses itself are `chevronleft.svg` (back) and `close.svg` (sheet close), both embedded in `Plugin.Maui.Spine`. The Calendar's month arrows and the DataGrid's group chevron are drawn as vector paths in those controls. They stay that way, because switching them to SVGs would make the controls depend on the Svg packages.
- The Favourites menu item in the Menus sample now uses `star` rather than `check`: the tick belongs to the picker's selection mark, not to the item.
- No `stroke-linejoin`/`stroke-linecap`: none of the existing files set them, and the new ones have to match at 24 pt next to them.
- The drawing helpers became the `/spine-symbol` skill (`.claude/skills/spine-symbol/`): `symbols.py` draws in the set's exact format, renders a contact sheet with Quick Look and a review gallery, and installs a pick (constants and counts included). New symbols go through it instead of a one-off script.
