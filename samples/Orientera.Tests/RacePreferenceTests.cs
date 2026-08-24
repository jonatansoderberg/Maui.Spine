using Orientera.Services.Local;
using Orientera.Services.Relevance;

namespace Orientera.Tests;

/// <summary>
/// The two halves of "vad håller du på med": the sports, which decide what exists at all, and the
/// favourites, which only decide what comes first.
/// </summary>
public class RacePreferenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2));

    private static Competition Competition(Sport sport, Discipline discipline) => new()
    {
        Id = new CompetitionId($"{sport}-{discipline}"),
        Name = "Tävling",
        Organiser = "Gävle OK",
        District = "Gästrikland",
        Place = "Hemlingby",
        Location = new GeoPoint(60.6, 17.1),
        Sport = sport,
        Discipline = discipline,
        Level = CompetitionLevel.National,
        FirstStart = Now.AddDays(7),
        LastFinish = Now.AddDays(7).AddHours(4),
    };

    private static RelevanceContext Context(params RacePreference[] favourites) => new()
    {
        Now = Now,
        Home = new GeoPoint(60.66, 17.14),
        HomeDistrict = "Gästrikland",
        MyClass = "H21",
        Favourites = favourites,
    };

    /// <summary>A preference nobody has set must not hide anything — the same rule as the districts.</summary>
    [Fact]
    public void No_sport_chosen_allows_every_sport()
    {
        Assert.True(RacePreferences.None.Allows(Sport.MountainBike));
        Assert.True(RacePreferences.None.Allows(Sport.Foot));
    }

    [Fact]
    public void A_chosen_sport_shuts_the_others_out()
    {
        var onFoot = new RacePreferences(new HashSet<Sport> { Sport.Foot, Sport.Indoor }, []);

        Assert.True(onFoot.Allows(Sport.Foot));
        Assert.True(onFoot.Allows(Sport.Indoor));
        Assert.False(onFoot.Allows(Sport.MountainBike));
    }

    /// <summary>The position is the weight: first place is worth twice second.</summary>
    [Fact]
    public void The_place_on_the_list_is_the_score()
    {
        var context = Context(
            new RacePreference(Sport.Indoor, Discipline.Sprint),
            new RacePreference(Sport.Foot, Discipline.Sprint),
            new RacePreference(Sport.Foot, Discipline.Middle));

        Assert.Equal(1.0, RelevanceEngine.PreferenceScore(Competition(Sport.Indoor, Discipline.Sprint), context));
        Assert.Equal(0.5, RelevanceEngine.PreferenceScore(Competition(Sport.Foot, Discipline.Sprint), context));
        Assert.Equal(1.0 / 3, RelevanceEngine.PreferenceScore(Competition(Sport.Foot, Discipline.Middle), context), 6);
    }

    /// <summary>
    /// A pair is a pair. Liking indoor sprints says nothing about sprints in a forest, which is
    /// the whole reason the setting is not two separate lists.
    /// </summary>
    [Fact]
    public void The_sport_and_the_distance_have_to_match_together()
    {
        var context = Context(new RacePreference(Sport.Indoor, Discipline.Sprint));

        Assert.Equal(1.0, RelevanceEngine.PreferenceScore(Competition(Sport.Indoor, Discipline.Sprint), context));
        Assert.Equal(0.0, RelevanceEngine.PreferenceScore(Competition(Sport.Foot, Discipline.Sprint), context));
        Assert.Equal(0.0, RelevanceEngine.PreferenceScore(Competition(Sport.Indoor, Discipline.Middle), context));
    }

    /// <summary>
    /// Adding a sixth favourite must not make the first one matter less, or the runner who fills
    /// the list in carefully ends up with a flatter calendar than one who named a single race.
    /// </summary>
    [Fact]
    public void A_longer_list_does_not_devalue_what_is_already_on_it()
    {
        var first = new RacePreference(Sport.Foot, Discipline.Long);

        double alone = RelevanceEngine.PreferenceScore(
            Competition(Sport.Foot, Discipline.Long), Context(first));

        double amongOthers = RelevanceEngine.PreferenceScore(
            Competition(Sport.Foot, Discipline.Long),
            Context(first,
                new RacePreference(Sport.Foot, Discipline.Middle),
                new RacePreference(Sport.Foot, Discipline.Sprint)));

        Assert.Equal(alone, amongOthers);
    }

    /// <summary>Nothing on the list scores nothing. It is a preference, not a filter.</summary>
    [Fact]
    public void A_race_nobody_named_still_scores_zero_and_not_less()
    {
        Assert.Equal(0.0, RelevanceEngine.PreferenceScore(
            Competition(Sport.Foot, Discipline.Relay), Context()));
    }
}
