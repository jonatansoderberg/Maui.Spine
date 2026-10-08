using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.BackgroundTasks.Tests;

/// <summary>Which tasks are due, in what order, and when the platform should be asked to run the next.</summary>
public class BackgroundTaskPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    private static Dictionary<string, BackgroundTaskState> States(params (string Name, DateTimeOffset Started)[] runs) =>
        runs.ToDictionary(r => r.Name, r => new BackgroundTaskState { LastStarted = r.Started }, StringComparer.Ordinal);

    [Fact]
    public void A_task_that_never_ran_is_due_now()
    {
        Assert.Equal(Now, BackgroundTaskPlanner.NextDue(Quarter, null, Now));
        Assert.Equal(["a"], BackgroundTaskPlanner.Due([("a", Quarter)], States(), Now));
    }

    [Fact]
    public void A_task_without_an_interval_is_never_due()
    {
        Assert.Null(BackgroundTaskPlanner.NextDue(TimeSpan.Zero, null, Now));
        Assert.Empty(BackgroundTaskPlanner.Due([("a", TimeSpan.Zero)], States(), Now));
        Assert.Null(BackgroundTaskPlanner.Earliest([("a", TimeSpan.Zero)], States(), Now));
    }

    [Fact]
    public void Due_an_interval_after_the_last_start()
    {
        var states = States(("a", Now.AddMinutes(-10)));

        Assert.Equal(Now.AddMinutes(5), BackgroundTaskPlanner.NextDue(Quarter, states["a"], Now));
        Assert.Empty(BackgroundTaskPlanner.Due([("a", Quarter)], states, Now));
        Assert.Equal(["a"], BackgroundTaskPlanner.Due([("a", Quarter)], states, Now.AddMinutes(5)));
    }

    [Fact]
    public void A_failed_run_waits_for_its_interval_like_a_completed_one()
    {
        var states = new Dictionary<string, BackgroundTaskState>
        {
            ["a"] = new() { LastStarted = Now.AddMinutes(-1), LastEnded = Now, LastOutcome = BackgroundTaskOutcome.Failed },
        };

        Assert.Empty(BackgroundTaskPlanner.Due([("a", Quarter)], states, Now));
    }

    [Fact]
    public void The_most_overdue_runs_first_and_ties_go_by_name()
    {
        var states = States(("late", Now.AddHours(-3)), ("b", Now.AddHours(-1)), ("a", Now.AddHours(-1)));

        var due = BackgroundTaskPlanner.Due([("a", Quarter), ("b", Quarter), ("late", Quarter), ("new", Quarter)], states, Now);

        // "new" never ran, so it is due now: the least overdue.
        Assert.Equal(["late", "a", "b", "new"], due);
    }

    [Fact]
    public void Earliest_is_the_soonest_due_task()
    {
        var states = States(("hourly", Now.AddMinutes(-50)), ("quarter", Now.AddMinutes(-5)));

        var earliest = BackgroundTaskPlanner.Earliest([("hourly", TimeSpan.FromHours(1)), ("quarter", Quarter)], states, Now);

        Assert.Equal(Now.AddMinutes(10), earliest);
    }

    [Fact]
    public void Earliest_is_never_in_the_past()
    {
        var states = States(("a", Now.AddDays(-2)));

        Assert.Equal(Now, BackgroundTaskPlanner.Earliest([("a", Quarter)], states, Now));
    }
}
