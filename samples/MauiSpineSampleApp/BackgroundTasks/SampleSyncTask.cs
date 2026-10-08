using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.BackgroundTasks;

/// <summary>
/// The Showcase's scheduled task: a pretend sync that stores when it ran, then has the sample widget
/// rebuilt, so the widget's "Built" time shows the run even with the app closed.
/// </summary>
[BackgroundTask(Name, IntervalMinutes = 15, Widgets = ["sample"])]
public sealed class SampleSyncTask : IBackgroundTask
{
    public const string Name = "sample-sync";
    public const string RunsKey = "sample-sync-runs";

    public async Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken)
    {
        // Stands in for a fetch: work that takes a moment and honours the token.
        await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        Preferences.Default.Set(RunsKey, Preferences.Default.Get(RunsKey, 0) + 1);
    }
}

/// <summary>
/// Runs only on request, and fails every other time: the status page shows the error it left, which is
/// what <see cref="IBackgroundTasks.StatusOf"/> is for.
/// </summary>
[BackgroundTask(Name)]
public sealed class FlakyUploadTask : IBackgroundTask
{
    public const string Name = "flaky-upload";
    private const string AttemptsKey = "flaky-upload-attempts";

    public async Task RunAsync(BackgroundTaskRun run, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var attempt = Preferences.Default.Get(AttemptsKey, 0) + 1;
        Preferences.Default.Set(AttemptsKey, attempt);
        if (attempt % 2 == 0)
            throw new HttpRequestException("503 on POST /uploads (the sample fails every other run)");
    }
}
