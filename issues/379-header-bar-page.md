# Issue #379 — Header bar: backgrounds that look different, a simpler API and one sample page

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/379
**Branch:** issue/379-header-bar-page
**Status:** In Progress
**Related:** #330, #366, #374, #377; build warnings in #380

## Plan

Feedback from an iPhone 16 Pro (iOS 26.5): "No difference between Solid and Clear, nor between ScrollEdge and ScrollEdgeHard. What is Auto?"

### Findings

- **Solid = Clear under `Normal`.** The presenter paints a background only when the bar floats over the content (Overlay, a large title or a scroll edge value). Under `Normal` the bar has its own row, nothing is ever under it, and the value is ignored. Solid also starts transparent and only fades in once content scrolls under the bar, so under Overlay at rest it looks like Clear too.
- **ScrollEdge = ScrollEdgeSoft on the iOS 26.4 simulator, but looks like Hard on the phone (iOS 26.5).** `ScrollEdge` sets `UIScrollEdgeEffectStyle.Automatic` and leaves the choice to UIKit, which may differ between OS versions and contexts. Side by side on the simulator, Automatic and Soft are pixel-identical and Hard differs.
- **Auto** resolves to `Clear` under Overlay, to `ScrollEdge` on iOS 26 for a region/tab page whose list fills it from the top, and to `Solid` otherwise.
- **None of this API is released.** `HeaderBarMode`, `HeaderBarBackground` and `StatusBarStyle` are not in v0.1.11, the latest tag. The sample uses project references. Values can be renamed and removed without `[Obsolete]`.

### Design

`HeaderBar` (layout), `LargeTitle` and `HeaderBarBackground` stay three independent settings, but each gets one meaning that holds in every combination:

- **Layout** decides where the page's content starts at rest.
  - `Normal`: below the bar. When content scrolls under the bar, Spine insets the scroll view so its first row still starts below the bar.
  - `Overlay`: at the top of the screen, behind the status bar and the bar. The page keeps clear what it wants with `SafeAreaInsets` / `SafeArea.ScrollInset="Top"`; Spine does not add a top inset (it did for Overlay + Solid, which pushed a photo in the scroll content below the bar).
- **Background** decides what is behind the title and the actions when content is under the bar. Five values, each visibly different:
  - `Auto`: what the platform's own bar does (see the table in `regions.md`).
  - `Solid`: the page's colour; content under the bar is hidden.
  - `Transparent` (was `Clear`): nothing; content shows through the bar. Under `Normal` content now scrolls under the bar, so it differs from `Solid`.
  - `ScrollEdge`: the iOS 26 scroll edge effect, **soft** style (was the system's automatic style).
  - `ScrollEdgeHard`: the hard style.
  - `ScrollEdgeSoft` is removed (it is what `ScrollEdge` is now).
- `EffectiveHeaderBarBackground` becomes public (read-only), so a page can show what `Auto` resolved to.
- `LargeTitle` and `StatusBarStyle` can change while the page is shown, like `HeaderBarMode` and `HeaderBarBackground`.

### Sample

One "Header bar" page replaces Collapsing header, Scroll edge and Overlay header: a photo at the top of the list, then the explanation, then coloured cards. A footer with chips for layout, large title, background, foreground and status bar style changes the page live; the page shows what each choice does, what `Auto` resolved to, and the code for the chosen combination.

### Docs

`regions.md`: a table "value → iOS 26 / iOS 18 / Android / Windows → when to pick it", screenshots. The `/spine-page` skill. Comments on #366 and #374.

## Open Questions

None.

## Changes

## Decisions
