using Orientera.Services.FakeData;
using Orientera.Services.Relevance;

namespace Orientera.Tests;

public class RelevanceEngineTests
{
    private static readonly GeoPoint Gavle = new(60.6749, 17.1413);
    private static readonly GeoPoint Sandviken = new(60.6180, 16.7760);
    private static readonly GeoPoint Falun = new(60.6790, 15.7400);

    private static readonly DateTimeOffset Now =
        new(new DateTime(2026, 8, 10, 12, 0, 0), TimeSpan.FromHours(2));

    private static Competition Make(
        string id,
        CompetitionLevel level,
        GeoPoint location,
        int daysAway = 14,
        string district = "Gästrikland",
        SeriesId? series = null,
        IReadOnlyList<string>? classes = null,
        string organiser = "Gävle OK",
        CompetitionSchedule? schedule = null) => new()
    {
        Id = new CompetitionId(id),
        Name = id,
        Organiser = organiser,
        District = district,
        Place = "Testplats",
        Location = location,
        Discipline = Discipline.Middle,
        Level = level,
        FirstStart = Now.AddDays(daysAway),
        LastFinish = Now.AddDays(daysAway).AddHours(4),
        Series = series,
        Classes = classes ?? ["D21", "H21"],
        Schedule = schedule ?? new CompetitionSchedule(),
    };

    private static RelevanceContext Context(
        IReadOnlySet<CompetitionId>? mine = null,
        IReadOnlySet<CompetitionId>? group = null,
        IReadOnlySet<SeriesId>? series = null) => new()
    {
        Now = Now,
        Home = Gavle,
        HomeDistrict = "Gästrikland",
        MyClass = "D21",
        MyEntries = mine ?? new HashSet<CompetitionId>(),
        GroupEntries = group ?? new HashSet<CompetitionId>(),
        FollowedSeries = series ?? new HashSet<SeriesId>(),
    };

    [Fact]
    public void The_five_weights_add_up_to_one()
    {
        double total = RelevanceEngine.PersonalWeight
                     + RelevanceEngine.ImportanceWeight
                     + RelevanceEngine.GeographicWeight
                     + RelevanceEngine.TemporalWeight
                     + RelevanceEngine.UrgencyWeight;

        Assert.Equal(1.0, total, 6);
    }

    [Fact]
    public void All_sub_scores_stay_inside_the_unit_range()
    {
        var competitions = FakeDataset.Instance.Competitions;

        foreach (var score in competitions.Select(c => RelevanceEngine.Score(c, Context())))
        {
            Assert.InRange(score.Importance, 0.0, 1.0);
            Assert.InRange(score.Personal, 0.0, 1.0);
            Assert.InRange(score.Geographic, 0.0, 1.0);
            Assert.InRange(score.Temporal, 0.0, 1.0);
            Assert.InRange(score.Total, 0.0, 1.0);
        }
    }

    [Fact]
    public void Being_entered_is_the_strongest_personal_signal()
    {
        var competition = Make("entered", CompetitionLevel.Local, Gavle);

        var entered = RelevanceEngine.PersonalScore(competition, Context(mine: new HashSet<CompetitionId> { competition.Id }));
        var notEntered = RelevanceEngine.PersonalScore(competition, Context());

        Assert.Equal(1.0, entered);
        Assert.True(entered > notEntered);
    }

    [Fact]
    public void A_group_entry_scores_below_my_own_but_well_above_nothing()
    {
        var competition = Make("group", CompetitionLevel.Local, Gavle);
        var ids = new HashSet<CompetitionId> { competition.Id };

        double mine = RelevanceEngine.PersonalScore(competition, Context(mine: ids));
        double group = RelevanceEngine.PersonalScore(competition, Context(group: ids));
        double neither = RelevanceEngine.PersonalScore(competition, Context());

        Assert.True(mine > group);
        Assert.True(group > neither);
    }

