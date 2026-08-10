using Orientera.Backend.Eventor;
using Orientera.Services.Grouping;
using Orientera.Services.Relevance;

namespace Orientera.Tests;

/// <summary>
/// The engines against a calendar that came through the adapter rather than out of the seed.
/// M1's DoD asks for exactly this: grouping and relevance measured on data shaped like
/// Eventor's, not on data shaped to suit them.
/// </summary>
public class NormalisedCalendarTests
{
    private readonly IReadOnlyList<Competition> _calendar =
        EventorNormalizer.ForZone("Europe/Stockholm").Competitions(
            Fixture.Eventor("events.xml"),
            OrganisationDirectory.From(Fixture.Eventor("organisations.xml")));

    private readonly RelevanceContext _gavleRunner = new()
    {
        Now = new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.FromHours(2)),
        Home = new GeoPoint(60.6749, 17.1413),
        HomeDistrict = "Gästrikland",
        MyClass = "H21",
    };

    /// <summary>Three club evenings on adjacent days are one row in the calendar, not three.</summary>
    [Fact]
    public void A_recurring_series_collapses_into_one_row()
    {
        var groups = EventGrouper.Group(_calendar);

        var series = groups.Single(g => g.Occurrences.Count > 1);

        Assert.Equal("Veckans bana", series.Title);
        Assert.Equal(3, series.Occurrences.Count);

        // Six competitions, four rows: the two championships, the sprint and the series.
        Assert.Equal(4, groups.Count);
    }

    /// <summary>A championship and a district race must not be merged by a shared organiser.</summary>
    [Fact]
    public void Separate_competitions_stay_separate()
    {
        var groups = EventGrouper.Group(_calendar);

        Assert.Contains(groups, g => g.Title == "DM, Sprint" && g.Occurrences.Count == 1);
        Assert.Contains(groups, g => g.Title == "Natt-SM, långdistans" && g.Occurrences.Count == 1);
    }

    /// <summary>
    /// "Nära events prioriteras, men får inte alltid slå mästerskap": the championship in my
    /// own district leads even though it is three weeks away and the district sprint is next
    /// weekend — and both outrank a club evening one district over.
    /// </summary>
    [Fact]
    public void Relevance_ranks_the_calendar_the_way_the_spec_describes()
    {
        var ranked = _calendar
            .OrderByDescending(c => RelevanceEngine.Score(c, _gavleRunner).Total)
            .Select(c => c.Name)
            .ToList();

        // Both championships are in my own district and lead; the district sprint follows; the
        // club evenings one district over come last. The championship three days past still
        // outranks them — that one is what "senaste resultat" is about.
        Assert.Equal("Natt-SM, långdistans", ranked[0]);
        Assert.Equal("Norrlandsmästerskapen, medel", ranked[1]);
        Assert.Equal("DM, Sprint", ranked[2]);
        Assert.All(ranked.Skip(3), name => Assert.StartsWith("Veckans bana", name));
    }

    [Fact]
    public void An_entry_of_my_own_outranks_everything_else()
    {
        var context = _gavleRunner with
        {
            MyEntries = new HashSet<CompetitionId> { new("38520") },
        };

        var top = _calendar
            .OrderByDescending(c => RelevanceEngine.Score(c, context).Total)
            .First();

        Assert.Equal("Veckans bana, etapp 7", top.Name);
    }
}
