using System.Xml.Linq;
using Orientera.Services.FakeData;
using Orientera.Services.Local;
using Orientera.Services.Time;
using Orientera.Backend.Ranking;

namespace Orientera.Tests;

/// <summary>
/// The start list is where the field comes from, against a real one saved from Eventor.
/// </summary>
/// <remarks>
/// It is the one source here that is a documented API rather than a scraped page, so the risk is
/// not that the markup moves but that the wrong element is read: a start list nests person, club
/// and class three different ways, and taking the club's name off the wrong one silently mixes
/// runners up.
/// </remarks>
public class StartFieldTests
{
    private static XElement Starts() =>
        XElement.Load(Fixture.PathFor("Eventor", "starts-53683.xml"));

    [Fact]
    public void One_class_is_read_out_of_the_whole_list()
    {
        var field = StartFieldSource.Field(Starts(), "H45");

        Assert.Equal(17, field.Count);
        Assert.All(field, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.All(field, r => Assert.NotNull(r.ClubId));
    }

    [Fact]
    public void A_runner_carries_what_the_start_list_states()
    {
        var first = StartFieldSource.Field(Starts(), "H45")[0];

        Assert.Equal("Henrik Wännström", first.Name);
        Assert.Equal("Linköpings OK", first.Club);
        Assert.Equal(new PersonId("4122"), first.Person);
        Assert.Equal("242", first.ClubId);
    }

    /// <summary>
    /// The club id travels with the runner all the way to the phone, which is where the club page
    /// is now read (#123). Without it the app would have to resolve a club from its name.
    /// </summary>
    [Fact]
    public void Every_runner_knows_which_club_page_to_look_them_up_on() =>
        Assert.All(StartFieldSource.Field(Starts(), "D21"), r => Assert.Matches(@"^\d+$", r.ClubId));

    [Fact]
    public void A_class_that_is_not_in_the_list_is_empty_rather_than_wrong() =>
        Assert.Empty(StartFieldSource.Field(Starts(), "H21"));
}

/// <summary>The demo's field, which is what the section shows when there is no backend.</summary>
public class FakeStartFieldTests
{
    private static FakeDataSource Source() =>
        new(new TimeMachineClock(FakeDataset.DefaultNow),
            new LocalIdentityStore(Path.Combine(Path.GetTempPath(), $"identity-{Guid.NewGuid():N}.json")));

    [Fact]
    public async Task The_demo_field_is_ranked_lowest_points_first()
    {
        var field = await Source().GetStartFieldAsync(FakeDataset.NmLongId, "D21");

        Assert.NotEmpty(field);

        var ranked = field.Where(r => r.Points is not null).Select(r => r.Points!.Value).ToList();
        Assert.Equal(ranked.Order(), ranked);

        // Whoever the list does not carry comes last, without an invented place.
        Assert.All(field.SkipWhile(r => r.Points is not null), r => Assert.Null(r.Points));
    }
}
