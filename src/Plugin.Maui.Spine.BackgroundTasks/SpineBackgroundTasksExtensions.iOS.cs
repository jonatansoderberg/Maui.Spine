#if IOS

using BackgroundTasks;
using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.BackgroundTasks;

public static partial class SpineBackgroundTasksExtensions
{
    private const string RefreshSuffix = ".spine.refresh";
    private const string ProcessingSuffix = ".spine.processing";

    // iOS kills the app when an identifier is registered twice.
    private static bool _registered;

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineBackgroundTasksOptions options)
    {
        // The identifiers the build wrote into Info.plist. A handler can only be registered for one listed
        // there, and every one listed there must have a handler before launch finishes.
        var permitted = NSBundle.MainBundle.ObjectForInfoDictionary("BGTaskSchedulerPermittedIdentifiers") is NSArray array
            ? Enumerable.Range(0, (int)array.Count).Select(i => array.GetItem<NSString>((nuint)i).ToString()).ToList()
            : [];
        var refresh = permitted.FirstOrDefault(id => id.EndsWith(RefreshSuffix, StringComparison.Ordinal));
        var processing = permitted.FirstOrDefault(id => id.EndsWith(ProcessingSuffix, StringComparison.Ordinal));
        var identifiers = new Identifiers(refresh, processing);

        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios =>
        {
            // MAUI raises this inside didFinishLaunching, before it returns: the last moment iOS accepts a
            // registration, also when it launches the app in the background for the task itself.
            ios.FinishedLaunching((_, _) =>
            {
                Register(identifiers);
                return true;
            });
            ios.DidEnterBackground(_ => Schedule(identifiers));
            ios.SceneDidEnterBackground(_ => Schedule(identifiers));
            ios.OnActivated(_ => CatchUp(options));
            ios.SceneOnActivated(_ => CatchUp(options));
        }));
    }

    private sealed record Identifiers(string? Refresh, string? Processing);

    private static void Register(Identifiers identifiers)
    {
        if (_registered) return;
        _registered = true;

        var services = Services();
        var logger = services.GetRequiredService<ILogger<IBackgroundTasks>>();
        var tasks = services.GetRequiredService<BackgroundTaskService>();

        if (identifiers.Refresh is null)
        {
            logger.LogWarning("Info.plist has no BGTaskSchedulerPermittedIdentifiers entry ending in {Suffix}, so background tasks run only while the app does. Plugin.Maui.Spine.BackgroundTasks.targets writes it (SpineBackgroundTasksEnabled); an app that references the project rather than the package imports it explicitly, with Plugin.Maui.Spine.Common.targets.", RefreshSuffix);
            return;
        }

        if (identifiers.Processing is null && tasks.Tasks.FirstOrDefault(t => t.Long) is { } longTask)
            logger.LogWarning("Background task \"{Name}\" is Long, but the app has no processing identifier (SpineBackgroundTasksProcessing=true); it runs in the refresh's ~30 seconds instead.", longTask.Name);

        Register(identifiers, identifiers.Refresh, processing: false);
        if (identifiers.Processing is not null)
            Register(identifiers, identifiers.Processing, processing: true);
    }

    private static void Register(Identifiers identifiers, string identifier, bool processing)
    {
        var registered = BGTaskScheduler.Shared.Register(identifier, null, task => Launch(identifiers, task, processing));
        Services().GetRequiredService<ILogger<IBackgroundTasks>>().Log(registered ? LogLevel.Debug : LogLevel.Warning,
            registered ? "Registered {Identifier}." : "BGTaskScheduler refused to register {Identifier}.", identifier);
    }

    // The tasks the refresh runs: the short ones, and the long ones too when there is no processing identifier.
    private static Func<BackgroundTaskDescriptor, bool> Picks(Identifiers identifiers, bool processing) =>
        processing ? static t => t.Long
        : identifiers.Processing is null ? static _ => true
        : static t => !t.Long;

    private static void Launch(Identifiers identifiers, BGTask task, bool processing)
    {
        var services = Services();
        var logger = services.GetRequiredService<ILogger<IBackgroundTasks>>();
        var tasks = services.GetRequiredService<BackgroundTaskService>();
        logger.LogInformation("iOS launched {Identifier}.", task.Identifier);

        // Booked before the work, so a run the system cuts short still leaves the next one booked.
        Schedule(identifiers);

        var cancellation = new CancellationTokenSource();
        var completed = 0;
        void Complete(bool success)
        {
            if (Interlocked.Exchange(ref completed, 1) == 0) task.SetTaskCompleted(success);
        }

        task.ExpirationHandler = () =>
        {
            logger.LogWarning("iOS ended {Identifier}: its time ran out.", task.Identifier);
            cancellation.Cancel();
            // A task that ignores its token must not keep the completion from being sent.
            Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(_ => Complete(false), TaskScheduler.Default);
        };

        Task.Run(async () =>
        {
            var success = false;
            try
            {
                success = await tasks.RunDueAsync(Picks(identifiers, processing), BackgroundTaskTrigger.Scheduled, cancellation.Token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Running the background tasks for {Identifier} failed.", task.Identifier);
            }
            finally
            {
                Schedule(identifiers);
                Complete(success && !cancellation.IsCancellationRequested);
            }
        });
    }

    /// <summary>
    /// Books the next refresh (and processing) run at the earliest time a task is due, replacing the pending
    /// request, or cancels it when no task has an interval. iOS allows one pending refresh request per app,
    /// which is why every task shares it.
    /// </summary>
    private static void Schedule(Identifiers identifiers)
    {
        if (identifiers.Refresh is null || IPlatformApplication.Current?.Services is not { } services) return;
        var tasks = services.GetRequiredService<BackgroundTaskService>();
        var logger = services.GetRequiredService<ILogger<IBackgroundTasks>>();

        if (tasks.EarliestDue(Picks(identifiers, processing: false)) is { } refreshAt)
            Submit(new BGAppRefreshTaskRequest(identifiers.Refresh) { EarliestBeginDate = (NSDate)refreshAt.UtcDateTime }, logger);
        else
            BGTaskScheduler.Shared.Cancel(identifiers.Refresh);

        if (identifiers.Processing is null) return;
        var longTasks = tasks.Tasks.Where(t => t.Long && tasks.IntervalOf(t) > TimeSpan.Zero).ToList();
        if (tasks.EarliestDue(Picks(identifiers, processing: true)) is { } processingAt && longTasks.Count > 0)
        {
            Submit(new BGProcessingTaskRequest(identifiers.Processing)
            {
                EarliestBeginDate = (NSDate)processingAt.UtcDateTime,
                // One request carries every long task: a network if any needs one, a charger only if all do.
                RequiresNetworkConnectivity = longTasks.Any(t => t.RequiresNetwork),
                RequiresExternalPower = longTasks.All(t => t.RequiresCharging),
            }, logger);
        }
        else
        {
            BGTaskScheduler.Shared.Cancel(identifiers.Processing);
        }
    }

    private static void Submit(BGTaskRequest request, ILogger logger)
    {
        if (BGTaskScheduler.Shared.Submit(request, out var error))
            logger.LogDebug("Booked {Identifier} for {At}.", request.Identifier, request.EarliestBeginDate);
        else
            logger.LogWarning("{Identifier} could not be booked: {Error}", request.Identifier, error?.LocalizedDescription ?? "no reason given");
    }
}

#endif
