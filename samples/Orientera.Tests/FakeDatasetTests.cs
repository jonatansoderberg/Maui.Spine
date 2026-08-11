using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Tests;

/// <summary>
/// The seed is a fixture the whole app is judged on, so it gets the same treatment as the
/// engines: it must be deterministic, internally consistent and actually reach the states the
/// demo depends on.
/// </summary>
public class FakeDatasetTests
{
    private static FakeDataset Data => FakeDataset.Instance;

    [Fact]
    public void The_seed_is_deterministic_across_constructions()
    {
        var a = Data.Runs[FakeDataset.NmLongId].Select(r => (r.Person.Id, r.TotalTime)).ToList();
        var b = FakeDataset.Instance.Runs[FakeDataset.NmLongId].Select(r => (r.Person.Id, r.TotalTime)).ToList();

        Assert.Equal(a, b);
    }

    [Fact]
    public void Control_codes_are_shared_by_everyone_on_the_same_course()
    {
        var d21 = Data.Runs[FakeDataset.NmLongId].Where(r => r.Class == "D21").ToList();

        var codeSets = d21.Select(r => string.Join(',', r.Splits.Select(s => s.ControlCode))).Distinct();

        Assert.Single(codeSets);
    }

    [Fact]
    public void Elapsed_times_are_the_running_sum_of_the_legs()
    {
        foreach (var run in Data.Runs.Values.SelectMany(r => r))
        {
            var expected = TimeSpan.Zero;

            foreach (var split in run.Splits)
            {
                expected += split.LegTime;
                Assert.Equal(expected, split.ElapsedTime);
            }
        }
    }

    [Fact]
    public void The_default_now_lands_in_the_middle_of_the_championship()
    {
        var nm = Data.Competitions.Single(c => c.Id == FakeDataset.NmLongId);

        Assert.True(FakeDataset.DefaultNow > nm.FirstStart);
        Assert.True(FakeDataset.DefaultNow < nm.LastFinish);

        var decision = ContextEngine.Evaluate(new ContextInput
        {
            Now = FakeDataset.DefaultNow,
            Competition = nm,
            MyEntryRegisteredAt = Data.Entries.First(e => e.Competition == nm.Id && e.Person == FakeDataset.MeId).RegisteredAt,
        });

        Assert.Equal(ContextState.Live, decision.State);
    }

    [Fact]
    public void At_the_default_now_some_runners_have_finished_and_some_are_still_out()
    {
        var runs = Data.Runs[FakeDataset.NmLongId];

        Assert.Contains(runs, r => r.HasFinishedBy(FakeDataset.DefaultNow));
        Assert.Contains(runs, r => r.HasStartedBy(FakeDataset.DefaultNow) && !r.HasFinishedBy(FakeDataset.DefaultNow));
    }

    [Fact]
    public void Elin_is_on_the_course_at_the_default_now()
    {
        var elin = Data.Runs[FakeDataset.NmLongId].Single(r => r.Person.Id == FakeDataset.MeId);

        Assert.True(elin.HasStartedBy(FakeDataset.DefaultNow));
        Assert.False(elin.HasFinishedBy(FakeDataset.DefaultNow));
    }

    [Fact]
    public void The_scripted_mistakes_survive_into_the_splits()
    {
        var elin = Data.Runs[FakeDataset.NmLongId].Single(r => r.Person.Id == FakeDataset.MeId);
        var reference = RunGenerator.LongCourse;

        // Legs 4 and 8 carry deliberate bommar; they must be clearly worse than the reference.
        Assert.True(elin.Splits[3].LegTime.TotalSeconds > reference[3] * 1.2);
        Assert.True(elin.Splits[7].LegTime.TotalSeconds > reference[7] * 1.2);
    }

    [Fact]
    public void Every_entry_belongs_to_a_seeded_competition_and_person()
    {
        var competitionIds = Data.Competitions.Select(c => c.Id).ToHashSet();
        var personIds = Data.People.Select(p => p.Id).ToHashSet();

        Assert.All(Data.Entries, e =>
        {
            Assert.Contains(e.Competition, competitionIds);
            Assert.Contains(e.Person, personIds);
        });
    }

