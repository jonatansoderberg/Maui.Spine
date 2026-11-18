# Issue #831 — Charts: BarChart with stacked segments, upright or across

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/831
**Branch:** issue/831-charts-barchart-with-stacked-segments-upright-or-a (stacked on #829)
**Status:** In Progress

## Plan

Add to `BarChart`, a single `Value` drawing as today: `Segments` (`ChartSegment(Value, Color)` stacked, filling the
bar in proportion), `Orientation` (`Vertical` stands it up; a single value then fills from the bottom) and
`CornerRadius`. The lengths move to a MAUI-free `BarSegments` with tests. Part S3 of Glukos's reports plan
(`docs/reports.md` §7.1 in Glukos): time in ranges on the report page.

## Open Questions

## Changes

- `ChartSegment`; `BarChart.Segments`, `Orientation`, `CornerRadius`.
- `BarSegments.Lengths` and `BarSegmentsTests`: proportions, a sliver of at least 3 points for a part above 0 taken
  from the longer parts, parts of 0 not drawn.
- `docs/wiki/charts.md` and the `spine-controls` skill.
- Tried in Glukos (simulator, packages `0.261118.4-dev.831`) as a temporary trial on the person page: the five
  ranges as a 44 × 200 bar standing up (radius 10) and as a 10-point bar across, shares of 0.4 % and 2 % as slivers,
  and a plain `Value` bar drawn as before.

## Decisions

- The parts are plain rectangles clipped by the bar's rounded shape, so only the bar's own ends are round; each
  overlaps the next by half a point so no seam of the track shows.
- Segments from the top when vertical: a report lists the ranges from very high down, as the bar reads.
- A vertical bar that still has the horizontal default size (6 tall, no width) becomes 6 wide with no height.