    [Fact]
    public void A_championship_further_away_still_beats_a_nearby_training_run()
    {
        // The spec's balance: "nära events prioriteras, men får inte alltid slå mästerskap".
        var championship = Make("dm", CompetitionLevel.Championship, Falun, district: "Dalarna");
        var training = Make("traning", CompetitionLevel.Training, Gavle);

        double championshipScore = RelevanceEngine.Score(championship, Context()).Total;
        double trainingScore = RelevanceEngine.Score(training, Context()).Total;

        Assert.True(championshipScore > trainingScore,
            $"championship {championshipScore:F3} should outrank training {trainingScore:F3}");
    }

    /// <summary>
    /// Distance is a guess at whether you would travel; an entry is you having answered. The club
    /// evening in Dalarna this runner is signed up for scored zero on geography and dropped out of
    /// the top of their own calendar the first time geography was weighted up.
    /// </summary>
    [Fact]
    public void A_race_I_have_entered_is_not_charged_for_being_far_away()
    {
        var distant = Make("distant", CompetitionLevel.Training, Falun, district: "Dalarna");
        var ids = new HashSet<CompetitionId> { distant.Id };

        Assert.True(RelevanceEngine.GeographicScore(distant, Context()) < 1.0);
        Assert.Equal(1.0, RelevanceEngine.GeographicScore(distant, Context(mine: ids)));
    }

    [Fact]
    public void Distance_lowers_the_geographic_score_monotonically()
    {
        var near = Make("near", CompetitionLevel.National, Gavle);
        var mid = Make("mid", CompetitionLevel.National, Sandviken);
        var far = Make("far", CompetitionLevel.National, Falun, district: "Dalarna");

        double n = RelevanceEngine.GeographicScore(near, Context());
        double m = RelevanceEngine.GeographicScore(mid, Context());
        double f = RelevanceEngine.GeographicScore(far, Context());

        Assert.True(n > m);
        Assert.True(m > f);
    }

    [Fact]
    public void My_own_district_gets_a_boost_without_excluding_others()
    {
        var home = Make("home", CompetitionLevel.National, Falun, district: "Gästrikland");
        var away = Make("away", CompetitionLevel.National, Falun, district: "Dalarna");

        double homeScore = RelevanceEngine.GeographicScore(home, Context());
        double awayScore = RelevanceEngine.GeographicScore(away, Context());

        Assert.True(homeScore > awayScore);
        Assert.True(awayScore > 0.0, "a competition outside my district must still be discoverable");
    }

    [Fact]
    public void Sooner_is_more_relevant_than_later()
    {
        var tomorrow = Make("tomorrow", CompetitionLevel.National, Gavle, daysAway: 1);
        var nextMonth = Make("next-month", CompetitionLevel.National, Gavle, daysAway: 45);

        Assert.True(
            RelevanceEngine.TemporalScore(tomorrow, Context()) >
            RelevanceEngine.TemporalScore(nextMonth, Context()));
    }

    [Fact]
    public void A_competition_running_right_now_is_maximally_timely()
    {
        // Without this the ongoing race falls into the "past" branch and ranks below tomorrow.
        var ongoing = Make("ongoing", CompetitionLevel.Championship, Gavle, daysAway: 0) with
        {
            FirstStart = Now.AddHours(-2),
            LastFinish = Now.AddHours(2),
        };

        var tomorrow = Make("tomorrow", CompetitionLevel.Championship, Gavle, daysAway: 1);

        Assert.Equal(1.0, RelevanceEngine.TemporalScore(ongoing, Context()));
        Assert.True(
            RelevanceEngine.Score(ongoing, Context()).Total >=
            RelevanceEngine.Score(tomorrow, Context()).Total);
    }

