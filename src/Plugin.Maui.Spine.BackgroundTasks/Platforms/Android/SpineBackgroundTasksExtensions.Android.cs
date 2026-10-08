using Android.App.Job;
using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks;

public static partial class SpineBackgroundTasksExtensions
{
    // JobScheduler's floor for a periodic job.
    internal static readonly TimeSpan MinimumPeriod = TimeSpan.FromMinutes(15);

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineBackgroundTasksOptions options)
    {
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            // Checked on every return to the foreground, so an interval changed in the options is booked;
            // a job whose parameters did not change is left alone, since booking it again restarts its period.
            android.OnResume(activity =>
            {
                ScheduleJobs(activity);
                CatchUp(options);
            });
            android.OnStop(ScheduleJobs);
        }));
    }

    /// <summary>One periodic, persisted job per task with an interval; the jobs of tasks that no longer have one are cancelled.</summary>
    private static void ScheduleJobs(Context context)
    {
        if (IPlatformApplication.Current?.Services is not { } services) return;
        var tasks = services.GetRequiredService<BackgroundTaskService>();
        var logger = services.GetRequiredService<ILogger<IBackgroundTasks>>();
        var scheduler = (JobScheduler)context.GetSystemService(Context.JobSchedulerService)!;
        var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(SpineBackgroundJobService)));
        var pending = scheduler.AllPendingJobs
            .Where(j => j.Service?.ClassName == component.ClassName)
            .ToDictionary(j => j.Id);

        var wanted = new HashSet<int>();
        foreach (var task in tasks.Tasks)
        {
            var interval = tasks.IntervalOf(task);
            if (interval <= TimeSpan.Zero) continue;

            var id = JobId(task.Name);
            wanted.Add(id);
            var period = (long)(interval < MinimumPeriod ? MinimumPeriod : interval).TotalMilliseconds;
            var network = task.RequiresNetwork ? NetworkType.Any : NetworkType.None;

#pragma warning disable CA1422 // NetworkType is deprecated from API 28 in favour of NetworkRequest; it still reports what SetRequiredNetworkType set.
            if (pending.TryGetValue(id, out var job) && job.IntervalMillis == period && job.NetworkType == network
                && job.IsRequireCharging == task.RequiresCharging && job.IsPersisted && job.Extras?.GetString(SpineBackgroundJobService.NameKey) == task.Name)
                continue;
#pragma warning restore CA1422

            var extras = new PersistableBundle();
            extras.PutString(SpineBackgroundJobService.NameKey, task.Name);
            var builder = new JobInfo.Builder(id, component);
            builder.SetPeriodic(period);
            builder.SetPersisted(true);
            builder.SetRequiredNetworkType(network);
            builder.SetRequiresCharging(task.RequiresCharging);
            builder.SetExtras(extras);
            var info = builder.Build()!;

            if (scheduler.Schedule(info) == JobScheduler.ResultSuccess)
                logger.LogInformation("Booked background task \"{Name}\" as job {Id} every {Minutes} min.", task.Name, id, period / 60_000);
            else
                logger.LogWarning("JobScheduler refused background task \"{Name}\" (job {Id}).", task.Name, id);
        }

        foreach (var id in pending.Keys.Where(id => !wanted.Contains(id)))
        {
            scheduler.Cancel(id);
            logger.LogInformation("Cancelled job {Id}: no background task with an interval uses it now.", id);
        }
    }

    /// <summary>A job id that stays the same across launches and versions for the same task name.</summary>
    internal static int JobId(string name)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var c in "spine.task:" + name)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)(hash & int.MaxValue);
        }
    }
}
