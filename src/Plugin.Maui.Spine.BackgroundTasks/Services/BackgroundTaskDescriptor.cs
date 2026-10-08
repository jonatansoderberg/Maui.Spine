using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>A task as the dispatcher sees it: the app's <see cref="BackgroundTaskAttribute"/> classes and Spine's own alike.</summary>
internal sealed record BackgroundTaskDescriptor(
    string Name,
    Type? TaskType,
    Func<TimeSpan> DefaultInterval,
    Func<IServiceProvider, BackgroundTaskRun, CancellationToken, Task> RunAsync)
{
    public bool RequiresNetwork { get; init; }
    public bool RequiresCharging { get; init; }
    public bool Long { get; init; }
    public IReadOnlyList<string> Widgets { get; init; } = [];
}
