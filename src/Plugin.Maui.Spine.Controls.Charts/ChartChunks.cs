namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The pieces <see cref="ChartScroller"/> loads its history in: whole days on a zone's own clock, a chunk being
/// <c>days</c> of them, numbered so the same days are always the same chunk. Free of MAUI so it can be tested.
/// </summary>
internal static class ChartChunks
{
    /// <summary>The number of the local day <paramref name="time"/> falls on.</summary>
    public static int Day(DateTimeOffset time, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).DateTime).DayNumber;

    /// <summary>
    /// When the local day <paramref name="day"/> begins: its midnight, or the first time after it the clock shows where
    /// midnight is skipped; where midnight comes twice, the first.
    /// </summary>
    public static DateTimeOffset StartOf(int day, TimeZoneInfo zone)
    {
        var wall = DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(wall))
            wall = wall.AddMinutes(15);
        var offset = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    /// <summary>The chunk <paramref name="time"/> is in.</summary>
    public static int Of(DateTimeOffset time, TimeZoneInfo zone, int days) => FloorDiv(Day(time, zone), days);

    /// <summary>Where a chunk begins and ends.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Bounds(int chunk, TimeZoneInfo zone, int days) =>
        (StartOf(chunk * days, zone), StartOf((chunk + 1) * days, zone));

    /// <summary>The first and last chunk that overlap <paramref name="from"/> up to (not including) <paramref name="to"/>.</summary>
    public static (int First, int Last) Range(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone, int days) =>
        (Of(from, zone, days), Of(to > from ? to.AddTicks(-1) : from, zone, days));

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
}
