# Issue #830 — Charts: a chart that scrolls along time, with day names that follow and data loaded as you go

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/830
**Branch:** issue/830-charts-a-chart-that-scrolls-along-time
**Status:** In Progress

## Plan

The owner's direction (2026-11-18), which replaces the issue's first idea of a native `ScrollView` with an empty
spacer:

- **A general container for the existing charts**, so any chart with a time axis can be scrolled back through
  history: `LineChart` and `ColumnChart`. Not `RingChart` and `BarChart`, which show one share and have no axis.
- **Loaded asynchronously as the view nears it**, from the app's own source.
- **One finger shows the legend** (the chart's selection: `LineChart.Selection`, `ColumnChart`'s value), as today;
  **two fingers drag the chart** along time. A setting makes one finger scroll instead.

### Shape

- `ChartScroller` (ContentView, content property `Chart`): holds one chart that implements `IScrollableChart`. It
  owns the window (`To`, `Span`; `From` = `To` − `Span`), the limits (`Earliest`, `Latest`; null `Latest` is now),
  snapping (`SnapTo`), the day names over the plot (`Sections`: days, months or none), the source (`Source`, an
  `IChartHistory`) and the gestures (`OneFinger`: `Select` or `Scroll`).
- The chart is drawn for the window in sight on every frame of a scroll: the scroller sets the window
  (`IScrollableChart.ShowWindow`) and the chart redraws. The value axis and the trailing value labels therefore stay
  where they are, and the time ticks move with the data. (A wide canvas moved by translation was rejected: the value
  axis would scroll away, and 90 days do not fit one canvas.)
- `IChartHistory.LoadAsync(from, to, token)` answers a `ChartSlice` (points and bands for a line chart, columns for a
  column chart). The scroller asks for whole local days (`ChunkDays`, 1 by default) in the window and one window
  each way, at most three at a time, keeps three windows each way and lets the rest go (a load still running there is
  cancelled). It hands the chart the loaded slices merged, oldest first (`IScrollableChart.ShowHistory`). A chunk
  that fails is asked again when it is needed after 10 s. While a chunk in view loads, a soft pulse lies over its
  part of the plot, so loading never reads as "no data".
- `InvalidateData()` / `InvalidateData(from, to)` let an app say new data exists (a new reading); following the latest
  (`IsAtLatest`) keeps the window at `Latest` when it moves. `ScrollToAsync(to)` and `ScrollToLatestAsync()` move it.
- **Motion** (`ScrollMotion`, free of MAUI): velocity from the last 100 ms of the drag; a flick is projected as
  UIScrollView decelerates (rate 0.998 a millisecond) and the end snapped to `SnapTo` on the wall clock of
  `TimeZone` (local midnight for a day, round hours across a clock change), then eased out; past `Earliest`/`Latest`
  the drag rubber-bands as iOS does and springs back.
- **Day names** (`SectionLayout`, free of MAUI, after the Home app's `ForecastChartView.PaintDayNames`): a strip over
  the plot, lined up with the chart's plot (`IScrollableChart.PlotMargin`); the section at the leading edge keeps its
  name there until the next one leaves no room, is then shortened (`ShortSectionFormat`), then pushed out, fading; a
  name reaching in at the trailing edge fades in; a name stands 4 pt inside its boundary.
- **Gestures.** A two-finger `PanGestureRecognizer` always scrolls; with `OneFinger="Scroll"` a one-finger one does
  too, and the chart then picks a value out by a tap only. While a scroll runs the chart's selection lets go and
  dragging does not pick (`IScrollableChart.SelectsByDragging`).
- **Charts:** `LineChart` clips its line, fill and bands to the plot when it is in a scroller (outside one it draws as
  before). `ColumnChart` gets a time layout: `Times` (each column's start), `ColumnSpan`, `From`/`To`; without them it
  lays its columns out as before.

### Not in this issue

Keys on a television and a keyboard, the trackpad on the Mac and Windows' wheel, VoiceOver's adjustable action, and
moving the Home app's forecast chart onto the scroller (it is drawn with SkiaSharp in that app, not with Spine's
charts).

## Changes

- `ChartScroller` (new): the window, the limits, snapping, the gestures, the glide and rubber band, loading by day
  chunks with cancelling and retry, the pulse over a day loading, the day/month names, following the latest.
- `IScrollableChart`, `IChartHistory`/`ChartHistory`, `ChartSlice` (with `Merge`), `ChartColumn`, `ChartDrag`,
  `ChartSections` (new, `ChartHistory.cs`).
- `ChartChunks`, `ScrollMotion`, `SectionLayout` (new, free of MAUI) with `ChartScrollerTests` (15 tests): local days
  across both clock changes, week chunks, deceleration, velocity, rubber band, snapping to midnight and to a round
  hour after the clock went back, and the names' placing, shortening, pushing out and fading.
- `LineChart` is an `IScrollableChart`: in a scroller it clips what is drawn along time to the plot, picks only a point
  in view, and marks the last point only when it is in view; dragging picks a point only while
  `SelectsByDragging`.
- `ColumnChart` is an `IScrollableChart` and has a time layout (`Times`, `ColumnSpan`, `From`, `To`); without them it
  draws as before.
- iOS and Mac: a pan on the chart begins only when it goes more sideways than up or down, so the page still scrolls;
  on the Mac the pan also takes the trackpad's scroll.
- Showcase: "Chart scroller" page with a line chart over 90 days of made-up readings (one finger / span / snapping /
  delay as options) and a column a day over a year in week chunks with month names.
- Docs: `docs/wiki/charts.md` "Scrolling back through history", the package README and description, the
  spine-controls skill.

Tried on 2026-11-18 in the iOS simulator (iPhone 17 Pro, iOS 26.4), dark: the line chart opens at now with Tuesday
and Wednesday over it and loads three days; a two-finger flick back a day rests on a round hour and shows "Now ›";
one finger picks a reading out and does not scroll; with "Scrolls" one finger scrolls, and an up-and-down drag that
starts on the chart scrolls the page; a two-finger drag past now springs back. The column chart opens on the last two
weeks with "November 2026" over it, a flick back five weeks rests on whole days with "Sep" shortened and pushed by
"October 2026". Android and the Mac build; neither was run.

## Decisions

- **The chart is drawn for the window on every frame**, not moved as a wide picture: the value axis then stays put
  and 90 days need no canvas of their size. A line chart's per-frame cost is its thinning and smoothing over the
  loaded days (at most seven windows of data).
- **Own pan gestures, not a native `ScrollView`**: the owner wants one finger to show the legend and two fingers to
  scroll, which a native scroll view cannot do; the motion (deceleration rate, rubber band) is copied from UIKit.
- **Whole local days are the unit of loading**, as the Calendar asks for months: a source can cache and answer by
  day, the same day is always the same request, and a 23- or 25-hour day is one request.
- **The value axis is not refitted while scrolling**; a fixed `Minimum`/`Maximum` is recommended in the docs rather
  than an animated refit, which can come later.
- **`BarChart` and `RingChart` are not scrollable**: they show one share and have no time axis.
