namespace Orientera.Tests;

/// <summary>
/// An arena Eventor publishes without a coordinate. The pair sits at zero, which is a real point
/// on the globe — in the Gulf of Guinea — and every distance measured from it is a number the app
/// does not have. "DM, lång, Gästrikland" was showing 6905 km in a district where everything else
/// was 12–41.
/// </summary>
public class ArenaPositionTests
{
    private static readonly GeoPoint Gavle = new(60.6749, 17.1413);

    private static Competition Competition(GeoPoint location) => new()
    {
        Id = new CompetitionId("c1"),
        Name = "DM, lång, Gästrikland",
        Organiser = "Storviks IF",
        District = "Gästrikland",
        Place = "Storvik",
        Location = location,
        Discipline = Discipline.Long,
        Level = CompetitionLevel.Championship,
        FirstStart = new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(2026, 9, 6, 15, 0, 0, TimeSpan.FromHours(2)),
    };

    [Fact]
    public void The_origin_is_not_a_place()
    {
        Assert.False(default(GeoPoint).IsKnown);
        Assert.False(new GeoPoint(0, 0).IsKnown);
        Assert.True(Gavle.IsKnown);
    }

    /// <summary>A single zeroed half is still a source that gave up, not a point on the equator.</summary>
    [Fact]
    public void One_real_half_is_enough_to_count_as_placed()
    {
        Assert.True(new GeoPoint(60.6749, 0).IsKnown);
        Assert.True(new GeoPoint(0, 17.1413).IsKnown);
    }

    [Fact]
    public void An_unplaced_arena_has_no_distance()
    {
        Assert.Null(Competition(default).DistanceFrom(Gavle));
        Assert.False(Competition(default).HasArena);
    }

    [Fact]
    public void A_placed_arena_measures_from_home()
    {
        var competition = Competition(new GeoPoint(60.6, 16.6));

        Assert.True(competition.HasArena);
        Assert.InRange(competition.DistanceFrom(Gavle)!.Value, 25, 35);
    }

    /// <summary>A runner whose own home is unknown cannot be told how far anything is either.</summary>
    [Fact]
    public void An_unplaced_home_has_no_distance_either()
    {
        Assert.Null(Competition(new GeoPoint(60.6, 16.6)).DistanceFrom(default));
    }
}
