using Orientera.Backend.Eventor;

namespace Orientera.Tests;

/// <summary>
/// One event, one class, two races. Karlstad Indoor is an <c>IndMultiDay</c> with "Etapp 1" and
/// "Etapp 2" on the same afternoon, and the result list showed two first places, two seconds, and
/// the same runner twice — because every race's result was filed under the same class name.
/// </summary>
public class MultiRaceResultTests
{
    private static IReadOnlyList<CompetitionResult> Results() =>
        EventorNormalizer.ForZone("Europe/Stockholm").Results(
            Fixture.Eventor("results-flerlopp.xml"),
            new CompetitionId("56311"));

    [Fact]
    public void Each_race_keeps_its_own_class()
    {
        var classes = Results().Select(r => r.Class).Distinct().ToList();

        Assert.Equal(["Herrar, Etapp 1", "Herrar, Etapp 2"], classes.Order().ToList());
    }

    /// <summary>The same runner ran both, so one place per race and never two in one list.</summary>
    [Fact]
    public void A_runner_who_ran_both_has_one_result_in_each()
    {
        var results = Results();

        Assert.Equal(2, results.Count);
        Assert.Single(results, r => r.Class == "Herrar, Etapp 1" && r.Place == 2);
        Assert.Single(results, r => r.Class == "Herrar, Etapp 2" && r.Place == 4);
    }

    /// <summary>
    /// A field is the race's, not the weekend's. The class says 91 started across both; second of
    /// 91 is a placing out of a list that never ran together.
    /// </summary>
    [Fact]
    public void The_field_is_the_race_that_was_run()
    {
        var results = Results();

        Assert.Equal(44, Assert.Single(results, r => r.Class == "Herrar, Etapp 1").Starters);
        Assert.Equal(47, Assert.Single(results, r => r.Class == "Herrar, Etapp 2").Starters);
    }
}
