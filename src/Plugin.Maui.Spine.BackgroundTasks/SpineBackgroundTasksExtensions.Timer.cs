#if MACCATALYST || WINDOWS

using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks;

// No scheduler launches the app here: BackgroundTaskBuilder on Windows needs an MSIX package, and on
// Mac Catalyst BGTaskScheduler never launches the app. So the tasks run while the app does: once at
// start, then whenever one is due, checked every minute.
public static partial class SpineBackgroundTasksExtensions
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineBackgroundTasksOptions options) =>
        builder.Services.AddSingleton<IMauiInitializeService>(new TimerStarter(options));

    private sealed class TimerStarter(SpineBackgroundTasksOptions options) : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => _ = RunAsync(services);

        private async Task RunAsync(IServiceProvider services)
        {
            var tasks = services.GetRequiredService<BackgroundTaskService>();
            var logger = services.GetRequiredService<ILogger<IBackgroundTasks>>();

            // The first run waits for the window; the app's start has better use for the first seconds.
            await Task.Delay(TimeSpan.FromSeconds(5));
            var trigger = options.CatchUp ? BackgroundTaskTrigger.CatchUp : BackgroundTaskTrigger.Scheduled;
            using var timer = new PeriodicTimer(Tick);
            do
            {
                try { await tasks.RunDueAsync(static _ => true, trigger, CancellationToken.None); }
                catch (Exception e) { logger.LogError(e, "Running the due background tasks failed."); }
                trigger = BackgroundTaskTrigger.Scheduled;
            }
            while (await timer.WaitForNextTickAsync());
        }
    }
}

#endif
