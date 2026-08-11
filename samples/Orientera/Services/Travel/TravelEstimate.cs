using Orientera.Domain;

namespace Orientera.Services.Travel;

/// <summary>
/// How long it takes to get to an arena, roughly. One rule, used by both the competition page
/// and the "time to leave" notification — two different answers to the same question would be
/// worse than one rough answer.
/// </summary>
/// <remarks>
/// A straight-line distance at an average road speed. The real answer needs the PM's parking
/// and arena walk (M3) and a routing service; until then this is honest about being an
/// estimate and never presented as more.
/// </remarks>
public static class TravelEstimate
{
    /// <summary>What a runner wants to be at the arena before their start.</summary>
    public static readonly TimeSpan DefaultMargin = TimeSpan.FromMinutes(45);

    public static double DistanceKm(GeoPoint home, GeoPoint arena) => home.DistanceKmTo(arena);

    public static TimeSpan Duration(GeoPoint home, GeoPoint arena)
    {
        double km = DistanceKm(home, arena);

        return TimeSpan.FromMinutes(Math.Round(km / SpeedKmh(km) * 60));
    }

    private const double TownSpeedKmh = 25.0;
    private const double RoadSpeedKmh = 80.0;

    /// <summary>Beyond this the extra kilometres are motorway and the average stops climbing.</summary>
    private const double RoadSpeedFromKm = 60.0;

    /// <summary>
    /// Average speed for a trip of this length. One constant cannot serve both errands: at
    /// highway speed a three-kilometre drive across town takes three minutes, which no one
    /// believes, and at town speed a two-hour drive north becomes half a day. Short trips are
    /// town streets, long ones are mostly motorway, and the share shifts gradually between them.
    /// </summary>
    /// <remarks>
    /// Gradually, not in steps. Stepped speeds make the estimate fall as the distance grows —
    /// a six-kilometre trip arriving before a four-kilometre one — which is the kind of wrong
    /// a reader notices immediately. A straight ramp keeps the answer rising the whole way.
    /// </remarks>
    private static double SpeedKmh(double km) =>
        TownSpeedKmh + ((RoadSpeedKmh - TownSpeedKmh) * Math.Min(km, RoadSpeedFromKm) / RoadSpeedFromKm);

    /// <summary>When to leave home to be at the arena in time for <paramref name="startTime"/>.</summary>
    public static DateTimeOffset LeaveAt(GeoPoint home, GeoPoint arena, DateTimeOffset startTime) =>
        startTime - DefaultMargin - Duration(home, arena);
}
