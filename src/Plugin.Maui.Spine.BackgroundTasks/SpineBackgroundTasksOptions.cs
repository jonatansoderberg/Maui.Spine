using System.Collections.Concurrent;

namespace Plugin.Maui.Spine.BackgroundTasks;

/// <summary>Options for <see cref="SpineBackgroundTasksExtensions.UseSpineBackgroundTasks"/>.</summary>
public sealed class SpineBackgroundTasksOptions
{
    private readonly ConcurrentDictionary<string, TimeSpan> _intervals = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs the tasks that are due when the app comes to the foreground (default <see langword="true"/>).
    /// On iOS and Android a safety net for runs the platform did not grant; on Windows and Mac Catalyst
    /// the first run after the app starts.
    /// </summary>
    public bool CatchUp { get; set; } = true;

    /// <summary>
    /// Replaces the <c>IntervalMinutes</c> of the task named <paramref name="name"/>, for example from a
    /// user setting. <see cref="TimeSpan.Zero"/> turns its scheduled runs off; requests still run it.
    /// Read whenever Spine books the next run, so a change made later applies from then on.
    /// </summary>
    public SpineBackgroundTasksOptions Interval(string name, TimeSpan interval)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);
        _intervals[name] = interval;
        return this;
    }

    internal TimeSpan? IntervalFor(string name) => _intervals.TryGetValue(name, out var interval) ? interval : null;
}
