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

    [Fact]
    public void What_has_been_run_is_past() =>
        Assert.Equal(EventTimeline.Past, EventTimeline.NameFor(Group(Today.AddDays(-1)), Today));

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
        var series = Group(Today.AddDays(-9), Today.AddDays(-8), Today.AddDays(-2));

        Assert.True(EventTimeline.IsPast(series, Today));
        Assert.Equal(Today.AddDays(-2), EventTimeline.SortDate(series, Today));
    }
}
