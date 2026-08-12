using Orientera.Backend.Ranking;
using Orientera.Domain.Ranking;

namespace Orientera.Tests;

/// <summary>
/// The runner's own Sverigelistan page, against a real page fetched through the proxy.
/// </summary>
/// <remarks>
/// The values are read off the real page rather than off the parser. This is the only thing that
/// notices when Eventor changes its markup, and the failure it prevents is the worst kind: points
/// that look plausible and are wrong.
/// </remarks>
public class RunnerRankingParserTests
{
    private static readonly DateOnly ReadOn = new(2026, 8, 11);

    private static RankingSnapshot Snapshot() =>
        RunnerRankingParser.Parse("121330", File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Ranking", "runner-121330.html")), ReadOn)!;

    [Fact]
    public void The_overall_standing_is_read()
    {
        var snapshot = Snapshot();

        Assert.Equal(new PersonId("121330"), snapshot.Person);
        Assert.Equal(1914, snapshot.NationalPlace);
        Assert.Equal(62.98, snapshot.Points, 2);
    }

    /// <summary>The thing the club page cannot give: a figure per discipline.</summary>
    [Fact]
    public void Every_discipline_list_is_read()
    {
        var points = Snapshot().DisciplinePoints;

        // Two decimals as published — places are separated by hundredths, so rounding these to
        // whole points threw away the only thing that tells two runners apart.
        Assert.Equal(85.55, points[Discipline.Long], 2);
        Assert.Equal(60.59, points[Discipline.Middle], 2);
        Assert.Equal(215.19, points[Discipline.Night], 2);
        Assert.Equal(84.91, points[Discipline.Sprint], 2);
    }

    /// <summary>
    /// Under every list the page repeats the same average against the runner's own class. The
    /// points are identical and only the place differs — 1914th in the country, 203rd in H45.
    /// </summary>
    [Fact]
    public void The_place_on_the_runners_own_class_list_is_read()
    {
        var snapshot = Snapshot();

        Assert.Equal("H45", snapshot.Class?.Class);
        Assert.Equal(203, snapshot.Class?.Place);
    }

    /// <summary>The one link that says which club page to merge a club place from.</summary>
    [Fact]
    public void The_page_names_the_runners_club()
    {
        var club = RunnerRankingParser.Club(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Ranking", "runner-121330.html")));

        Assert.Equal("115", club?.Id);
        Assert.Equal("Gävle OK", club?.Name);
    }

    [Fact]
    public void The_results_behind_the_average_are_read()
    {
        var results = Snapshot().Results;

        Assert.True(results.Count > 100);

        var latest = results[0];
        Assert.Equal(new DateOnly(2026, 7, 24), latest.Date);
        Assert.Contains("O-Ringen", latest.CompetitionName);
    }

    /// <summary>Sverigelistan is the average of six; the page marks which six.</summary>
    [Fact]
    public void Exactly_the_counting_results_are_marked()
    {
        Assert.Equal(6, Snapshot().Counting.Count());
    }

    /// <summary>A result drops out exactly one year after it was run.</summary>
    [Fact]
    public void A_result_expires_a_year_after_the_race()
    {
        var counting = Snapshot().Counting.First();

        Assert.Equal(counting.Date.AddYears(1), counting.ExpiresOn);
    }

    [Fact]
    public void A_page_that_is_not_a_runner_page_is_null() =>
        Assert.Null(RunnerRankingParser.Parse("121330", "<html><body>Avgift krävs</body></html>", ReadOn));
}
