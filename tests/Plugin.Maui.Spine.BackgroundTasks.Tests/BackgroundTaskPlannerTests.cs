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

    [Fact]
    public void A_scheduled_run_seconds_after_the_catch_up_is_skipped()
    {
        // First launch: the catch-up ran the task, then JobScheduler starts the newly booked job.
        var state = new BackgroundTaskState { LastStarted = Now.AddSeconds(-3), LastTrigger = BackgroundTaskTrigger.CatchUp };

        Assert.True(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, state, Now));
    }

    [Fact]
    public void A_scheduled_run_that_never_ran_or_is_a_period_on_runs()
    {
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, null, Now));
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState(), Now));
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState { LastStarted = Now.AddMinutes(-15) }, Now));
    }

    [Fact]
    public void A_scheduled_run_is_skipped_for_less_than_half_its_period()
    {
        // A job may come a little early in its window, so a run that is not quite due still runs.
        Assert.True(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState { LastStarted = Now.AddMinutes(-7) }, Now));
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState { LastStarted = Now.AddMinutes(-7.5) }, Now));
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState { LastStarted = Now.AddMinutes(-12) }, Now));
    }

    [Fact]
    public void The_job_first_then_the_catch_up_runs_the_task_once()
    {
        var states = new Dictionary<string, BackgroundTaskState>
        {
            ["a"] = new() { LastStarted = Now.AddSeconds(-3), LastTrigger = BackgroundTaskTrigger.Scheduled },
        };

        Assert.Empty(BackgroundTaskPlanner.Due([("a", Quarter)], states, Now));
    }

    [Fact]
    public void A_start_in_the_future_skips_no_scheduled_run()
    {
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(Quarter, new BackgroundTaskState { LastStarted = Now.AddHours(1) }, Now));
    }

    [Fact]
    public void Without_a_period_nothing_is_skipped()
    {
        Assert.False(BackgroundTaskPlanner.SkipsScheduledRun(TimeSpan.Zero, new BackgroundTaskState { LastStarted = Now }, Now));
    }
}
