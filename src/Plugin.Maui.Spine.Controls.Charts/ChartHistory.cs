namespace Plugin.Maui.Spine.Controls;

/// <summary>A column at a time, for a <see cref="ColumnChart"/> in a <see cref="ChartScroller"/>: its value and what is said about it.</summary>
public sealed record ChartColumn(DateTimeOffset Time, double Value, string? Label = null, string? ValueLabel = null, Color? Color = null, string? ToolTip = null);

/// <summary>
/// What a <see cref="ChartScroller"/>'s source answers for a stretch of time: points and bands for a
/// <see cref="LineChart"/>, columns for a <see cref="ColumnChart"/>. Nothing in a stretch is an empty slice, not an error.
/// </summary>
public sealed record ChartSlice(IReadOnlyList<ChartPoint>? Points = null, IReadOnlyList<ChartBand>? Bands = null, IReadOnlyList<ChartColumn>? Columns = null)
{
    public static ChartSlice Empty { get; } = new();

    /// <summary>Slices one after the other as one, oldest first: the points and columns in order, each band's ranges joined by its place in the list.</summary>
    public static ChartSlice Merge(IEnumerable<ChartSlice> slices)
    {
        var points = new List<ChartPoint>();
        var columns = new List<ChartColumn>();
        var bands = new List<(List<ChartRange> Ranges, Color? Color)>();
        foreach (var slice in slices)
        {
            if (slice.Points is { } p)
                points.AddRange(p);
            if (slice.Columns is { } c)
                columns.AddRange(c);
            if (slice.Bands is { } b)
            {
                for (var i = 0; i < b.Count; i++)
                {
                    if (i == bands.Count)
                        bands.Add(([], b[i].Color));
                    bands[i].Ranges.AddRange(b[i].Ranges);
                }
            }
        }
        return new ChartSlice(points, [.. bands.Select(b => new ChartBand(b.Ranges, b.Color))], columns);
    }
}

/// <summary>Where a <see cref="ChartScroller"/> gets its history from: the app's own store or service.</summary>
public interface IChartHistory
{
    /// <summary>
    /// What there is from <paramref name="from"/> up to (not including) <paramref name="to"/>: whole local days, asked
    /// for as the window nears them. Throw when it cannot be read; the scroller asks again later.
    /// </summary>
    Task<ChartSlice> LoadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>An <see cref="IChartHistory"/> from a function, for a source that needs no class of its own.</summary>
public sealed class ChartHistory(Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<ChartSlice>> load) : IChartHistory
{
    public Task<ChartSlice> LoadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => load(from, to, cancellationToken);
}

/// <summary>
/// A chart a <see cref="ChartScroller"/> can move along time. <see cref="LineChart"/> and <see cref="ColumnChart"/> are;
/// a chart of one share, such as <see cref="RingChart"/>, has no time to move along.
/// </summary>
public interface IScrollableChart
{
    /// <summary>Draws the time from <paramref name="from"/> to <paramref name="to"/> across the plot: called on every frame of a scroll.</summary>
    void ShowWindow(DateTimeOffset from, DateTimeOffset to);

    /// <summary>The history loaded around the window, oldest first; it may reach well past the window either way.</summary>
    void ShowHistory(ChartSlice history);

    /// <summary>Whether dragging a finger over the chart picks a value out. Off while it scrolls, and when one finger scrolls it.</summary>
    bool SelectsByDragging { get; set; }

    /// <summary>Lets go of a value picked out.</summary>
    void ClearSelection();

    /// <summary>The chart's space outside its plot at the leading and trailing sides, so the scroller lines up with the plot.</summary>
    Thickness PlotMargin { get; }
}

/// <summary>What one finger dragging over a chart in a <see cref="ChartScroller"/> does; two fingers always scroll.</summary>
public enum ChartDrag
{
    /// <summary>Picks a value out, the chart's legend; two fingers scroll.</summary>
    Select,

    /// <summary>Scrolls; a tap picks a value out.</summary>
    Scroll,
}

/// <summary>The names over a <see cref="ChartScroller"/>'s plot.</summary>
public enum ChartSections
{
    None,

    /// <summary>A name a local day, at its midnight.</summary>
    Days,

    /// <summary>A name a month, at its first day.</summary>
    Months,
}
