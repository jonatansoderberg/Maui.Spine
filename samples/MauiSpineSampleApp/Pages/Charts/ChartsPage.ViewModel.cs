using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Charts;

public partial class ChartsPageViewModel : SampleViewModel
{
    /// <summary>The history reaches 90 days back, as a glucose app keeps it.</summary>
    public DateTimeOffset Earliest { get; } = new(DateTime.Today.AddDays(-89));

    /// <summary>A year of daily steps, ending today.</summary>
    public DateTimeOffset YearAgo { get; } = new(DateTime.Today.AddDays(-364));

    public DateTimeOffset EndOfToday { get; } = new(DateTime.Today.AddDays(1));

    [ObservableProperty]
    public partial ChartDrag OneFinger { get; set; } = ChartDrag.Scroll;

    [ObservableProperty]
    public partial TimeSpan LineSpan { get; set; } = TimeSpan.FromDays(1);

    [ObservableProperty]
    public partial TimeSpan? LineSnap { get; set; } = TimeSpan.FromHours(1);

    [ObservableProperty]
    public partial int Delay { get; set; } = 700;

    [ObservableProperty]
    public partial string LineAsked { get; set; } = "The service has not been asked yet.";

    [ObservableProperty]
    public partial string ColumnAsked { get; set; } = "The service has not been asked yet.";

    public IChartHistory Readings => field ??= new ChartHistory(async (from, to, token) =>
    {
        Asked(from, to, text => LineAsked = text, ref _lineCount);
        await Task.Delay(Delay, token);
        return new ChartSlice(Points: [.. FakeReadings(from, to)]);
    });

    public IChartHistory Steps => field ??= new ChartHistory(async (from, to, token) =>
    {
        Asked(from, to, text => ColumnAsked = text, ref _columnCount);
        await Task.Delay(Delay, token);
        var days = new List<ChartColumn>();
        for (var day = from.LocalDateTime.Date; day < to.LocalDateTime.Date && day <= DateTime.Today; day = day.AddDays(1))
        {
            var steps = 2500 + Scatter(day.DayOfYear + day.Year * 400) % 9000;
            days.Add(new ChartColumn(new DateTimeOffset(day), steps, day.ToString("%d"), steps.ToString("N0"),
                ToolTip: $"{day:dddd d MMMM}: {steps:N0} steps"));
        }
        return new ChartSlice(Columns: days);
    });

    private int _lineCount, _columnCount;

    private static void Asked(DateTimeOffset from, DateTimeOffset to, Action<string> show, ref int count)
    {
        var n = Interlocked.Increment(ref count);
        MainThread.BeginInvokeOnMainThread(() => show($"Asked {n}×, last for {from.LocalDateTime:ddd d MMM}{(to - from > TimeSpan.FromDays(1.5) ? $" – {to.LocalDateTime.AddDays(-1):ddd d MMM}" : "")}"));
    }

    /// <summary>A reading every 5 minutes, waving over the day as glucose does, with now and then an hour without readings.</summary>
    private static IEnumerable<ChartPoint> FakeReadings(DateTimeOffset from, DateTimeOffset to)
    {
        var now = DateTimeOffset.Now;
        for (var t = from; t < to && t <= now; t = t.AddMinutes(5))
        {
            var hours = (t - DateTimeOffset.UnixEpoch).TotalHours;
            var day = (int)(hours / 24);
            if (Scatter(day) % 4 == 0 && (hours % 24) is > 13 and < 14.5)
                continue;
            var value = 7.2 + 2.6 * Math.Sin(hours / 24 * 2 * Math.PI + Scatter(day) % 7) + 1.4 * Math.Sin(hours / 3.1) + 0.6 * Math.Sin(hours * 1.7 + day);
            yield return new ChartPoint(t, Math.Round(Math.Clamp(value, 2.4, 16), 1));
        }
    }

    // Stable across runs, unlike HashCode.Combine.
    private static int Scatter(int x)
    {
        var h = (uint)x * 2654435761u;
        h ^= h >> 13;
        return (int)(h % 100_000);
    }

    [RelayCommand]
    private Task ShowLineOptions() => ShowOptionsAsync("Chart scroller",
        Choices("One finger", OneFinger, v => OneFinger = v,
            ("Scrolls", "Sideways scrolls the chart, up and down the page; tap, or hold a moment and drag, to see the values.", ChartDrag.Scroll),
            ("Shows the value", "One finger picks a value out at once; only two fingers scroll.", ChartDrag.Select)),
        Choices("Span", LineSpan, v => LineSpan = v,
            ("6 hours", "", TimeSpan.FromHours(6)),
            ("A day", "", TimeSpan.FromDays(1)),
            ("3 days", "", TimeSpan.FromDays(3))),
        Choices<TimeSpan?>("Comes to rest on", LineSnap, v => LineSnap = v,
            ("Anywhere", "No snapping: a flick stops where it stops.", null),
            ("Round hours", "On the wall clock, also across a change of clock.", TimeSpan.FromHours(1)),
            ("Midnight", "A day fills the view.", TimeSpan.FromDays(1))),
        Choices("The service answers after", Delay, v => Delay = v,
            ("0.2 s", "", 200),
            ("0.7 s", "", 700),
            ("3 s", "Long enough to watch a day load.", 3000)));

    // Choosing sets the page's property at once, so the chart changes behind the sheet.
    private static ChoiceGroup Choices<T>(string name, T current, Action<T> set, params (string Label, string Description, T Value)[] items)
    {
        ChoiceGroup group = null!;
        group = new ChoiceGroup(name, [.. items.Select((item, i) => new Choice(item.Label, item.Description, () =>
        {
            set(item.Value);
            group.Select(i);
        }))]);
        group.Select(Array.FindIndex(items, item => EqualityComparer<T>.Default.Equals(item.Value, current)));
        return group;
    }
}
