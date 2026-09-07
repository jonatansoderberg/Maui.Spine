using BackgroundTasks;
using CoreFoundation;
using Foundation;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

public static partial class SpineWidgetsExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<IWidgetPlatform, WidgetPlatform>();

        // The identifier the build wrote into Info.plist; the task can only be registered for one listed there.
        var refreshTask = NSBundle.MainBundle.ObjectForInfoDictionary("BGTaskSchedulerPermittedIdentifiers") is NSArray permitted
            ? Enumerable.Range(0, (int)permitted.Count).Select(i => permitted.GetItem<NSString>((nuint)i).ToString()).FirstOrDefault(id => id.EndsWith(".spine-widgets.refresh", StringComparison.Ordinal))
            : null;
        var background = refreshTask is not null && options.BackgroundRefreshInterval > TimeSpan.Zero;

        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios =>
        {
            // Once at launch so a freshly installed widget has content, then every time the app is
            // backgrounded so the home screen shows the state the user just left.
            ios.FinishedLaunching((_, _) =>
            {
                if (background) RegisterBackgroundRefresh(refreshTask!, options.BackgroundRefreshInterval);
                ListenForActions();
                DrainActions(Services());
                RefreshAllInBackground(Services());
                return true;
            });
            ios.DidEnterBackground(_ =>
            {
                if (options.RefreshOnBackground) RefreshAllInBackground(Services());
                if (background) ScheduleBackgroundRefresh(refreshTask!, options.BackgroundRefreshInterval);
            });
            ios.WillEnterForeground(_ => DrainActions(Services()));

            ios.OpenUrl((_, url, _) => HandleLink(url));
            ios.SceneOpenUrl((_, contexts) =>
            {
                var handled = false;
                foreach (var context in contexts)
                    handled |= HandleLink(context.Url);
                return handled;
            });
        }));

        static bool HandleLink(Foundation.NSUrl url) =>
            url.AbsoluteString is { } value
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && TryHandleLink(Services(), uri);
    }

    private static IServiceProvider Services() => IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI application has not started.");

    // Button taps: the extension appends them to actions.jsonl in the container and posts a Darwin
    // notification. A running app handles them at once; otherwise the file waits for the next launch.
    private static void ListenForActions()
    {
        if (Services().GetRequiredService<IWidgetPlatform>() is not WidgetPlatform { ActionNotificationName: { } name }) return;
        CFNotificationCenter.Darwin.AddObserver(name, null!, (_, _) => DrainActions(Services()), CFNotificationSuspensionBehavior.DeliverImmediately);
    }

    private static void DrainActions(IServiceProvider services)
    {
        if (services.GetRequiredService<IWidgetPlatform>() is not WidgetPlatform platform) return;
        foreach (var (kind, actionId) in platform.TakeActions())
            HandleActionAsync(services, kind, actionId)
                .SafeFireAndForget(e => services.GetRequiredService<ILogger<IWidgetService>>().LogError(e, "Handling action \"{Action}\" of widget \"{Kind}\" failed.", actionId, kind));
    }

    // BGAppRefreshTask: registered before launch finishes (a hard requirement), booked whenever the app
    // goes to the background and again after each run, since a request is consumed by its run.
    private static void RegisterBackgroundRefresh(string identifier, TimeSpan interval)
    {
        var registered = BGTaskScheduler.Shared.Register(identifier, null, task =>
        {
            ScheduleBackgroundRefresh(identifier, interval);
            var cancellation = new CancellationTokenSource();
            task.ExpirationHandler = cancellation.Cancel;
            Task.Run(async () =>
            {
                try
                {
                    await RunBackgroundRefreshAsync(Services(), cancellation.Token);
                    task.SetTaskCompleted(true);
                }
                catch (Exception e)
                {
                    Services().GetRequiredService<ILogger<IWidgetService>>().LogError(e, "Background refresh failed.");
                    task.SetTaskCompleted(false);
                }
            });
        });
        if (!registered)
            Services().GetRequiredService<ILogger<IWidgetService>>().LogWarning("BGTaskScheduler refused to register {Identifier}.", identifier);
    }

    private static void ScheduleBackgroundRefresh(string identifier, TimeSpan interval)
    {
        var request = new BGAppRefreshTaskRequest(identifier) { EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(interval.TotalSeconds) };
        if (!BGTaskScheduler.Shared.Submit(request, out var error) && error is not null)
            Services().GetRequiredService<ILogger<IWidgetService>>().LogWarning("Background refresh could not be scheduled: {Error}", error.LocalizedDescription);
    }
}