    [Fact]
    public void Competition_ids_are_unique()
    {
        Assert.Equal(Data.Competitions.Count, Data.Competitions.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Every_competition_ends_after_it_starts()
    {
        Assert.All(Data.Competitions, c => Assert.True(c.LastFinish > c.FirstStart));
    }

    [Fact]
    public void Publication_timestamps_run_in_the_right_order()
    {
        foreach (var c in Data.Competitions)
        {
            var s = c.Schedule;

            if (s is { RegistrationOpensAt: { } opens, EntryDeadline: { } deadline })
                Assert.True(opens < deadline, $"{c.Name}: registration opens after the deadline");

            if (s is { ResultsPublishedAt: { } results, SplitsPublishedAt: { } splits })
                Assert.True(results <= splits, $"{c.Name}: splits published before results");

            if (s is { SplitsPublishedAt: { } splitsAt, MapPublishedAt: { } map })
                Assert.True(splitsAt <= map, $"{c.Name}: map published before splits");

            if (s.ResultsPublishedAt is { } published)
                Assert.True(published >= c.FirstStart, $"{c.Name}: results published before the first start");
        }
    }

    [Fact]
    public void The_championship_profile_carries_sources_for_every_fact()
    {
        var profile = Data.Competitions.Single(c => c.Id == FakeDataset.NmLongId).Profile;

        Assert.NotNull(profile);
        Assert.All(profile.Facts, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.SourceDocument));
            Assert.InRange(f.Confidence, 0.0, 1.0);
            Assert.True(f.Page > 0);
        });
    }

    [Fact]
    public void A_class_specific_note_reaches_its_classes_and_nobody_else()
    {
        var profile = Data.Competitions.Single(c => c.Id == FakeDataset.NmLongId).Profile!;

        var youth = profile.ForClass("H14").Select(f => f.Label).ToList();
        var adult = profile.ForClass("H45").Select(f => f.Label).ToList();

        Assert.Contains("Ungdomsbanor", youth);
        Assert.DoesNotContain("Ungdomsbanor", adult);
    }

    [Fact]
    public void Sverigelistan_counts_exactly_six_results()
    {
        Assert.Equal(6, Data.Ranking.Counting.Count());
    }

    [Fact]
    public void A_counting_result_is_about_to_expire_so_the_Jag_tab_has_something_to_warn_about()
    {
        var today = new DateOnly(2026, 8, 15);

        Assert.Contains(Data.Ranking.Results, r => r.ExpiresSoon(today));
    }

    [Fact]
    public void The_series_standing_drops_the_rounds_that_do_not_count()
    {
        var standing = Data.SeriesStandings.Single();
        var series = Data.Series.Single(s => s.Id == standing.Series);

        Assert.True(standing.Rounds.Count(r => r.IsCounting) <= series.CountingRounds);
        Assert.Contains(standing.Rounds, r => !r.IsCounting);
    }

    [Fact]
    public void A_predictions_interval_is_ordered_and_fits_inside_the_field()
    {
        Assert.All(Data.Predictions, p =>
        {
            Assert.True(p.LowPlace <= p.HighPlace);
            Assert.True(p.HighPlace <= p.FieldSize);
            Assert.NotEmpty(p.Drivers);
        });
    }

    /// <summary>
    /// A live snapshot reports the radio controls, not every control on the course — a
    /// competition puts a radio at a couple of them, and the split table has one column each.
    /// </summary>
    [Fact]
    public async Task A_live_snapshot_carries_the_classes_radio_controls()
    {
        var source = new FakeDataSource(new TimeMachineClock(FakeDataset.DefaultNow));

        var snapshot = await source.GetSnapshotAsync(FakeDataset.NmLongId);
        var controls = snapshot.ControlsFor("D21");

        var course = Data.Runs[FakeDataset.NmLongId].First(r => r.Class == "D21").Splits;

        Assert.NotEmpty(controls);
        Assert.True(controls.Count < course.Count);
        Assert.DoesNotContain(controls, c => c.Code == course[^1].ControlNumber);
        Assert.Equal(controls.Select(c => c.Code).Order(), controls.Select(c => c.Code));
    }

    /// <summary>
    /// A runner out on the course has passed some of the radios and not the rest, which is the
    /// state the split table exists to show.
    /// </summary>
    [Fact]
    public async Task A_runner_in_the_forest_has_passed_some_of_the_radios()
    {
        var source = new FakeDataSource(new TimeMachineClock(FakeDataset.DefaultNow));

        var snapshot = await source.GetSnapshotAsync(FakeDataset.NmLongId);
        var running = snapshot.Entries.Where(e => e.Status == LiveStatus.Running).ToList();

        Assert.NotEmpty(running);
        Assert.Contains(running, e => e.Passings.Count > 0);

        Assert.All(running, entry =>
        {
            Assert.True(entry.Passings.Count <= snapshot.ControlsFor(entry.Class).Count);
            Assert.Equal(entry.Passings.Select(p => p.Elapsed).Order(), entry.Passings.Select(p => p.Elapsed));
        });

        // Someone leads every radio that anyone has reached, and a run that will not be ranked
        // is timed at the radios without being placed at them.
        Assert.Contains(snapshot.Entries.SelectMany(e => e.Passings), passing => passing.Place == 1);
        Assert.All(snapshot.Entries.SelectMany(e => e.Passings), passing => Assert.True(passing.Place is null or >= 1));
    }
}
