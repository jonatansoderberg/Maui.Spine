using Orientera.Services.Analysis;
using Orientera.Services.FakeData;

namespace Orientera.Tests;

/// <summary>
/// The facts a race narrative may be built from. These are the guardrail: whatever a language
/// model does with the wording afterwards, it is given nothing but what is proven here — so a
/// claim that is not in <see cref="RaceStoryFacts.Lines"/> is a claim the model invented.
/// </summary>
public class RaceStoryFactsTests
{
    private static readonly CompetitionResult Winner = Result(
        place: 1,
        legs: [180, 265, 140, 330, 215, 290],
        behind: TimeSpan.Zero);

    /// <summary>Same pace as the winner, plus two minutes dropped on one control.</summary>
    private static readonly CompetitionResult WithMistake = Result(
        place: 2,
        legs: [190, 280, 145, 470, 225, 300],
        behind: TimeSpan.FromSeconds(190),
        name: "Bommaren");

    private static IReadOnlyList<CompetitionResult> Field => [Winner, WithMistake];

    private static IReadOnlyList<string> LinesFor(CompetitionResult result) =>
        RaceStoryFacts.From(result, SplitAnalyzer.Analyse(result, Field), Field).Lines;

    [Fact]
    public void A_race_always_ends_with_how_it_ended()
    {
        var lines = LinesFor(Winner);

        Assert.Contains(lines, l => l.StartsWith("I mål:"));
        Assert.Equal(lines[^1], lines.Last(l => l.StartsWith("I mål:")));
    }

    [Fact]
    public void The_fastest_first_leg_is_said_plainly()
    {
        Assert.Contains(LinesFor(Winner), l => l == "Snabbast i klassen till första kontrollen.");
    }

    /// <summary>
    /// A mistake is modelled, not measured, and the sentence has to keep saying so — this is the
    /// same rule the Analys tab follows with <c>EstimateInk</c>.
    /// </summary>
    [Fact]
    public void A_mistake_is_named_by_its_control_and_hedged()
    {
        var mistake = Assert.Single(LinesFor(WithMistake), l => l.Contains("tapp"));

        Assert.StartsWith("Kontroll 4:", mistake);
        Assert.Contains("uppskattat", mistake);
        Assert.Contains("omkring", mistake);
    }

    /// <summary>
    /// The runner's own word for where a mistake happened is the control, not the code number on
    /// it — "kontroll 4", never "kontroll 34".
    /// </summary>
    [Fact]
    public void A_mistake_is_not_named_by_its_code()
    {
        var mistake = Assert.Single(LinesFor(WithMistake), l => l.Contains("tapp"));

        Assert.DoesNotContain("34", mistake);
        Assert.DoesNotContain("sträcka", mistake);
    }

    /// <summary>
    /// The claim the issue was written around: "bland de snabbaste mellan 7 och 10" must not be
    /// said about a runner who was not. Second of two on every leg is not a strong stretch.
    /// </summary>
    [Fact]
    public void A_middling_runner_gets_no_strong_stretch()
    {
        Assert.DoesNotContain(LinesFor(WithMistake), l => l.StartsWith("Från "));
    }

    /// <summary>
    /// A stretch is named by the controls it runs between: the six legs of this course start at
    /// the start and end at control 6, so it is never "kontroll 1 till 6".
    /// </summary>
    [Fact]
    public void A_runner_who_led_every_leg_gets_one()
    {
        var stretch = Assert.Single(LinesFor(Winner), l => l.StartsWith("Från "));

        Assert.Equal("Från start till kontroll 6: snabbaste sträcktid i klassen hela vägen.", stretch);
    }

    /// <summary>A race with no splits has nothing to tell, but still says how it ended.</summary>
    [Fact]
    public void A_result_without_splits_still_produces_a_finish()
    {
        var bare = Winner with { Splits = [] };

        var line = Assert.Single(RaceStoryFacts.From(bare, [], Field).Lines);

        Assert.StartsWith("I mål:", line);
    }

    [Fact]
    public void A_mispunch_is_told_as_a_mispunch()
    {
        var mispunched = WithMistake with { Status = ResultStatus.Mispunch, Place = null };

        Assert.Contains(LinesFor(mispunched), l => l.Contains("felstämplat"));
    }

    private static CompetitionResult Result(
        int? place,
        int[] legs,
        TimeSpan behind,
        string name = "Vinnaren")
    {
        var splits = new List<Split>(legs.Length);
        var elapsed = TimeSpan.Zero;

        for (int i = 0; i < legs.Length; i++)
        {
            elapsed += TimeSpan.FromSeconds(legs[i]);

            splits.Add(new Split
            {
                ControlNumber = i + 1,
                ControlCode = (31 + i).ToString(),
                LegTime = TimeSpan.FromSeconds(legs[i]),
                ElapsedTime = elapsed,
            });
        }

        return new CompetitionResult
        {
            Id = new ResultId($"r-{name}"),
            Competition = FakeDataset.NmLongId,
            Person = new PersonId($"p-{name}"),
            Name = name,
            Club = "OK Testklubben",
            Class = "D21",
            Status = ResultStatus.Ok,
            Time = elapsed,
            Place = place,
            BehindWinner = behind,
            Starters = 2,
            Splits = splits,
        };
    }
}
