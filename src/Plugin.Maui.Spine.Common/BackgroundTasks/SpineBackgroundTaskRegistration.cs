namespace Plugin.Maui.Spine.Common;

/// <summary>
/// A background task a Spine package contributes itself, added to the services as a singleton. The
/// BackgroundTasks package runs it with the app's <see cref="BackgroundTaskAttribute"/> tasks; without
/// that package nobody reads it, so the contributing package keeps its own fallback.
/// </summary>
/// <param name="Name">The task's name, by convention <c>spine.&lt;package&gt;</c>.</param>
/// <param name="Interval">Read on every scheduling decision, so an option changed at run time applies.</param>
/// <param name="RunAsync">The work, given the run's scoped services.</param>
internal sealed record SpineBackgroundTaskRegistration(
    string Name,
    Func<TimeSpan> Interval,
    Func<IServiceProvider, BackgroundTaskRun, CancellationToken, Task> RunAsync)
{
    public bool RequiresNetwork { get; init; }
}
