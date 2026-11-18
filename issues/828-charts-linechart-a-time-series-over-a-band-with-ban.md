# Issue #828 — Charts: LineChart, a time series over a band, with bands between two series

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/828
**Branch:** issue/828-charts-linechart-a-time-series-over-a-band-with-ban
**Status:** In Progress

## Plan

Move Glukos's `LineChart` (`src/Glukos.App/Controls/LineChart.cs`, `Smoothing.cs`, `Glukos.Shared/LocalClock.cs`)
into `Plugin.Maui.Spine.Controls.Charts` as it is, on `ChartView`, with what was glucose-specific made a property, and
add `Bands`. Part S1 of Glukos's reports plan (`docs/reports.md` §7.1 in Glukos).

- `LineChart.cs`: the control; `LineSeries.cs`: `ChartPoint`, `ChartRange` and the MAUI-free series work (gap
  threshold, thinning, splitting, value axis); `LocalClock.cs` and `ChartSmoothing.cs` public and MAUI-free.
- Glukos's fixed 7-minute least gap and 15-minute long gap become `MinimumGap` and `LongGap`; `TimeZoneInfo.Local`
  becomes `TimeZone`; the clock-change words come from Spine's strings (`Spine.Charts.ClockBack` / `ClockForward`).
- `Bands`: `ChartBand(Ranges, Color)` of `ChartRange(Time, Low, High)`, filled between the upper and lower series under
  the line, broken at gaps, in the value axis.
- Tests in `Plugin.Maui.Spine.Controls.Tests` (the MAUI-free files linked in); docs in `docs/wiki/charts.md`.
- Done when Glukos draws the same with Spine's chart, compared frame by frame.

## Open Questions

## Changes

- `LineChart`, `ChartSelection`, `ChartBand` (`LineChart.cs`); `ChartPoint`, `ChartRange`, `LineSeries` (`LineSeries.cs`);
  `LocalClock`; `ChartSmoothing`; the strings in `Resources/Strings`.
- `LineChartTests`: the clock (Glukos's tests, plus another zone's midnight), gap threshold, splitting, thinning that
  keeps extremes and never merges across a gap, the value axis, smoothing and the curve.
- `docs/wiki/charts.md` LineChart section, the package README and description, the `spine-controls` skill.
- Checked in Glukos (iPhone 17 Pro simulator, 2026-11-18) with the packages packed as `0.261118.1-dev.828`: its own
  `LineChart`, `Smoothing` and `LocalClock` deleted, pages on `charts:LineChart` with `MinimumGap="00:07:00"
  LongGap="00:15:00"`. The person page (12 h, 24 h with a long gap at midnight, light and dark, a point picked out)
  and the start page's cards draw as before; the 24 h chart differs from the old one in 1.3 % of its pixels, the minute
  of demo data that moved between the two screenshots. Bands drawn on the person page as a trial: under the line, broken at the gap, in the value axis, in
  both themes.

## Decisions

- Seen while checking, not changed: when the system theme changes just before the app starts, a chart can draw in
  the other theme until the next change. Glukos's own chart reads the theme through `ChartView` the same way, so it
  likely shares this; not reproduced with it. A change while the app runs redraws correctly.

- The drawing is Glukos's, line for line, so its pages look the same; only names and defaults changed.
- `LongGap` defaults to twice the line's break and `MinimumGap` to none: Glukos's 15 and 7 minutes are glucose's
  5-minute history, so Glukos sets them.
- The small graph's axis keeps Glukos's room (a tenth below, then a tenth of the new range above), not an even tenth,
  so Glukos's cards keep their shape.
- Bands are rounded with the curve but not eased: they are percentiles already, and easing them apart from the
  median could cross them.
