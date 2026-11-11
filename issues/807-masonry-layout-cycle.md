# Issue #807 — MasonryLayout: layout cycle on Windows at 150 % while the window is resized

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/807
**Branch:** fix/masonry-arrange-rounding
**Status:** Completed

## Plan

Find what WinUI's layout cycle goes round (`DebugSettings.LayoutCycleTracingLevel = High`, the trace read from the debug
output) and stop `MasonryLayout` from measuring its items in the arrange pass for a width that only differs by rounding.

## Changes

- `MasonryLayout.ArrangeChildren`: arranged at another width with as many columns, it spreads the measured items over it
  (`Spread`: each keeps its column, place and height; the column takes the new width) instead of measuring them again.
  Another number of columns still measures again.
- `ColumnsFor` and `ColumnWidth` shared by measure and arrange; the column each item was measured into is kept.

## Decisions

- **No threshold on the width difference.** A first fix measured again only from one point's difference; the trace then
  showed a two-pixel difference (1288 measured, 1289.33 arranged) and the cycle elsewhere. Keeping the measurement for the
  same number of columns has no threshold to fall between.
- **A different number of columns measures again**, as before: that width is far from the measured one, and doesn't flip.

## Verified

- Windows at 150 %, AIDash Connect with this DLL: 2604 window sizes, widths 450–2400 px both ways at two heights, no
  crash and nothing in its crash log (before: a crash at 1214–1256 px and near 1970 px). One, two and three columns lay
  out as before.
- Not verified: the Mac and iOS (the same code; there AppKit/UIKit arrange at the measured width).
