using Orientera.Backend.Activities;

namespace Orientera.Tests;

/// <summary>
/// The club activity page, against a real one saved from Eventor.
/// </summary>
/// <remarks>
/// Same fragility as the ranking pages, and the same guard: the values below are read off the
/// saved page, not off the parser.
/// </remarks>
public class ActivityPageParserTests
{
    private static readonly TimeZoneInfo Sweden = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private static IReadOnlyList<ClubActivity> Parse() =>
        ActivityPageParser.Parse(
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "Activities", "activities-115.html")),
            Sweden);

    [Fact]
    public void The_page_yields_its_activities()
    {
        var activities = Parse();

        Assert.Equal(13, activities.Count);
        Assert.All(activities, a => Assert.Matches(@"^\d+$", a.Id));
        Assert.All(activities, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    /// <summary>The page groups by organisation, and a row means nothing without knowing whose.</summary>
    [Fact]
    public void Every_activity_knows_whose_it_is()
    {
        var activities = Parse();

        Assert.Equal(8, activities.Count(a => a.Organisation == "Gävle OK"));
        Assert.Equal(5, activities.Count(a => a.Organisation == "Gästriklands OF"));
    }

    [Fact]
    public void A_relay_carries_its_deadline_and_its_count()
    {
        var relay = Parse().Single(a => a.Name == "DM-Stafett 30/8 Ockelbo");

        Assert.Equal("26684", relay.Id);
        Assert.Equal(6, relay.EntryCount);
        Assert.True(relay.IsOpen);
        Assert.Equal(new DateTime(2026, 8, 23, 20, 0, 0), relay.EntryDeadline?.DateTime);
    }

    /// <summary>
    /// The deadline is shown as "om 11 dagar" and only the title attribute has the date. Reading
    /// the text would have meant doing calendar arithmetic against Eventor's reading clock.
    /// </summary>
    [Fact]
    public void A_deadline_shown_in_words_is_still_read_as_a_date()
    {
        var closed = Parse().Single(a => a.Name == "SM-stafett 23/8 Örebro");

        Assert.Equal(new DateTime(2026, 8, 9, 20, 0, 0), closed.EntryDeadline?.DateTime);
        Assert.False(closed.IsOpen);
    }

    /// <summary>
    /// A relay sign-up is not an appointment: most rows have no start time, and the district's
    /// activities do. Inventing one from the deadline would have been a lie either way.
    /// </summary>
    [Fact]
    public void A_start_time_is_read_when_there_is_one()
    {
        var activities = Parse();

        Assert.Null(activities.Single(a => a.Name == "10-Mila Tranås 2-3/5").StartsAt);
        Assert.Equal(
            new DateTime(2026, 8, 22, 8, 0, 0),
            activities.Single(a => a.Name.StartsWith("Träningsdag inför USM")).StartsAt?.DateTime);
    }

    [Fact]
    public void Something_that_is_not_an_activity_page_yields_nothing() =>
        Assert.Empty(ActivityPageParser.Parse("<html><body><p>Sidan finns inte</p></body></html>", Sweden));
}
