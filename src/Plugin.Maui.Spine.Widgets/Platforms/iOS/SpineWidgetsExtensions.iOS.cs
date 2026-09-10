using AsyncAwaitBestPractices;
using BackgroundTasks;
using CoreFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Common;
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
                ListenForActivities();
                ListenForWidgetPushToken();

                // At launch too, not only at foreground: an activity that ended while the app was not
                // running is found here, against what the app last knew, and announced.
                ReconcileActivities(Services());
                DrainActions(Services());
                RefreshAllInBackground(Services());
                return true;
            });
            ios.DidEnterBackground(_ =>
            {
                if (options.RefreshOnBackground) RefreshAllInBackground(Services());
                if (background) ScheduleBackgroundRefresh(refreshTask!, options.BackgroundRefreshInterval);
            });
            ios.WillEnterForeground(_ =>
            {
                DrainActions(Services());
                ReconcileActivities(Services());
            });

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

    // Button taps: the intent appends them to actions.jsonl in the container and posts a Darwin
    // notification. The intent runs in this process — iOS launches the app in the background for it
    // when it is not running — so the notification is what normally delivers a tap, and the drain at
    // launch covers one that arrived before the observer existed, or one recorded by the extension
    // on an iOS that ran the intent there.
    private static void ListenForActions()
    {
        if (Services().GetRequiredService<IWidgetPlatform>() is not WidgetPlatform { ActionNotificationName: { } name }) return;
        CFNotificationCenter.Darwin.AddObserver(name, null!, (_, _) => DrainActions(Services()), CFNotificationSuspensionBehavior.DeliverImmediately);
    }

    // An activity's fate is decided outside the app: a swipe on the Lock Screen, a push, its stale
    // date. The bridge watches ActivityKit and posts when one is gone; the reconcile at foreground
    // covers a notification that found the process suspended, which Darwin does not queue.
    private static void ListenForActivities()
    {
        if (Services().GetRequiredService<IWidgetPlatform>() is not WidgetPlatform { ActivityNotificationName: { } name } platform) return;
        CFNotificationCenter.Darwin.AddObserver(name, null!, (_, _) => ReconcileActivities(Services()), CFNotificationSuspensionBehavior.DeliverImmediately);
        platform.ObserveActivities();
    }

    // iOS 26's widget push token: fetched at launch, and again whenever the extension's push handler or
    // the bridge says it changed. The widget service passes the news on, so Spine.Push registers it.
    private static void ListenForWidgetPushToken()
    {
        if (Services().GetRequiredService<IWidgetPlatform>() is not WidgetPlatform { PushTokenNotificationName: { } name } platform) return;
        CFNotificationCenter.Darwin.AddObserver(name, null!, (_, _) =>
        {
            platform.RefreshWidgetPushToken();
            if (Services().GetService<IWidgetService>() is WidgetService widgets) widgets.OnPushTokenChanged();
        }, CFNotificationSuspensionBehavior.DeliverImmediately);
        platform.RefreshWidgetPushToken();
    }

    private static void ReconcileActivities(IServiceProvider services)
    {
        if (services.GetService<ILiveActivityService>() is LiveActivityService activities) activities.Reconcile();
    }

    private static void DrainActions(IServiceProvider services)
    {
        if (services.GetRequiredService<IWidgetPlatform>() is not WidgetPlatform platform) return;
        foreach (var action in platform.TakeActions())
            HandleRecordedActionAsync(services, platform, action)
                .SafeFireAndForget(e => services.GetRequiredService<ILogger<IWidgetService>>().LogError(e, "Handling action \"{Action}\" of widget \"{Kind}\" failed.", action.ActionId, action.Kind));
    }

    // The intent's perform() waits for the completion, so it is sent whatever the handler did: an
    // unanswered tap holds the process for the intent's full timeout.
    private static async Task HandleRecordedActionAsync(IServiceProvider services, WidgetPlatform platform, RecordedAction action)
    {
        var started = Environment.TickCount64;
        try { await HandleActionAsync(services, action.Kind, action.ActionId, action.At); }
        finally
        {
            if (action.Id is { } id) platform.CompleteAction(id);
            services.GetRequiredService<ILogger<IWidgetService>>().LogDebug("Handled action \"{Action}\" of widget \"{Kind}\" in {Elapsed} ms.", action.ActionId, action.Kind, Environment.TickCount64 - started);
        }
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
