namespace Plugin.Maui.Spine.Common;

/// <summary>
/// The app's background tasks, registered by <c>Plugin.Maui.Spine.BackgroundTasks</c>. Other Spine
/// packages reach it with <c>GetService</c>, so they work with or without it.
/// </summary>
public interface IBackgroundTasks
{
    /// <summary>
    /// Whether the platform runs tasks while the app is closed: <see langword="true"/> on iOS and Android,
    /// <see langword="false"/> on Windows and Mac Catalyst, where tasks run only while the app does.
    /// </summary>
    bool RunsWhileClosed { get; }

    /// <summary>Every task's name, the app's and Spine's own (such as <c>spine.widgets</c>), in order.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Runs <paramref name="name"/> now, in this process, and completes when it has run. A run of the same
    /// task already in progress is joined rather than started twice. A failing task is logged and shows in
    /// <see cref="StatusOf"/>; it does not throw here.
    /// </summary>
    /// <exception cref="ArgumentException">No task is named <paramref name="name"/>.</exception>
    Task RequestAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="name"/> now as <see cref="RequestAsync(string, CancellationToken)"/> does, with
    /// <paramref name="trigger"/> as its <see cref="BackgroundTaskRun.Trigger"/>. Spine.PushNotifications uses
    /// it for <see cref="PushKeys.Task"/> with <see cref="BackgroundTaskTrigger.Push"/>.
    /// </summary>
    /// <exception cref="ArgumentException">No task is named <paramref name="name"/>.</exception>
    Task RequestAsync(string name, BackgroundTaskTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>Runs the task <typeparamref name="TTask"/> now, as <see cref="RequestAsync(string, CancellationToken)"/>.</summary>
    Task RequestAsync<TTask>(CancellationToken cancellationToken = default) where TTask : IBackgroundTask;

    /// <summary>When <paramref name="name"/> last ran, how it went and when it is next due: the answer to "why is my widget old?".</summary>
    /// <exception cref="ArgumentException">No task is named <paramref name="name"/>.</exception>
    BackgroundTaskStatus StatusOf(string name);

    /// <summary>Raised with the task's name when a run starts or ends, on whatever thread ran it.</summary>
    event Action<string>? StatusChanged;
}

/// <summary>What is known about a background task's runs. Kept across launches.</summary>
/// <param name="Name">The task's name.</param>
/// <param name="Interval">The current interval between scheduled runs; <see cref="TimeSpan.Zero"/> when it runs only on request.</param>
/// <param name="IsRunning">Whether a run is in progress in this process.</param>
/// <param name="LastStarted">When the last run started.</param>
/// <param name="LastEnded">When the last run ended, whatever its outcome.</param>
/// <param name="LastCompleted">When the last run that completed ended.</param>
/// <param name="LastOutcome">How the last run ended.</param>
/// <param name="LastTrigger">Why the last run ran.</param>
/// <param name="LastError">The last failure's exception type and message, or <see langword="null"/> after a completed run.</param>
/// <param name="NextDue">
/// When the task is next due; the platform may run it later, never earlier. <see langword="null"/> for a
/// task that runs only on request.
/// </param>
public sealed record BackgroundTaskStatus(
    string Name,
    TimeSpan Interval,
    bool IsRunning,
    DateTimeOffset? LastStarted,
    DateTimeOffset? LastEnded,
    DateTimeOffset? LastCompleted,
    BackgroundTaskOutcome LastOutcome,
    BackgroundTaskTrigger? LastTrigger,
    string? LastError,
    DateTimeOffset? NextDue);

/// <summary>How a background task's run ended.</summary>
public enum BackgroundTaskOutcome
{
    /// <summary>It has not run yet.</summary>
    None,

    /// <summary>It ran to the end.</summary>
    Completed,

    /// <summary>It threw.</summary>
    Failed,

    /// <summary>Its time ran out, or the run was cancelled, before it ended.</summary>
    Cancelled,
}
