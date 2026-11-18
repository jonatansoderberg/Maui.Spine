namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Times on a zone's own clock for a chart's axis, through a change between summer and winter time: round times that
/// read the clock on the wall on both sides of it, and where the clock was set back or forward.
/// </summary>
public static class LocalClock
{
    /// <summary>
    /// Every <paramref name="step"/> from local midnight, after <paramref name="from"/> and up to <paramref name="to"/>, each
    /// with the wall-clock time it reads. An hour the clock skips in spring has no time; one it repeats in autumn has both.
    /// </summary>
    public static IEnumerable<(DateTimeOffset At, DateTime Wall)> Ticks(DateTimeOffset from, DateTimeOffset to, TimeSpan step, TimeZoneInfo zone)
    {
        var seen = new HashSet<DateTimeOffset>();
        for (var wall = TimeZoneInfo.ConvertTime(from, zone).DateTime.Date; ; wall += step)
        {
            if (zone.IsInvalidTime(wall))
                continue;
            TimeSpan[] offsets = zone.IsAmbiguousTime(wall) ? [.. zone.GetAmbiguousTimeOffsets(wall).OrderByDescending(o => o)] : [zone.GetUtcOffset(wall)];
            var past = false;
            foreach (var offset in offsets)
            {
                var at = new DateTimeOffset(wall, offset);
                if (at > to)
                    past = true;
                else if (at > from && seen.Add(at))
                    yield return (at, wall);
            }
            if (past)
                yield break;
        }
    }

    /// <summary>Where between <paramref name="from"/> and <paramref name="to"/> the clock changed, and by how much.</summary>
    public static IEnumerable<(DateTimeOffset At, TimeSpan Change)> Changes(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var step = TimeSpan.FromMinutes(15);
        var before = zone.GetUtcOffset(from);
        for (var at = from + step; at <= to; at += step)
        {
            var offset = zone.GetUtcOffset(at);
            if (offset == before)
                continue;
            // To the minute: the change falls on a quarter hour everywhere it is made.
            var moment = at - step;
            while (zone.GetUtcOffset(moment) == before)
                moment += TimeSpan.FromMinutes(1);
            yield return (moment, offset - before);
            before = offset;
        }
    }
}
