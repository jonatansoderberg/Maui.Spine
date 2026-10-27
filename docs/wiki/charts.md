# Charts

`Plugin.Maui.Spine.Controls.Charts` has the small charts a dashboard needs, drawn with Microsoft.Maui.Graphics:

| Control | Shows |
|---|---|
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
