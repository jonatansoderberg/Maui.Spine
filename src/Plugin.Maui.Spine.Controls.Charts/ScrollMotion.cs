namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// How <see cref="ChartScroller"/> moves: the speed of a drag, how far a flick carries, the rubber band past an end, and
/// the round time a scroll comes to rest on. Free of MAUI so it can be tested.
/// </summary>
internal sealed class ScrollMotion
{
    /// <summary>How much of its speed a flick keeps each millisecond, as a UIScrollView's normal deceleration.</summary>
    public const double Deceleration = 0.998;

    /// <summary>The drag's last moments that its speed is measured over.</summary>
    const double Window = 100;

    readonly List<(double Time, double X)> _samples = [];

    public void Reset() => _samples.Clear();

    /// <summary>Where the drag is, in points, at a time in milliseconds.</summary>
    public void Add(double time, double x)
    {
        _samples.Add((time, x));
        while (_samples.Count > 2 && time - _samples[0].Time > Window)
            _samples.RemoveAt(0);
    }

    /// <summary>
    /// Points a millisecond over the drag's last 100 ms; 0 when the finger rested before it let go, so a drag that stops
    /// does not fling.
    /// </summary>
    public double Velocity(double now)
    {
        if (_samples.Count < 2 || now - _samples[^1].Time > 50)
            return 0;
        var (first, last) = (_samples[0], _samples[^1]);
        return last.Time > first.Time ? (last.X - first.X) / (last.Time - first.Time) : 0;
    }

    /// <summary>How far, in points, a flick at <paramref name="velocity"/> points a millisecond carries before it stops.</summary>
    public static double Carry(double velocity) => velocity * Deceleration / (1 - Deceleration);

    /// <summary>
    /// How far a drag of <paramref name="overshoot"/> points past an end moves the content, as iOS's rubber band: less
    /// and less, never more than <paramref name="dimension"/>.
    /// </summary>
    public static double RubberBand(double overshoot, double dimension) =>
        dimension <= 0 ? 0 : Math.Sign(overshoot) * (1 - 1 / (Math.Abs(overshoot) * 0.55 / dimension + 1)) * dimension;

    /// <summary>How long the glide to rest takes for a distance in points: longer for further, within a quarter and four fifths of a second.</summary>
    public static double Duration(double distance) => Math.Clamp(180 + Math.Sqrt(Math.Abs(distance)) * 14, 250, 800);

    /// <summary>The round time at or before <paramref name="time"/>, as <see cref="Snap"/> finds them.</summary>
    public static DateTimeOffset SnapDown(DateTimeOffset time, TimeSpan step, TimeZoneInfo zone)
    {
        var snapped = Snap(time, step, zone);
        return snapped <= time ? snapped : Snap(time - step, step, zone);
    }

    /// <summary>The round time at or after <paramref name="time"/>, as <see cref="Snap"/> finds them.</summary>
    public static DateTimeOffset SnapUp(DateTimeOffset time, TimeSpan step, TimeZoneInfo zone)
    {
        var snapped = Snap(time, step, zone);
        return snapped >= time ? snapped : Snap(time + step, step, zone);
    }

    /// <summary>
    /// The round time nearest <paramref name="time"/> on <paramref name="zone"/>'s clock: a whole number of days from
    /// local midnight for a step of a day or more, else every <paramref name="step"/> from local midnight, so an hour
    /// is a round hour on both sides of a clock change.
    /// </summary>
    public static DateTimeOffset Snap(DateTimeOffset time, TimeSpan step, TimeZoneInfo zone)
    {
        if (step <= TimeSpan.Zero)
            return time;
        if (step >= TimeSpan.FromDays(1))
        {
            var days = Math.Max(1, (int)Math.Round(step.TotalDays));
            var day = ChartChunks.Day(time, zone);
            var below = day - ((day % days) + days) % days;
            var (start, next) = (ChartChunks.StartOf(below, zone), ChartChunks.StartOf(below + days, zone));
            return time - start <= next - time ? start : next;
        }
        var nearest = ChartChunks.StartOf(ChartChunks.Day(time, zone), zone);
        foreach (var (at, _) in LocalClock.Ticks(time - step - step, time + step + step, step, zone))
            if (Math.Abs((at - time).Ticks) < Math.Abs((nearest - time).Ticks))
                nearest = at;
        return nearest;
    }
}
