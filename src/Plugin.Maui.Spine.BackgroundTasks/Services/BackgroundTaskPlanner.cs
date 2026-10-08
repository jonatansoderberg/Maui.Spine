namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>
/// Which tasks are due and when the next one is. A task is due an interval after its last run
/// started, whatever that run's outcome, so a failing task is retried on its schedule rather than in
/// a loop; one that has never run is due at once. A task without an interval is never due.
/// </summary>
internal static class BackgroundTaskPlanner
{
    public static DateTimeOffset? NextDue(TimeSpan interval, BackgroundTaskState? state, DateTimeOffset now) =>
        interval <= TimeSpan.Zero ? null
        : state?.LastStarted is { } started ? started + interval
        : now;

    /// <summary>
    /// Whether a run the platform starts on its own clock (an Android job) is skipped: it is when the
    /// task started less than half of <paramref name="period"/> ago, by any trigger. A run that comes
    /// seconds after another (JobScheduler starts a newly booked job at once, right after the catch-up
    /// on activation) is dropped, while the job's own next period, never much shorter than
    /// <paramref name="period"/>, still runs. A start dated in the future (the clock was set back)
    /// skips nothing.
    /// </summary>
    public static bool SkipsScheduledRun(TimeSpan period, BackgroundTaskState? state, DateTimeOffset now) =>
        period > TimeSpan.Zero
        && state?.LastStarted is { } started
        && started <= now
        && now - started < period / 2;

    /// <summary>The tasks due at <paramref name="now"/>, the most overdue first.</summary>
    public static IReadOnlyList<string> Due(
        IEnumerable<(string Name, TimeSpan Interval)> tasks,
        IReadOnlyDictionary<string, BackgroundTaskState> states,
        DateTimeOffset now) =>
        [.. tasks
            .Select(t => (t.Name, Due: NextDue(t.Interval, states.GetValueOrDefault(t.Name), now)))
            .Where(t => t.Due <= now)
            .OrderBy(t => t.Due)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => t.Name)];

    /// <summary>
    /// When the platform should next run one of <paramref name="tasks"/>, never before
    /// <paramref name="now"/>; <see langword="null"/> when none has an interval.
    /// </summary>
    public static DateTimeOffset? Earliest(
        IEnumerable<(string Name, TimeSpan Interval)> tasks,
        IReadOnlyDictionary<string, BackgroundTaskState> states,
        DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        foreach (var (name, interval) in tasks)
        {
            if (NextDue(interval, states.GetValueOrDefault(name), now) is not { } due) continue;
            if (earliest is null || due < earliest) earliest = due;
        }

        return earliest is { } at && at < now ? now : earliest;
    }
}
