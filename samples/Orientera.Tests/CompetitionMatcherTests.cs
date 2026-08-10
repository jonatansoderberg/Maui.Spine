using Orientera.Backend.LiveResults;

namespace Orientera.Tests;

/// <summary>
/// Spike SP-04. Eventor and LiveResults share no ids, so the link between them is a judgement
/// made on a runner's behalf — and the judgement these pin is that a wrong live list is worse
/// than none.
/// </summary>
public class CompetitionMatcherTests
{
    private readonly IReadOnlyList<LiveCompetition> _calendar =
        LiveResultsNormalizer.ForZone("Europe/Stockholm").Competitions(Fixture.LiveResults("competitions.json"));

    private static Competition Eventor(string name, string organiser, DateOnly date) => new()
    {
        Id = new CompetitionId("38499"),
        Name = name,
        Organiser = organiser,
        District = "Gästrikland",
        Place = "Näset",
        Location = new GeoPoint(60.67, 17.14),
        Discipline = Discipline.Middle,
        Level = CompetitionLevel.Championship,
        FirstStart = date.ToDateTime(new TimeOnly(10, 0)),
        LastFinish = date.ToDateTime(new TimeOnly(15, 0)),
    };

    [Fact]
    public void The_same_competition_in_both_systems_matches()
    {
        var match = CompetitionMatcher.Match(
            Eventor("Norrlandsmästerskapen, medel", "Gävle OK", new DateOnly(2026, 8, 9)),
            _calendar);

        Assert.NotNull(match);
        Assert.Equal(37308, match.Competition.Id);
        Assert.Equal(1.0, match.Confidence);
    }

    /// <summary>Two people writing the same competition's name never write it the same way.</summary>
    [Fact]
    public void Punctuation_and_case_do_not_break_a_match()
    {
        var match = CompetitionMatcher.Match(
            Eventor("Norrlandsmästerskapen Medel", "GÄVLE OK", new DateOnly(2026, 8, 9)),
            _calendar);

        Assert.NotNull(match);
        Assert.Equal(37308, match.Competition.Id);
    }

    /// <summary>
    /// The distance is one token, and it is the token that decides. A middle and a long on the
    /// same weekend by the same club are two competitions, not one.
    /// </summary>
    [Fact]
    public void The_wrong_race_of_a_weekend_is_not_matched()
    {
        var match = CompetitionMatcher.Match(
            Eventor("Norrlandsmästerskapen, lång", "Gävle OK", new DateOnly(2026, 8, 9)),
            _calendar);

        Assert.NotEqual(37308, match?.Competition.Id);
    }

    /// <summary>A competition is run on its day; a different day is a different competition.</summary>
    [Fact]
    public void A_competition_on_another_day_is_never_matched()
    {
        var match = CompetitionMatcher.Match(
            Eventor("Norrlandsmästerskapen, medel", "Gävle OK", new DateOnly(2026, 8, 15)),
            _calendar);

        Assert.Null(match);
    }

    [Fact]
    public void A_competition_that_is_not_in_the_live_calendar_has_no_match()
    {
        var match = CompetitionMatcher.Match(
            Eventor("Veckans bana, etapp 7", "Stora Tuna OK", new DateOnly(2026, 8, 9)),
            _calendar);

        Assert.Null(match);
    }

    /// <summary>
    /// Two candidates that score alike are two competitions we cannot tell apart. Picking
    /// either would be a coin toss shown to the user as a fact.
    /// </summary>
    [Fact]
    public void Two_equally_good_candidates_produce_no_match()
    {
        var twins = new List<LiveCompetition>
        {
            new() { Id = 1, Name = "Vårserien", Organizer = "Gävle OK", Date = new DateOnly(2026, 8, 9) },
            new() { Id = 2, Name = "Vårserien", Organizer = "Gävle OK", Date = new DateOnly(2026, 8, 9) },
        };

        Assert.Null(CompetitionMatcher.Match(Eventor("Vårserien", "Gävle OK", new DateOnly(2026, 8, 9)), twins));
    }

    [Fact]
    public void A_weak_candidate_is_left_alone()
    {
        var unrelated = new List<LiveCompetition>
        {
            new() { Id = 9, Name = "Motionsorientering Tuve", Organizer = "Tolereds AIK", Date = new DateOnly(2026, 8, 9) },
        };

        Assert.Null(CompetitionMatcher.Match(Eventor("DM, Sprint", "Gävle OK", new DateOnly(2026, 8, 9)), unrelated));
    }
}
