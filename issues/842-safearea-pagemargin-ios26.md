# Issue #842 — SafeArea.PageMargin has no effect on iOS 26 with MAUI 10.0.60+ (HeroCollectionView and every list)

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/842
**Branch:** issue/842-safearea-pagemargin-ios26
**Status:** Completed

## Plan

The owner noticed on the Home app's main page (a `HeroCollectionView`) that the list's scroll bar stood a bit in from
the screen edge: the page margin was on the list itself, because `SafeArea.PageMargin` did nothing there. The owner's
rule: a list's scroll bar is never inset, hero lists included.

Found: on iOS 26, MAUI 10.0.60 and later give every section of a list `ContentInsetsReference = None`
(`LayoutFactory2`, decompiled from 10.0.50 and 10.0.130), which overrides the layout configuration's `LayoutMargins`
Spine set. A side content inset and a fresh layout pass do not move the rows either; a plain `CollectionView` is as
affected as a hero list. Spine and the Showcase are on 10.0.50, so it never showed there.

Three ways were put to the owner: a margin on each row (public API, grids get no outer margin on iOS 26), wrapping
UIKit's section provider (exact, but undocumented internals), or documenting it only. The owner chose a margin per row.

## Changes

- `RowMargins` (iOS, Mac Catalyst, new): for a list with `PageMargin` that is not a grid, the page margin is added to
  each child view's `Margin` as the list makes it (rows, header, footer, empty view), remembered so it is never added
  twice and comes off when `PageMargin` is turned off or the list becomes a grid.
- `SafeArea.PageMargin` calls it; `ApplyPageMargin` leaves such a list's layout margins at zero, so an older MAUI does
  not inset the rows twice. A grid keeps the layout margins.
- Showcase: the HeroCollectionView page uses `SafeArea.PageMargin` instead of a margin in each row template (one of
  them a hard-coded 20).
- Docs: `regions.md` "Lists inside the page margin", `hero-collection-view.md`.

## Decisions

- **A margin on each row rather than on the sections** (the owner, 2026-11-19): only MAUI's own section code can set a
  section's insets, and reaching it means undocumented UIKit internals.
- **A grid keeps the layout margins**, without an outer margin on iOS 26 with a newer MAUI: a margin on every cell
  would double the gap between its columns.

## Verified

2026-11-19, iOS 26.4 simulator (iPhone 17 Pro):
- In the Home app (MAUI 10.0.130) with a local dev package, a plain `CollectionView` and a `HeroCollectionView` with `SafeArea.PageMargin` both have their rows 16 pt in and the scroll bar at the screen edge. Before the fix, both had their rows edge to edge.
- In the Showcase (MAUI 10.0.50), the HeroCollectionView page lines its rows up with the back button, with the scroll bar at the edge and the photo full width. The margin is not doubled.

Android builds. Grids, Android and Mac Catalyst were not run.