    [Fact]
    public void The_past_decays_below_anything_upcoming()
    {
        var lastWeek = Make("last-week", CompetitionLevel.Championship, Gavle, daysAway: -7);
        var upcoming = Make("upcoming", CompetitionLevel.Championship, Gavle, daysAway: 30);

        Assert.True(
            RelevanceEngine.TemporalScore(lastWeek, Context()) <
            RelevanceEngine.TemporalScore(upcoming, Context()));
    }

    [Fact]
    public void A_closing_entry_deadline_pulls_a_competition_up()
    {
        var schedule = new CompetitionSchedule
        {
            RegistrationOpensAt = Now.AddDays(-30),
            EntryDeadline = Now.AddDays(2),
        };

        var closing = Make("closing", CompetitionLevel.National, Gavle, daysAway: 20, schedule: schedule);
        var relaxed = Make("relaxed", CompetitionLevel.National, Gavle, daysAway: 20);

        Assert.True(
            RelevanceEngine.Ranking(closing, Context()) >
            RelevanceEngine.Ranking(relaxed, Context()));
    }

    /// <summary>
    /// Eventor publishes an entry deadline for almost everything and an opening date for almost
    /// nothing. The old bonus asked for both, so it never fired for the närtävlingar it existed
    /// to lift — the deadline alone has to be enough.
    /// </summary>
    [Fact]
    public void A_deadline_without_an_opening_date_still_counts()
    {
        var schedule = new CompetitionSchedule { EntryDeadline = Now.AddDays(2) };
        var closing = Make("closing", CompetitionLevel.Local, Gavle, daysAway: 20, schedule: schedule);

        Assert.True(RelevanceEngine.UrgencyScore(closing, Context()) > 0.8);
    }

    /// <summary>An entry that has not opened is not something to act on today.</summary>
    [Fact]
    public void An_entry_that_has_not_opened_is_not_urgent()
    {
        var schedule = new CompetitionSchedule
        {
            RegistrationOpensAt = Now.AddDays(1),
            EntryDeadline = Now.AddDays(3),
        };

        var later = Make("later", CompetitionLevel.National, Gavle, daysAway: 20, schedule: schedule);

        Assert.Equal(0.0, RelevanceEngine.UrgencyScore(later, Context()));
    }

    /// <summary>A deadline that has passed is not urgent; it is over.</summary>
    [Fact]
    public void A_passed_deadline_is_not_urgent()
    {
        var schedule = new CompetitionSchedule { EntryDeadline = Now.AddDays(-1) };
        var closed = Make("closed", CompetitionLevel.National, Gavle, daysAway: 5, schedule: schedule);

        Assert.Equal(0.0, RelevanceEngine.UrgencyScore(closed, Context()));
    }

    /// <summary>
    /// The case the axis was added for: a närtävling twelve kilometres away in the home district,
    /// closing in three days, against a championship in another district seventy-six kilometres out.
    /// The near one wins now — but the home-district championship still wins over both.
    /// </summary>
    [Fact]
    public void A_near_race_closing_soon_outranks_a_distant_championship()
    {
        var closing = new CompetitionSchedule { EntryDeadline = Now.AddDays(3) };

        var nearby = Make(
            "nearby",
            CompetitionLevel.Local,
            Gavle,
            daysAway: 7,
            district: "Gästrikland",
            schedule: closing);

        var faraway = Make(
            "faraway",
            CompetitionLevel.Championship,
            Falun,
            daysAway: 12,
            district: "Västmanland");

        var home = Make(
            "home",
            CompetitionLevel.Championship,
            Gavle,
            daysAway: 12,
            district: "Gästrikland");

        Assert.True(RelevanceEngine.Ranking(nearby, Context()) > RelevanceEngine.Ranking(faraway, Context()));
        Assert.True(RelevanceEngine.Ranking(home, Context()) > RelevanceEngine.Ranking(nearby, Context()));
    }

