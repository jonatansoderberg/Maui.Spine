# Charts

`Plugin.Maui.Spine.Controls.Charts` has the charts a dashboard needs, drawn with Microsoft.Maui.Graphics:

| Control | Shows |
|---|---|
| `LineChart` | A time series (`Points`) as a line over a band, such as glucose over its target range, with times on the wall clock, gaps, `Bands` between two series under the line, and the last point live. 230 points tall by default; a small graph without axes for a card |
| `RingChart` | A share (`Value`, 0 to 1) as a ring, clockwise from 12 o'clock with round ends. `null` draws only the track, for unknown, which is not 0. `Thickness` is the ring's width |
| `BarChart` | A share as a bar, the track the whole width; or `Segments` stacked in their colours, such as the time in each range. Across or standing up (`Orientation`). 6 points tall by default |
| `ColumnChart` | `Values` as columns with `Labels` under them, such as the cost per day of the last week. Each column is its share of the highest value (or `Maximum`); the last label is bold (`HighlightLast`). A colour per column, every value above its column and a reference line for reports. 72 points tall by default |

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
- **For a report:** `Colors` gives each column its own colour (a mean in the colour of the range it falls in; `Color`
  for a column without one, and each track follows its column), `ShowValueLabels="True"` shows every `ValueLabels`
  text just above its column instead of only the one picked out, `ReferenceValue` draws a dashed line across the
  columns at a value with `ReferenceLabel` at its trailing end (the scale reaches it), and `Maximum` fixes the top of
  the scale so charts side by side compare. With `ShowTrack="False"` a 0 is a short mark at the bottom, so a count of
  none still shows its place.

```xml
<charts:ColumnChart Values="{Binding BlockMeans}" Labels="{Binding Blocks}" ValueLabels="{Binding BlockMeanTexts}"
                    Colors="{Binding BlockColors}" ShowValueLabels="True" ShowTrack="False" HighlightLast="False"
                    ReferenceValue="10" ReferenceLabel="10,0" HeightRequest="140" />
```

- **A stacked bar:** `Segments` is a list of `ChartSegment(Value, Color)`, stacked from the leading edge (from the
  top when `Orientation="Vertical"`) and filling the bar in proportion to their total; `Value` is then not drawn. A part
  above 0 keeps a sliver of at least 3 points, taken from the longer parts, so a 0.4 % share still shows; a part of 0
  is not drawn. Only the bar's own ends are round. `CornerRadius` makes a wide bar's ends less round (half the
  thickness by default). A vertical bar is 6 points wide by default; give it a height. A single `Value` on a vertical
  bar fills from the bottom.

```xml
<charts:BarChart Segments="{Binding Ranges}" Orientation="Vertical" WidthRequest="40" HeightRequest="240" CornerRadius="10" />
```

```csharp
Ranges = [
    new ChartSegment(1, Color.FromArgb("#E8890C")),   // very high
    new ChartSegment(18, Color.FromArgb("#F2B312")),  // high
    new ChartSegment(81, Color.FromArgb("#2BB894")),  // in range
    new ChartSegment(0, Color.FromArgb("#F2643D")),   // low
    new ChartSegment(0, Color.FromArgb("#C8321E")),   // very low
];
```

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
round ends, a faint fade under it where no band is drawn (`Fill`), solid value lines with the values on the trailing side, and a dashed line with the
time just after it at each round time.

