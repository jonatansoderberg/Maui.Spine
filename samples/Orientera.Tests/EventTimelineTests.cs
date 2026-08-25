using Orientera.Services.Grouping;

namespace Orientera.Tests;

/// <summary>
/// Which heading a competition ends up under. The hard case is the recurring series: it spans
/// several dates and must still occupy exactly one place in the list.
/// </summary>
public class EventTimelineTests
{
    private static readonly DateOnly Today = new(2026, 8, 11);

    private static Competition On(DateOnly date, string name = "Tävling") => new()
    {
        Id = new CompetitionId($"{name}-{date:yyyyMMdd}"),
        Name = name,
        Organiser = "Gävle OK",
        District = "Gästrikland",
        Place = "Hemlingby",
        Location = new GeoPoint(60.6749, 17.1413),
        Discipline = Discipline.Middle,
        Level = CompetitionLevel.District,
        FirstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 0)), TimeSpan.FromHours(2)),
    };

    private static EventGroup Group(params DateOnly[] dates) => new()
    {
        Id = new EventGroupId("g"),
        Title = "Veckans bana",
        Organiser = "Gävle OK",
        Place = "Hemlingby",
        Occurrences = [.. dates.Select(d => On(d))],
    };

    [Theory]
    [InlineData(0, "Denna vecka")]
    [InlineData(6, "Denna vecka")]
    [InlineData(7, "Nästa vecka")]
    [InlineData(13, "Nästa vecka")]
    [InlineData(21, "September")]
    public void Weeks_first_then_months(int daysAhead, string expected) =>
        Assert.Equal(expected, EventTimeline.NameFor(Group(Today.AddDays(daysAhead)), Today));

    [Fact]
    public void A_year_from_now_says_which_year() =>
        Assert.Equal("Augusti 2027", EventTimeline.NameFor(Group(Today.AddDays(365)), Today));

    /// <summary>
    /// Yesterday's race is what a runner is still looking up, so it stays in the list. Anything
    /// older is the summer that was, and belongs behind the chip.
    /// </summary>
    [Fact]
    public void Yesterday_stays_in_the_list_under_its_own_heading()
    {
        var yesterday = Group(Today.AddDays(-1));

        Assert.False(EventTimeline.IsPast(yesterday, Today));
        Assert.Equal(EventTimeline.Recent, EventTimeline.NameFor(yesterday, Today));
    }

    [Fact]
    public void The_day_before_yesterday_is_archived() =>
        Assert.Equal(EventTimeline.Past, EventTimeline.NameFor(Group(Today.AddDays(-2)), Today));

    /// <summary>Today's races are current, not recent — the card already says "idag".</summary>
    [Fact]
    public void Today_is_this_week_not_recent() =>
        Assert.Equal("Denna vecka", EventTimeline.NameFor(Group(Today), Today));

    [Fact]
    public void Today_is_not_past() =>
        Assert.False(EventTimeline.IsPast(Group(Today), Today));

    /// <summary>
    /// A series that started last week and runs into next week belongs where a runner can still
    /// act on it — under the next occurrence ahead, not under the ones already run.
    /// </summary>
    [Fact]
    public void A_series_straddling_today_is_filed_by_its_next_occurrence()
    {
        var series = Group(Today.AddDays(-3), Today.AddDays(-1), Today.AddDays(8), Today.AddDays(9));

        Assert.False(EventTimeline.IsPast(series, Today));
        Assert.Equal(Today.AddDays(8), EventTimeline.SortDate(series, Today));
        Assert.Equal("Nästa vecka", EventTimeline.NameFor(series, Today));
    }

    [Fact]
    public void A_series_entirely_behind_us_is_past()
    {
        var series = Group(Today.AddDays(-9), Today.AddDays(-8), Today.AddDays(-3));

        Assert.True(EventTimeline.IsPast(series, Today));
        Assert.Equal(Today.AddDays(-3), EventTimeline.SortDate(series, Today));
    }

    [Fact]
    public void The_first_row_of_a_section_carries_its_date()
    {
        Assert.True(EventTimeline.DrawsDate(null, Today));
        Assert.True(EventTimeline.DrawsMonth(null, Today));
    }

    [Fact]
    public void A_second_competition_on_the_same_day_leaves_the_column_empty()
    {
        Assert.False(EventTimeline.DrawsDate(Today, Today));
        Assert.False(EventTimeline.DrawsMonth(Today, Today));
    }

    [Fact]
    public void A_new_day_in_the_same_month_draws_the_day_but_not_the_month()
    {
        var above = new DateOnly(2026, 8, 24);

        Assert.True(EventTimeline.DrawsDate(above, new DateOnly(2026, 8, 25)));
        Assert.False(EventTimeline.DrawsMonth(above, new DateOnly(2026, 8, 25)));
    }

    /// <summary>"Mest relevant" is ranked, so the month can go backwards as well as forwards.</summary>
    [Fact]
    public void A_month_that_changes_in_either_direction_is_named_again()
    {
        Assert.True(EventTimeline.DrawsMonth(new DateOnly(2026, 9, 4), new DateOnly(2026, 8, 24)));
        Assert.True(EventTimeline.DrawsMonth(new DateOnly(2026, 8, 24), new DateOnly(2026, 9, 4)));
    }

    /// <summary>
    /// Same day and month, a year apart. Comparing the month alone would leave "24 mån" standing
    /// for a date twelve months from the one above it.
    /// </summary>
    [Fact]
    public void The_same_month_in_another_year_is_named_again()
    {
        var above = new DateOnly(2026, 8, 24);

        Assert.True(EventTimeline.DrawsDate(above, new DateOnly(2027, 8, 24)));
        Assert.True(EventTimeline.DrawsMonth(above, new DateOnly(2027, 8, 24)));
    }
}
