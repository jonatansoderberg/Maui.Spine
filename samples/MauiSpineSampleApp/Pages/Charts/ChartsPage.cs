using System.Globalization;
using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Charts;

[NavigableRegion(Title = "Chart scroller")]
public partial class ChartsPage
{
    public ChartsPage()
    {
        InitializeComponent();
        // What a finger picks out is the page's to say, as real text over the plot.
        Line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LineChart.Selection))
                Picked.Text = Line.Selection is { } s
                    ? $"{s.Point.Value.ToString("0.0", CultureInfo.CurrentCulture)} mmol/L · {s.Point.Time.ToLocalTime():ddd d MMM HH:mm}"
                    : " ";
        };
        LineScroller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartScroller.IsAtLatest))
                LineNow.IsVisible = !LineScroller.IsAtLatest;
        };
        ColumnScroller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChartScroller.IsAtLatest))
                ColumnNow.IsVisible = !ColumnScroller.IsAtLatest;
        };
        LineNow.Clicked += (_, _) => _ = LineScroller.ScrollToLatestAsync();
        ColumnNow.Clicked += (_, _) => _ = ColumnScroller.ScrollToLatestAsync();
    }
}
