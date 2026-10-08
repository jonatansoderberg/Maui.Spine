using Android.App;
using Android.App.Job;
using Android.Runtime;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using System.Collections.Concurrent;

namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>
/// Runs a background task when JobScheduler starts its job, in the app's process — started for it when
/// the app is not running, after <c>MauiApplication.OnCreate</c> has built the services.
/// </summary>
[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
[Register("plugin/maui/spine/backgroundtasks/SpineBackgroundJobService")]
internal sealed class SpineBackgroundJobService : JobService
{
    internal const string NameKey = "spine.task";

    private readonly ConcurrentDictionary<int, CancellationTokenSource> _runs = new();

    public override bool OnStartJob(JobParameters? parameters)
    {
        if (parameters?.Extras?.GetString(NameKey) is not { } name) return false;
        if (IPlatformApplication.Current?.Services?.GetService<BackgroundTaskService>() is not { } tasks)
        {
            Android.Util.Log.Warn("Spine", $"Background task \"{name}\" was started before the MAUI application; it is retried later.");
            JobFinished(parameters, true);
            return false;
        }

        var logger = IPlatformApplication.Current.Services.GetRequiredService<ILogger<IBackgroundTasks>>();
        logger.LogInformation("JobScheduler started background task \"{Name}\" (job {Id}).", name, parameters.JobId);

        var cancellation = new CancellationTokenSource();
        _runs[parameters.JobId] = cancellation;
        Task.Run(async () =>
        {
            try { await tasks.RunScheduledAsync(name, cancellation.Token); }
            catch (Exception e) { logger.LogError(e, "Background task \"{Name}\" failed in its job.", name); }
            finally
            {
                // Once only: after OnStopJob the job is no longer the app's to finish.
                if (_runs.TryRemove(parameters.JobId, out _)) JobFinished(parameters, false);
                cancellation.Dispose();
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        if (parameters is null || !_runs.TryRemove(parameters.JobId, out var cancellation)) return false;
        cancellation.Cancel();
        // Stopped before it ended (a constraint went away, or its time ran out): run it again later.
        return true;
    }
}
