using Orientera.Services.FakeData;
using Orientera.Services.Time;

namespace Orientera.Tests;

/// <summary>
/// The demo's club badges. A fixture that changes between runs is not a fixture.
/// </summary>
public class FakeClubBadgeTests
{
    [Fact]
    public void A_club_keeps_its_badge_between_runs()
    {
        // The value is asserted literally on purpose: a stable hash that quietly starts
        // returning something else would still pass a test that only compared it to itself.
        Assert.Equal("club_badge_2.png", FakeClubBadges.For("Gävle OK"));
        Assert.Equal("club_badge_2.png", FakeClubBadges.For("Gävle OK"));
    }

    [Fact]
    public void Different_clubs_do_not_all_share_one_badge()
    {
        string[] clubs = ["Gävle OK", "Sandvikens OK", "OK Gästrike", "Falu OK", "Hofors OK", "Rehns BK"];

        Assert.True(clubs.Select(FakeClubBadges.For).Distinct().Count() >= 3);
    }

    [Fact]
    public void A_runner_without_a_club_has_no_badge()
    {
        Assert.Null(FakeClubBadges.For(null));
        Assert.Null(FakeClubBadges.For("  "));
    }

    /// <summary>The whole point: demo rows have to look like real ones.</summary>
    [Fact]
    public async Task Demo_results_carry_a_badge()
    {
        var source = new FakeDataSource(new TimeMachineClock(FakeDataset.DefaultNow));

        var results = await source.GetResultsAsync(FakeDataset.HemlingbyloppetId);

        Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.ClubLogo)));
    }
}
