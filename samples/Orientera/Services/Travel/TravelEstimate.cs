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
    private const double AverageSpeedKmh = 70.0;

    /// <summary>What a runner wants to be at the arena before their start.</summary>
    public static readonly TimeSpan DefaultMargin = TimeSpan.FromMinutes(45);

    public static double DistanceKm(GeoPoint home, GeoPoint arena) => home.DistanceKmTo(arena);

    public static TimeSpan Duration(GeoPoint home, GeoPoint arena) =>
        TimeSpan.FromMinutes(Math.Round(DistanceKm(home, arena) / AverageSpeedKmh * 60));

    /// <summary>When to leave home to be at the arena in time for <paramref name="startTime"/>.</summary>
    public static DateTimeOffset LeaveAt(GeoPoint home, GeoPoint arena, DateTimeOffset startTime) =>
        startTime - DefaultMargin - Duration(home, arena);
}
