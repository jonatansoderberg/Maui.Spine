# Plugin.Maui.Spine.Controls.Charts

Charts for dashboards, drawn with Microsoft.Maui.Graphics:

- `LineChart`: a time series as a line over a band, with wall-clock times (summer/winter time marked), gaps, bands
  between two series, thinning and smoothing that never hide a value outside the band, and a live last point.
- `RingChart`: a share (0–1) as a ring, clockwise from 12 o'clock; null draws only the track.
- `BarChart`: a share as a bar.
- `ColumnChart`: values as columns with a label under each, such as cost per day.
- `ChartScroller`: a line or column chart moved along time with two fingers (or one), the history loaded a day at a
  time from the app's `IChartHistory` as the window nears it, with the day or month names following over the plot.

The track is the value's colour mixed towards the background (`ChartColors.Track`), and follows the theme. See
[Charts](../../docs/wiki/charts.md).
