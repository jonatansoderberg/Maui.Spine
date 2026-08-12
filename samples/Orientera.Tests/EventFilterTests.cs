using Orientera.Features.Events;

namespace Orientera.Tests;

/// <summary>
/// The filter decides what a whole tab shows, and every rule in it can hide something the user
/// wanted. These pin the ones where "no answer" and "everything" are easy to confuse.
/// </summary>
public class EventFilterTests
{
    private static readonly DateOnly Today = new(2026, 8, 12);

    private static Competition Competition(string name, string district, DateOnly date) => new()
    {
        Id = new CompetitionId($"c-{name}"),
        Name = name,
        Organiser = "Gävle OK",
        District = district,
        Place = "Hemlingby",
        Location = new GeoPoint(60.6, 17.1),
        Discipline = Discipline.Middle,
        Level = CompetitionLevel.National,
        FirstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(2)),
    };

    /// <summary>A filter nobody has set must not hide anything.</summary>
    [Fact]
    public void An_empty_filter_is_not_active()
    {
        Assert.False(EventFilter.Default.IsActive);
        Assert.Equal(0, EventFilter.Default.ActiveCount);
        Assert.Null(EventFilter.Default.Window(Today));
        Assert.True(EventFilter.Default.Matches(Competition("Hemlingbyloppet", "Gästrikland", Today)));
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

    /// <summary>Each of the new three counts once, so the "Filter (n)" badge stays honest.</summary>
    [Fact]
    public void Every_set_choice_is_counted_once()
    {
        var filter = new EventFilter
        {
            Districts = new HashSet<string> { "Gästrikland", "Hälsingland" },
            Query = "DM",
            Period = EventPeriod.NextMonth,
        };

        Assert.True(filter.IsActive);
        Assert.Equal(3, filter.ActiveCount);
    }
}
