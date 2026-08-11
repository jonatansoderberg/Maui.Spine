using Orientera.Services.Travel;

namespace Orientera.Tests;

/// <summary>
/// The travel estimate is modelled, and the model has to stay believable at both ends. One
/// average speed cannot serve a drive across town and a drive to another county.
/// </summary>
public class TravelEstimateTests
{
    private static readonly GeoPoint Home = new(60.6749, 17.1413);

    private static GeoPoint North(double km) => new(Home.Latitude + (km / 111.0), Home.Longitude);

    [Fact]
    public void A_trip_across_town_is_not_driven_at_highway_speed()
    {
        var minutes = TravelEstimate.Duration(Home, North(3.4)).TotalMinutes;

        // The old constant 70 km/h said three minutes for this. Nobody crosses a town that fast.
        Assert.InRange(minutes, 7, 10);
    }

    [Fact]
    public void A_trip_to_the_next_county_is_not_driven_at_town_speed()
    {
        var minutes = TravelEstimate.Duration(Home, North(160)).TotalMinutes;

        Assert.InRange(minutes, 110, 130);
    }

    /// <summary>Longer is never quicker, however the tiers are drawn.</summary>
    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 6)]
    [InlineData(6, 19)]
    [InlineData(19, 21)]
    [InlineData(21, 59)]
    [InlineData(59, 61)]
    [InlineData(61, 200)]
    public void Further_away_never_takes_less_time(double nearer, double further)
    {
        Assert.True(
            TravelEstimate.Duration(Home, North(further)) >= TravelEstimate.Duration(Home, North(nearer)),
            $"{further} km kom fram före {nearer} km.");
    }
}
