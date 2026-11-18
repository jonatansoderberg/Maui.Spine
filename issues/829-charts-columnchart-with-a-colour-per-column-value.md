# Issue #829 — Charts: ColumnChart with a colour per column, value labels and a reference line

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/829
**Branch:** issue/829-charts-columnchart-with-a-colour-per-column-value (stacked on #828)
**Status:** In Progress

## Plan

Add to `ColumnChart`, every default drawing as today: `Colors`, `ShowValueLabels`, `ReferenceValue` +
`ReferenceLabel`, `Maximum`. The scale (top and column height) moves to a MAUI-free `ColumnScale` with tests. Part S2
of Glukos's reports plan (`docs/reports.md` §7.1 in Glukos): low events and mean per 3-hour block.

## Open Questions

## Changes

- `ColumnChart`: `Colors` (a colour and its own track per column), `ShowValueLabels` (each value just over its
  column; dimmed with the others when one is picked out), `ReferenceValue`/`ReferenceLabel` (a dashed line over the
  columns, the label in room kept at the trailing side), `Maximum`.
- `ColumnScale` and `ColumnScaleTests`.
- `docs/wiki/charts.md` and the `spine-controls` skill.
- The reference line breaks where a value's text crosses it (seen in the first trial: "8,7" and "8,8" sat on the dashes).
- Tried in Glukos (simulator, packages `0.261118.3-dev.829`) as a temporary pair on the person page: means per
  3-hour block coloured by range with a 10,0 line, and low events per block with zeros as marks, light and dark.

## Decisions

- No new zero mark: with `ShowTrack="False"` a 0 is already a short mark at the bottom in the track's colour.
- The reference line is drawn over the columns, as a target over the values, and the plot narrows by its label's
  width so the label never sits on a column.
