# Charts

`Plugin.Maui.Spine.Controls.Charts` has the charts a dashboard needs, drawn with Microsoft.Maui.Graphics:

| Control | Shows |
|---|---|
| `LineChart` | A time series (`Points`) as a line over a band, such as glucose over its target range, with times on the wall clock, gaps, `Bands` between two series under the line, and the last point live. 230 points tall by default; a small graph without axes for a card |
| `RingChart` | A share (`Value`, 0 to 1) as a ring, clockwise from 12 o'clock with round ends. `null` draws only the track, for unknown, which is not 0. `Thickness` is the ring's width |
| `BarChart` | A share as a bar, the track the whole width. 6 points tall by default |
| `ColumnChart` | `Values` as columns with `Labels` under them, such as the cost per day of the last week. Each column is its share of the highest value; the last label is bold (`HighlightLast`). 72 points tall by default |

```xml
<Grid WidthRequest="84" HeightRequest="84">
    <charts:RingChart Value="{Binding Used}" Color="{Binding Accent}" Thickness="9" />
    <Label Text="{Binding Percent}" HorizontalOptions="Center" VerticalOptions="Center" />
</Grid>

<charts:BarChart Value="{Binding Share}" Color="{Binding Accent}" />

<charts:ColumnChart Values="{Binding CostPerDay}" Labels="{Binding Weekdays}" Color="{Binding Accent}" FontFamily="Inter" />
```

`xmlns:charts="clr-namespace:Plugin.Maui.Spine.Controls;assembly=Plugin.Maui.Spine.Controls.Charts"`. Nothing is registered.

- The track is the value's colour mixed towards the background (`ChartColors.Track`), so a series reads the same on a
  light and a dark card; `TrackColor` overrides it. The charts redraw when the theme changes.
- **Without the track:** `ShowTrack="False"` on a `ColumnChart` drops the full-height track behind each column, and a value of 0 becomes a short column at the bottom in the track's colour. Use it when most values can be 0, so one busy day doesn't read as the only lit column among empty ones.
- **Pointing at a column:** with `ValueLabels` (one text per value, such as "$0.32"), pointing at a column picks it out: the others dim, its value shows above it in a row the chart keeps for that, its label turns bold, and the system tooltip says the label and the value. `ToolTips` gives a longer text per column. On touch a tap picks a column out and a second tap lets it go. `SelectedIndex` is the column picked out, or -1. Give the chart about 16 points more height for the value row.
- **Rings and bars:** set `ToolTipProperties.Text` for the detailed value, on the `Grid` that holds the ring and its label, so the label in the middle doesn't take the pointer.

```xml
<charts:ColumnChart Values="{Binding CostPerDay}" Labels="{Binding Weekdays}" ValueLabels="{Binding CostTexts}"
                    ToolTips="{Binding DayDetails}" Color="{Binding Accent}" HeightRequest="88" />
```

- A widget can't host these views; `W.Ring` and `W.Progress` are their counterparts there (see [Widgets](widgets.md)).

## LineChart

```xml
<charts:LineChart Points="{Binding Points}" From="{Binding From}" To="{Binding To}"
                  BandLow="3.9" BandHigh="10" BandLabel="Target 3.9–10.0"
                  Color="#2BB894" BelowColor="#F2643D" AboveColor="#F2B312"
                  IsLive="{Binding IsFresh}" IsCurrent="{Binding IsFresh}" GapLabel="No readings" />
```

`Points` is a list of `ChartPoint(Time, Value)` in any order. Drawn the way iOS draws its charts: a smooth line with
round ends, a faint fade under it, solid value lines with the values on the trailing side, and a dashed line with the
time just after it at each round time.

- **Time axis.** `From` and `To` fix the window (the first and last point when null). Times are round times on the
  wall clock of `TimeZone` (the device's when null), so they stay right across a change between summer and winter
  time, and the change itself is marked with a line and "Summer time +1 h" / "Winter time −1 h"
  (`ClockForwardLabel`, `ClockBackLabel`, or Spine's strings `Spine.Charts.ClockForward` / `ClockBack`). `TimeFormat`
  (`HH:mm`) and `ValueFormat` (`0.#`) format the labels.
- **The band.** `BandLow` and `BandHigh` draw a band (`BandColor`, the chart's colour faint when null) and colour the
  line in up to three colours: `AboveColor` above it, `Color` inside, `BelowColor` below, and the fade under it
  follows. `BandLabel` names the band at its top edge. `ShowBand="False"` keeps the colours but not the band, for a
  small graph where colour over the line would read as data.
- **Gaps.** A pause longer than `GapAfter` (three times the usual spacing when null, never less than `MinimumGap`)
  breaks the line. A short gap is crossed by a dotted line; one longer than `LongGap` (twice the break when null)
  breaks the band too and says `GapLabel` when it is wide enough. Nothing is drawn across a gap that isn't there.
- **A dense series** is thinned before drawing to about one point per 3 points of width, and with `Smooth` (on by
  default) eased a few points wide and drawn as a Catmull-Rom curve. A value outside the band, and the last point,
  are always drawn as measured: thinning keeps the most extreme value of a merged stretch, and smoothing leaves it.
- **The last point** is marked (`HighlightLast`). `IsLive` adds a ring that grows from it and fades every couple of
  seconds (still under reduced motion). With `IsCurrent="False"` it is drawn hollow, and a dashed line runs on from it
  to `To`, so an old value never looks current.
- **Pointing at the chart** (or touching it) picks out the nearest point: a guide line from the top and a dot. The chart
  draws no text for it; `Selection` (`ChartSelection`: the point, its x and its colour) and `SelectedIndex` let the page
  show the value as a real label over the top of the plot (`PlotTop`). On touch it lets go after three seconds.
- **Bands between two series.** `Bands` is a list of `ChartBand(Ranges, Color)`, each a list of
  `ChartRange(Time, Low, High)`, drawn under the line in order (the first at the back) and broken where their times
  pause. Their values are fitted into the value axis. Daily patterns (an ambulatory glucose profile) are two bands and
  a median line over one day:

```xml
<charts:LineChart Points="{Binding Median}" Bands="{Binding Percentiles}" From="{Binding Midnight}" To="{Binding NextMidnight}"
                  TimeFormat="HH" Fill="False" HighlightLast="False" BandLow="3.9" BandHigh="10" Color="#2BB894" AboveColor="#F2B312" />
```

```csharp
Percentiles = [
    new ChartBand([.. rows.Select(r => new ChartRange(r.Time, r.P5, r.P95))], Color.FromArgb("#2BB894").WithAlpha(0.14f)),
    new ChartBand([.. rows.Select(r => new ChartRange(r.Time, r.P25, r.P75))], Color.FromArgb("#2BB894").WithAlpha(0.28f)),
];
```

- **A small graph.** `ShowAxes="False"` drops the grid and labels; the fill then runs a little past the ends of the
  line and fades out there, and the axis fits the line so the graph shows its shape.
- `LocalClock` (round wall-clock ticks and clock changes in a zone) and `ChartSmoothing` (the easing and the curve)
  are public, for a picture of the same series drawn elsewhere, such as a widget.
