namespace Plugin.Maui.Spine.Controls;

/// <summary>A value at a time, for <see cref="LineChart"/>.</summary>
public readonly record struct ChartPoint(DateTimeOffset Time, double Value);

/// <summary>A low and a high value at a time: one step of a band in <see cref="LineChart.Bands"/>.</summary>
public readonly record struct ChartRange(DateTimeOffset Time, double Low, double High);

/// <summary>What <see cref="LineChart"/> does with its points before drawing, free of MAUI so it can be tested.</summary>
internal static class LineSeries
{
    /// <summary>The middle spacing between neighbouring points; 5 minutes for fewer than two.</summary>
    public static TimeSpan UsualSpacing(IReadOnlyList<DateTimeOffset> sorted)
    {
        if (sorted.Count < 2)
            return TimeSpan.FromMinutes(5);
        var spacings = sorted.Zip(sorted.Skip(1), (a, b) => b - a).Order().ToArray();
        return spacings[spacings.Length / 2];
    }

    /// <summary>
    /// A pause longer than this breaks the line: <paramref name="gapAfter"/> when set, else three times the usual
    /// spacing, and never less than <paramref name="minimumGap"/>.
    /// </summary>
    public static TimeSpan GapThreshold(IReadOnlyList<DateTimeOffset> sorted, TimeSpan? gapAfter, TimeSpan? minimumGap)
    {
        if (gapAfter is { } set)
            return set;
        var gap = UsualSpacing(sorted) * 3;
        return minimumGap is { } floor && floor > gap ? floor : gap;
    }

    /// <summary>
    /// Points closer together than <paramref name="bucketSeconds"/> merged into their mean, keeping the last point as it
    /// is, so a dense series is not drawn as a staircase. A bucket with a value below <paramref name="low"/> keeps its
    /// lowest, one above <paramref name="high"/> its highest, with its time, so merging never hides one. Nothing is
    /// merged across a gap.
    /// </summary>
    public static ChartPoint[] Thin(ChartPoint[] sorted, double bucketSeconds, TimeSpan gap, double? low, double? high)
    {
        if (sorted.Length < 3 || bucketSeconds <= 0)
            return sorted;
        var thinned = new List<ChartPoint>();
        var bucket = new List<ChartPoint>();
        void Flush()
        {
            if (bucket.Count == 0)
                return;
            var below = bucket.Where(p => p.Value < low).ToList();
            var above = bucket.Where(p => p.Value > high).ToList();
            thinned.Add(below.Count > 0 ? below.MinBy(p => p.Value)
                : above.Count > 0 ? above.MaxBy(p => p.Value)
                : new ChartPoint(
                    DateTimeOffset.FromUnixTimeMilliseconds((long)bucket.Average(p => p.Time.ToUnixTimeMilliseconds())),
                    bucket.Average(p => p.Value)));
            bucket.Clear();
        }
        foreach (var point in sorted[..^1])
        {
            if (bucket.Count > 0 && ((point.Time - bucket[0].Time).TotalSeconds >= bucketSeconds || point.Time - bucket[^1].Time > gap))
                Flush();
            bucket.Add(point);
        }
        Flush();
        thinned.Add(sorted[^1]);
        return [.. thinned];
    }

    /// <summary>The items split where the time between two of them is more than <paramref name="gap"/>.</summary>
    public static List<T[]> Split<T>(T[] sorted, Func<T, DateTimeOffset> time, TimeSpan gap)
    {
        var runs = new List<T[]>();
        if (sorted.Length == 0)
            return runs;
        var start = 0;
        for (var i = 1; i < sorted.Length; i++)
        {
            if (time(sorted[i]) - time(sorted[i - 1]) > gap)
            {
                runs.Add(sorted[start..i]);
                start = i;
            }
        }
        runs.Add(sorted[start..]);
        return runs;
    }

    /// <summary>
    /// The value axis over <paramref name="values"/>, on round steps, at most four lines. With <paramref name="round"/>
    /// the ends are on steps (a chart with axes); without, the values get a tenth of their range as room each way.
    /// </summary>
    public static (double Min, double Max, double Step) ValueAxis(IReadOnlyCollection<double> values, double? minimum, double? maximum, bool round)
    {
        var min = minimum ?? (values.Count > 0 ? values.Min() : 0);
        var max = maximum ?? (values.Count > 0 ? values.Max() : 1);
        if (max <= min)
            max = min + 1;
        var step = NiceStep((max - min) / 3);
        if (minimum is null)
            min = round ? Math.Floor(min / step) * step : min - (max - min) * 0.1;
        if (maximum is null)
            max = round ? Math.Ceiling(max / step) * step : max + (max - min) * 0.1;
        return (min, max, step);
    }

    /// <summary>1, 2 or 5 times a power of ten, at least <paramref name="rough"/>.</summary>
    public static double NiceStep(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var fraction = rough / magnitude;
        return magnitude * (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
    }
}
