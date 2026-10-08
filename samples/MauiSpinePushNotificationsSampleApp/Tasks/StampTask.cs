using MauiSpinePushNotificationsSampleApp.Services;
using Plugin.Maui.Spine.Common;

namespace MauiSpinePushNotificationsSampleApp.Tasks;

/// <summary>
/// A background task a push can start: send a silent or widget push with <c>spine.task=stamp</c> in its
/// data, and the task runs in the push's time, then the sample widget is rebuilt. It also runs on the
/// platform's own schedule every 30 minutes. The Log page shows each run with what started it.
/// </summary>
[BackgroundTask("stamp", IntervalMinutes = 30, Widgets = ["sample"])]
public sealed class StampTask(PushLog log) : IBackgroundTask
{
    public Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken)
    {
        log.Note("task", $"{run.Name} ran ({run.Trigger}) at {DateTimeOffset.Now:HH:mm:ss}");
        return Task.CompletedTask;
    }
}
