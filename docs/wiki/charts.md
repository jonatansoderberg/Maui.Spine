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
- A widget can't host these views; `W.Ring` and `W.Progress` are their counterparts there (see [Widgets](widgets.md)).
