namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Work the app does without being on screen, declared with <see cref="BackgroundTaskAttribute"/>.
/// Created through DI in a scope of its own for every run, on the thread pool.
/// </summary>
/// <remarks>
/// A run may happen while no window exists: fetch, store, refresh widgets and Live Activities, plan
/// local notifications — but do not navigate, show dialogs or ask for permissions.
/// </remarks>
public interface IBackgroundTask
{
    /// <summary>
    /// Does the work. <paramref name="cancellationToken"/> is cancelled when the platform's time runs out —
    /// about 30 seconds for a scheduled run on iOS — so honour it: a task that does not is killed with
    /// the process.
    /// </summary>
    Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken);
}

/// <summary>One run of a background task.</summary>
/// <param name="Name">The task's name.</param>
/// <param name="Trigger">Why it runs.</param>
/// <param name="LastCompleted">When the last run that completed ended, or <see langword="null"/> if none has.</param>
public sealed record BackgroundTaskRun(string Name, BackgroundTaskTrigger Trigger, DateTimeOffset? LastCompleted);

/// <summary>Why a background task runs.</summary>
public enum BackgroundTaskTrigger
{
    /// <summary>The platform's schedule: a <c>BGTask</c> launch on iOS, a job on Android, the timer on Windows and Mac Catalyst.</summary>
    Scheduled,

    /// <summary>The app asked with <see cref="IBackgroundTasks.RequestAsync(string, CancellationToken)"/>.</summary>
    Requested,

    /// <summary>A push asked for it.</summary>
    Push,

    /// <summary>The app came to the foreground and the task was due.</summary>
    CatchUp,
}
