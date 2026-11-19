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
        Forecast.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LineChart.Selection))
                ForecastPicked.Text = Forecast.Selection is { } s
                    ? $"{s.Point.Value.ToString("0.0", CultureInfo.CurrentCulture)}° · {s.Point.Time.ToLocalTime():ddd d MMM HH:mm}"
                    : " ";
        };
    }
}