- **Time axis.** `From` and `To` fix the window (the first and last point when null). Times are round times on the
  wall clock of `TimeZone` (the device's when null), so they stay right across a change between summer and winter
  time, and the change itself is marked with a line and "Summer time +1 h" / "Winter time −1 h"
  (`ClockForwardLabel`, `ClockBackLabel`, or Spine's strings `Spine.Charts.ClockForward` / `ClockBack`). `TimeFormat`
  (`HH:mm`) and `ValueFormat` (`0.#`) format the labels.
- **The band.** `BandLow` and `BandHigh` draw a band (`BandColor`, the chart's colour faint when null) and colour the
  line in up to three colours: `AboveColor` above it, `Color` inside, `BelowColor` below. With the band drawn there
  is no fade under the line: the band is the backdrop. A small graph with `ShowBand="False"` keeps its fade, in the
  line's colours. `BandLabel` names the band at its top edge. `ShowBand="False"` keeps the colours but not the band, for a
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

## Scrolling back through history

`ChartScroller` holds a `LineChart` or a `ColumnChart` and moves it along time, so a chart can show any stretch of a
long history while only the days near the window are loaded. `RingChart` and `BarChart` show one share and have no
time to move along.

```xml
<charts:ChartScroller Source="{Binding History}" Earliest="{Binding NinetyDaysAgo}"
                      Span="1.00:00" SnapTo="01:00">
    <charts:LineChart Minimum="2" Maximum="16" BandLow="3.9" BandHigh="10" GapLabel="No readings" />
</charts:ChartScroller>
```

```csharp
// The app's own store or service, asked for whole local days as the window nears them.
public IChartHistory History { get; } = new ChartHistory(async (from, to, ct) =>
    new ChartSlice(Points: await readings.GetAsync(from, to, ct)));
```

- **The window.** `Span` of time is across the plot (a day by default), ending at `To`. `From` is read-only.
  `Earliest` and `Latest` are the ends of the history (`Latest` is now when null); past them a drag rubber-bands and
  springs back, and nothing the source gave past them is drawn. `ScrollToAsync(to)` glides anywhere, and setting `To`
  jumps.
- **Now.** `NowAt` says where now stands across the window when the chart opens and when it goes back to now: 1 (the
  default) at the trailing edge for a history, 0 at the leading edge for a forecast, between for both in one chart.
  With `SnapTo` the window rests on round times with now in view. `To` is null while the window is at now, and it
  then moves on with now. Scrolled away, a small "Now" pill (Spine's `Spine.Charts.Now`) stands in the row of names
  on the side where now lies, "Now ›" back in a history and "‹ Now" ahead in a forecast; a tap glides back. For a
  button of the page's own, `ShowNowButton="False"`, `IsAtNow`, `IsNowLater` and `ScrollToNowAsync()`. With
  `NowAt` under 1 a line marks now wherever it is in view. A forecast is usually one request: give it a `ChunkDays`
  as long as the forecast.

```xml
<!-- A forecast: yesterday behind, ten days ahead, now a quarter in -->
<charts:ChartScroller Source="{Binding Forecast}" Earliest="{Binding Yesterday}" Latest="{Binding TenDaysAhead}"
                      NowAt="0.25" Span="1.00:00" SnapTo="01:00">
    <charts:LineChart BandLow="0" BandHigh="0" ShowBand="False" AboveColor="#E8505E" BelowColor="#0C7ED9" HighlightLast="False" />
</charts:ChartScroller>
```

- **Gestures.** As in Stocks and Health: one finger sideways scrolls the chart, up or down scrolls the page (the
  page and iOS 26's back swipe wait for the chart to decide). A tap shows the value there for a few seconds; a press
  held 0.3 s picks the value out with a tick, and it then follows the finger until it lifts (a scrub), without
  scrolling anything. Two fingers scroll too. A finger on a gliding chart stops it. `OneFinger="Select"` instead
  picks a value out at once as a chart alone does, and only two fingers scroll. With a mouse a drag scrolls and
  pointing picks a value out. A flick glides as a scroll view decelerates and comes to rest on `SnapTo`, a round time
  at the start of the window on the clock of `TimeZone` (an hour on a round hour across a change of clock, a day on
  local midnight), with a light tick when it rests on another day; an end of the history is always a resting place.
- **Loading.** `Source` (an `IChartHistory`, or `ChartHistory` around a function) is asked for whole local days,
  `ChunkDays` at a time (1 by default; 7 for a column a day over months): those in the window first, newest first,
  then one window each way, at most three at a time; during a glide, those where it will rest. Days more than three windows away are let go and a load still
  running there is cancelled. A day in view that has nothing yet, asked for or waiting its turn, has a soft pulse over it (`IsLoading`), so loading
  never reads as "no data"; a day that could not be read (the source threw) is asked again when needed after 10 s.
  `InvalidateData()` or `InvalidateData(from, to)` asks again for days the app knows have changed, such as today after
  a new reading; the chart keeps what it had until the answer comes.
- **What a source answers.** `ChartSlice(Points, Bands, Columns)`: `ChartPoint`s and `ChartBand`s for a line chart,
  `ChartColumn(Time, Value, Label, ValueLabel, Color, ToolTip)`s for a column chart. The scroller merges the loaded days
  oldest first and hands them to the chart (`IScrollableChart.ShowHistory`); the chart's own `Points`, `Bands`,
  `Values`, `Labels` and so on are then the scroller's to set.
- **Names over the plot.** `Sections="Days"` (the default) puts the day's name over the plot at each local midnight
  and `Months` the month's at each first; `None` leaves the row out. The section at the leading edge keeps its name
  there until the next one leaves no room; it is then shortened (`ShortSectionFormat`), then pushed out, fading, and a
  name coming in at the trailing edge fades in. `SectionFormat` and `ShortSectionFormat` are date formats in the
  current culture (`dddd`/`ddd` for days, `MMMM yyyy`/`MMM` for months by default); the first letter is a capital.
  The names line up with the chart's plot, 4 points inside the line where their section begins.
- **A still scale.** The value axis stays where it is while the time moves. Give the chart a fixed `Minimum` and
  `Maximum` (a `ColumnChart` a `Maximum`), or the axis is fitted to everything loaded and moves when more arrives.
- **A column chart along time.** In a scroller a `ColumnChart` places each column by its time: `Times` (when each
  column begins), `From`/`To` and `ColumnSpan` (the shortest step between two times when null). Columns run past the
  edges and are cut off there. Set `SnapTo` to the column's span, so a scroll rests with whole columns in view.

```xml
<charts:ChartScroller Source="{Binding Steps}" Span="14.00:00" SnapTo="1.00:00" ChunkDays="7" Sections="Months">
    <charts:ColumnChart Maximum="12000" ColumnSpan="1.00:00" ReferenceValue="8000" ReferenceLabel="8k" HeightRequest="140" />
</charts:ChartScroller>
```

- **Another chart.** Any view that implements `IScrollableChart` can be scrolled: `ShowWindow(from, to)` on every frame
  of a scroll, `ShowHistory(slice)` when the loaded days change, `SelectsByDragging` (off while scrolling),
  `ClearSelection()`, and `PlotMargin`, its space outside the plot at either side, for the names to line up with.
- **Not yet:** keys on a television or a keyboard, VoiceOver's and TalkBack's adjustable action, and Windows' mouse
  wheel. On the Mac the trackpad's swipe and the mouse drag scroll the chart.
