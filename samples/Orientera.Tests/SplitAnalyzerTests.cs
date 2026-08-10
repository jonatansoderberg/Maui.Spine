using Orientera.Services.Analysis;

namespace Orientera.Tests;

public class SplitAnalyzerTests
{
    private static CompetitionResult Runner(string id, params int[] legSeconds)
    {
        var splits = new List<Split>();
        var elapsed = TimeSpan.Zero;

        for (int i = 0; i < legSeconds.Length; i++)
        {
            var leg = TimeSpan.FromSeconds(legSeconds[i]);
            elapsed += leg;

            splits.Add(new Split
            {
                ControlNumber = i + 1,
                ControlCode = (31 + i).ToString(),
                LegTime = leg,
                ElapsedTime = elapsed,
            });
        }

        return new CompetitionResult
        {
            Id = new ResultId(id),
            Competition = new CompetitionId("c-test"),
            Person = new PersonId(id),
            Name = id,
            Club = "Testklubben",
            Class = "D21",
            Status = ResultStatus.Ok,
            Time = elapsed,
            Starters = 3,
            Splits = splits,
        };
    }

    [Fact]
    public void A_runner_who_is_evenly_slower_has_made_no_mistakes()
    {
        // 20% down on every leg is pace, not eleven separate errors.
        var winner = Runner("winner", 100, 200, 150, 300, 250);
        var steady = Runner("steady", 120, 240, 180, 360, 300);

        var legs = SplitAnalyzer.Analyse(steady, [winner, steady]);

        Assert.All(legs, leg => Assert.False(leg.IsLikelyMistake));
        Assert.Equal(TimeSpan.Zero, SplitAnalyzer.TotalMistakeTime(legs));
    }

    [Fact]
    public void A_leg_far_outside_the_runners_own_pace_is_flagged()
    {
        var winner = Runner("winner", 100, 200, 150, 300, 250);
        var bommade = Runner("bommade", 105, 210, 158, 480, 262);

        var legs = SplitAnalyzer.Analyse(bommade, [winner, bommade]);

        var flagged = legs.Where(l => l.IsLikelyMistake).ToList();

        var mistake = Assert.Single(flagged);
        Assert.Equal(4, mistake.ControlNumber);
        Assert.True(mistake.MistakeConfidence > 0.5);
        Assert.True(mistake.EstimatedMistakeTime > TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Loss_to_the_best_is_observed_even_where_nothing_is_flagged()
    {
        var winner = Runner("winner", 100, 200, 150, 300, 250);
        var steady = Runner("steady", 120, 240, 180, 360, 300);

        var legs = SplitAnalyzer.Analyse(steady, [winner, steady]);

        Assert.All(legs, leg => Assert.True(leg.LossToBest > TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(20), legs[0].LossToBest);
    }

    [Fact]
    public void A_small_absolute_loss_is_noise_however_bad_the_ratio()
    {
        // Doubling a 10-second leg looks terrible as a ratio and means nothing in a race.
        var winner = Runner("winner", 10, 200, 150, 300, 250);
        var runner = Runner("runner", 22, 205, 154, 308, 256);

        var legs = SplitAnalyzer.Analyse(runner, [winner, runner]);

        Assert.False(legs[0].IsLikelyMistake);
    }

    [Fact]
    public void The_theoretical_time_removes_only_the_flagged_losses()
    {
        var winner = Runner("winner", 100, 200, 150, 300, 250);
        var bommade = Runner("bommade", 105, 210, 158, 480, 262);

        var legs = SplitAnalyzer.Analyse(bommade, [winner, bommade]);
        var theoretical = SplitAnalyzer.TheoreticalTime(bommade.Time!.Value, legs);

        Assert.True(theoretical < bommade.Time);
        Assert.True(theoretical > winner.Time, "removing a mistake must not turn a runner into the winner");
        Assert.Equal(bommade.Time!.Value - SplitAnalyzer.TotalMistakeTime(legs), theoretical);
    }

    [Fact]
    public void Leg_place_and_position_after_come_from_the_field()
    {
        var fast = Runner("fast", 100, 200, 150);
        var middle = Runner("middle", 110, 220, 160);
        var slow = Runner("slow", 130, 260, 190);

        var legs = SplitAnalyzer.Analyse(middle, [fast, middle, slow]);

        Assert.Equal(2, legs[0].LegPlace);
        Assert.Equal(2, legs[0].PositionAfter);
        Assert.Equal(1, SplitAnalyzer.Analyse(fast, [fast, middle, slow])[0].LegPlace);
    }

    [Fact]
    public void An_even_race_is_more_stable_than_a_swinging_one()
    {
        var winner = Runner("winner", 100, 200, 150, 300, 250);
        var even = Runner("even", 110, 220, 165, 330, 275);
        var swinging = Runner("swinging", 100, 320, 150, 460, 250);

        double evenIndex = SplitAnalyzer.StabilityIndex(SplitAnalyzer.Analyse(even, [winner, even]));
        double swingIndex = SplitAnalyzer.StabilityIndex(SplitAnalyzer.Analyse(swinging, [winner, swinging]));

        Assert.True(evenIndex > swingIndex);
        Assert.InRange(evenIndex, 0.0, 1.0);
    }

    [Fact]
    public void A_result_without_splits_analyses_to_nothing()
    {
        var bare = Runner("bare") with { Splits = [] };

        Assert.Empty(SplitAnalyzer.Analyse(bare, [bare]));
    }

    [Fact]
    public void Only_the_same_class_is_used_as_the_comparison_field()
    {
        var mine = Runner("mine", 200, 200, 200);
        var otherClass = Runner("other", 50, 50, 50) with { Class = "H21" };

        var legs = SplitAnalyzer.Analyse(mine, [mine, otherClass]);

        // The H21 runner's much faster legs must not become my class's best times.
        Assert.Equal(TimeSpan.FromSeconds(200), legs[0].BestLegTime);
        Assert.Equal(TimeSpan.Zero, legs[0].LossToBest);
    }
}