    [Fact]
    public void A_competition_without_my_class_is_pushed_down()
    {
        var mine = Make("mine", CompetitionLevel.National, Gavle, classes: ["D21", "H21"]);
        var youthOnly = Make("youth", CompetitionLevel.National, Gavle, classes: ["H14", "D14"]);

        Assert.True(
            RelevanceEngine.PersonalScore(mine, Context()) >
            RelevanceEngine.PersonalScore(youthOnly, Context()));
    }

    [Fact]
    public void A_followed_series_lifts_both_importance_and_personal_relevance()
    {
        var series = new SeriesId("s-test");
        var round = Make("round", CompetitionLevel.District, Gavle, series: series);
        var followed = Context(series: new HashSet<SeriesId> { series });

        Assert.True(RelevanceEngine.ImportanceScore(round, followed) > RelevanceEngine.ImportanceScore(round, Context()));
        Assert.True(RelevanceEngine.PersonalScore(round, followed) > RelevanceEngine.PersonalScore(round, Context()));
    }

    [Fact]
    public void Ranking_the_seeded_calendar_puts_my_own_championship_first()
    {
        var context = Context(mine: new HashSet<CompetitionId> { FakeDataset.NmLongId });

        var ranked = RelevanceEngine.Rank(FakeDataset.Instance.Competitions, context);

        Assert.Equal(FakeDataset.NmLongId, ranked[0].Id);
    }

    [Fact]
    public void Recreational_training_sinks_below_real_competitions()
    {
        var ranked = RelevanceEngine.Rank(FakeDataset.Instance.Competitions, Context()).ToList();

        double averageRealRank = ranked.Index().Where(x => !x.Item.IsLowPriority).Average(x => x.Index);
        double averageTrainingRank = ranked.Index().Where(x => x.Item.IsLowPriority).Average(x => x.Index);

        Assert.True(averageRealRank < averageTrainingRank,
            $"real competitions average rank {averageRealRank:F1} should beat training {averageTrainingRank:F1}");
    }
}

/// <summary>
/// What settles the order when two competitions are equally relevant.
/// </summary>
/// <remarks>
/// Gästriklands DM medel and DM stafett: same championship, same club, same entry deadline,
/// arenas forty metres apart. Both shown as 41 km. The geographic score differed in the fifth
/// decimal, and the calendar put Sunday's race above Saturday's — an order that looks arbitrary
/// because it is.
/// </remarks>
public class RelevanceTieBreakTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.FromHours(2));

    private static Competition Championship(string name, int day, double longitude) => new()
    {
        Id = new CompetitionId(name),
        Name = name,
        Organiser = "Ockelbo OK",
        District = "Gästrikland",
        Place = "Ockelbo",
        Location = new GeoPoint(60.8398, longitude),
        Discipline = Discipline.Middle,
        Level = CompetitionLevel.Championship,
        FirstStart = new DateTimeOffset(2026, 8, day, 10, 0, 0, TimeSpan.FromHours(2)),
        LastFinish = new DateTimeOffset(2026, 8, day, 15, 0, 0, TimeSpan.FromHours(2)),
    };

    private static RelevanceContext Context() => new()
    {
        Now = Now,
        Home = new GeoPoint(60.6749, 17.1413),
        HomeDistrict = "Gästrikland",
        MyClass = "H45",
    };

    [Fact]
    public void Forty_metres_between_arenas_does_not_decide_the_order()
    {
        var saturday = Championship("DM, medel", 29, 16.457287723341);
        var sunday = Championship("DM, stafett", 30, 16.4580667866488);

        var ranked = RelevanceEngine.Rank([sunday, saturday], Context());

        Assert.Equal("DM, medel", ranked[0].Name);
    }

    /// <summary>The rounding must not flatten a difference that is real.</summary>
    [Fact]
    public void A_difference_worth_believing_still_wins()
    {
        var near = Championship("Nära", 30, 17.10);
        var far = Championship("Långt bort", 29, 14.00);

        var ranked = RelevanceEngine.Rank([far, near], Context());

        Assert.Equal("Nära", ranked[0].Name);
    }
}
