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
    public void The_four_weights_add_up_to_one()
    {
        double total = RelevanceEngine.PersonalWeight
                     + RelevanceEngine.ImportanceWeight
                     + RelevanceEngine.GeographicWeight
                     + RelevanceEngine.TemporalWeight;

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
            RelevanceEngine.TemporalScore(closing, Context()) >
            RelevanceEngine.TemporalScore(relaxed, Context()));
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
