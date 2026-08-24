using Orientera.Features.Events;

namespace Orientera.Tests;

/// <summary>
/// The filter decides what a whole tab shows, and every rule in it can hide something the user
/// wanted. These pin the ones where "no answer" and "everything" are easy to confuse.
/// </summary>
public class EventFilterTests
{
    private static readonly DateOnly Today = new(2026, 8, 12);

    private static readonly DateTimeOffset Now =
        new(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(2));

    /// <summary>Home is Gävle, so the Hemlingby competitions below are a few kilometres away.</summary>
    private static readonly Person Me = new()
    {
        Id = new PersonId("me"),
        Name = "Jonatan Söderberg",
        Club = "Gävle OK",
        District = "Gästrikland",
        DefaultClass = "H40",
        Home = new GeoPoint(60.66, 17.14),
    };

    private static Competition Competition(
        string name,
        string district,
        DateOnly date,
        CompetitionLevel level = CompetitionLevel.National,
        Discipline discipline = Discipline.Middle,
        GeoPoint? location = null) => new()
    {
        Id = new CompetitionId($"c-{name}"),
        Name = name,
        Organiser = "Gävle OK",
        District = district,
        Place = "Hemlingby",
        Location = location ?? new GeoPoint(60.6, 17.1),
        Discipline = discipline,
        Level = level,
        FirstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(2)),
    };

    /// <summary>A filter nobody has set must not hide anything.</summary>
    [Fact]
    public void An_empty_filter_hides_nothing()
    {
        var competition = Competition("Hemlingbyloppet", "Gästrikland", Today);

        Assert.Empty(EventFilter.Default.Facets);
        Assert.Null(EventFilter.Default.Window(Today));
        Assert.True(EventFilter.Default.Matches(competition));
        Assert.True(EventFilter.Default.Includes(competition, Me, Now));
    }

    [Fact]
    public void The_search_reads_what_a_person_would_type()
    {
        var competition = Competition("Hemlingbyloppet", "Gästrikland", Today);

        Assert.True(new EventFilter { Query = "hemlingby" }.Matches(competition));
        Assert.True(new EventFilter { Query = "gävle ok" }.Matches(competition));
        Assert.True(new EventFilter { Query = "GÄSTRIKLAND" }.Matches(competition));
        Assert.False(new EventFilter { Query = "sprint" }.Matches(competition));
    }

    /// <summary>"Denna månad" ends with the month, not a month from now.</summary>
    [Fact]
    public void A_period_is_a_window_with_two_ends()
    {
        var thisMonth = new EventFilter { Period = EventPeriod.ThisMonth }.Window(Today);
        Assert.Equal(Today, thisMonth?.From);
        Assert.Equal(new DateOnly(2026, 8, 31), thisMonth?.To);

        var next = new EventFilter { Period = EventPeriod.NextMonth }.Window(Today);
        Assert.Equal(new DateOnly(2026, 9, 1), next?.From);
        Assert.Equal(new DateOnly(2026, 9, 30), next?.To);

        var rest = new EventFilter { Period = EventPeriod.RestOfYear }.Window(Today);
        Assert.Equal(new DateOnly(2026, 12, 31), rest?.To);
    }

    /// <summary>
    /// Each district is its own chip and the period is one more. The query is not among them —
    /// the search box on the page already shows it, with its own clear button.
    /// </summary>
    [Fact]
    public void Every_set_choice_is_its_own_chip_except_the_query()
    {
        var filter = new EventFilter
        {
            Districts = new HashSet<string> { "Gästrikland", "Hälsingland" },
            Query = "DM",
            Period = EventPeriod.NextMonth,
        };

        Assert.Equal(["Gästrikland", "Hälsingland", "Nästa månad"], filter.Facets.Select(f => f.Label));
    }

    /// <summary>
    /// The one thing the old ladder could not say. "Nivå och uppåt" always dragged the
    /// championships along with the local races; a set does not.
    /// </summary>
    [Fact]
    public void Levels_are_a_set_and_not_a_ladder()
    {
        var filter = new EventFilter { Levels = new HashSet<CompetitionLevel> { CompetitionLevel.Local } };

        Assert.True(filter.Includes(
            Competition("Onsdagsträffen", "Gästrikland", Today, CompetitionLevel.Local), Me, Now));

        Assert.False(filter.Includes(
            Competition("DM", "Gästrikland", Today, CompetitionLevel.Championship), Me, Now));
    }

    [Fact]
    public void Several_levels_can_be_kept_at_once()
    {
        var filter = new EventFilter
        {
            Levels = new HashSet<CompetitionLevel> { CompetitionLevel.Championship, CompetitionLevel.District },
        };

        Assert.True(filter.Includes(Competition("DM", "X", Today, CompetitionLevel.Championship), Me, Now));
        Assert.True(filter.Includes(Competition("Distriktet", "X", Today, CompetitionLevel.District), Me, Now));
        Assert.False(filter.Includes(Competition("SM-veckan", "X", Today, CompetitionLevel.National), Me, Now));
    }

    /// <summary>Training hides by default, and shows for either of the two ways of asking.</summary>
    [Fact]
    public void Training_hides_until_it_is_asked_for()
    {
        var training = Competition("Tisdagsträning", "Gästrikland", Today, CompetitionLevel.Training);

        Assert.False(EventFilter.Default.Includes(training, Me, Now));
        Assert.True(new EventFilter { ShowTraining = true }.Includes(training, Me, Now));

        // Naming the level is asking for it, switch or no switch.
        Assert.True(new EventFilter
        {
            Levels = new HashSet<CompetitionLevel> { CompetitionLevel.Training },
        }.Includes(training, Me, Now));
    }

    [Fact]
    public void Disciplines_are_a_set_too()
    {
        var filter = new EventFilter
        {
            Disciplines = new HashSet<Discipline> { Discipline.Relay, Discipline.Sprint },
        };

        Assert.True(filter.Includes(Competition("Stafetten", "X", Today, discipline: Discipline.Relay), Me, Now));
        Assert.True(filter.Includes(Competition("Sprinten", "X", Today, discipline: Discipline.Sprint), Me, Now));
        Assert.False(filter.Includes(Competition("Medeln", "X", Today, discipline: Discipline.Middle), Me, Now));
    }

    [Fact]
    public void A_radius_is_measured_from_home()
    {
        var near = Competition("Hemlingby", "Gästrikland", Today);
        var far = Competition("Kiruna", "Norrbotten", Today, location: new GeoPoint(67.85, 20.23));

        var filter = new EventFilter { MaxDistanceKm = 50 };

        Assert.True(filter.Includes(near, Me, Now));
        Assert.False(filter.Includes(far, Me, Now));
    }

    /// <summary>
    /// A competition that has not published its classes yet must not be hidden by a class filter.
    /// Not knowing is not the same as not having it.
    /// </summary>
    [Fact]
    public void An_unpublished_class_list_does_not_hide_the_competition()
    {
        var filter = new EventFilter { OnlyMyClass = true };

        Assert.True(filter.Includes(Competition("Ingen klasslista än", "X", Today), Me, Now));
    }

    [Fact]
    public void Every_district_is_its_own_removable_chip()
    {
        var filter = new EventFilter
        {
            Districts = new HashSet<string> { "Gästrikland", "Hälsingland" },
        };

        var facets = filter.Facets;

        Assert.Equal(["Gästrikland", "Hälsingland"], facets.Select(f => f.Label));
        Assert.Equal(["Hälsingland"], facets[0].Without.Districts);
        Assert.Equal(["Gästrikland"], facets[1].Without.Districts);
    }

    /// <summary>Removing one chip takes back one choice and leaves the others standing.</summary>
    [Fact]
    public void Removing_a_facet_leaves_the_rest_of_the_filter_alone()
    {
        var filter = new EventFilter
        {
            Districts = new HashSet<string> { "Gästrikland" },
            Period = EventPeriod.NextMonth,
            Disciplines = new HashSet<Discipline> { Discipline.Relay },
            OnlyRegisterable = true,
        };

        var period = Assert.Single(filter.Facets, f => f.Label == "Nästa månad");

        Assert.Equal(EventPeriod.Any, period.Without.Period);
        Assert.Equal(["Gästrikland"], period.Without.Districts);
        Assert.Equal([Discipline.Relay], period.Without.Disciplines);
        Assert.True(period.Without.OnlyRegisterable);
    }

}
