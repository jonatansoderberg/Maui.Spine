using AsyncAwaitBestPractices;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Push.Services;

namespace Plugin.Maui.Spine.Push.Extensions;

public static partial class SpinePushExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpinePushOptions options)
    {
        var platform = new AndroidPushPlatform();
        builder.Services.AddSingleton<IPushPlatform>(platform);
        builder.Services.AddSingleton<ILocalNotificationService, AndroidLocalNotifications>();

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            android.OnApplicationCreate(application =>
                PushNotifications.CreateChannels(application, [.. options.Channels]));

            android.OnCreate((activity, _) =>
            {
                SpinePushLifecycle.IsForeground = true;
                Start(options).SafeFireAndForget();
                Opened(activity.Intent).SafeFireAndForget();
            });

            // A notification tapped while the app is already running arrives as a new intent.
            android.OnNewIntent((_, intent) => Opened(intent).SafeFireAndForget());

            android.OnResume(_ =>
            {
                SpinePushLifecycle.IsForeground = true;
                Resume(options).SafeFireAndForget();
            });

            android.OnPause(_ => SpinePushLifecycle.IsForeground = false);
        }));
    }

    private static async Task Start(SpinePushOptions options)
    {
        if (options.Permission is PushPermission.AtLaunch or PushPermission.Provisional)
        {
            await Services().GetRequiredService<IPushService>().RequestPermissionAsync();
            return;
        }

        await Resume(options);
    }

    /// <summary>
    /// Asks Firebase for a token and registers — unless there is no backend to register with, which
    /// is what an app that only schedules local notifications looks like. Asking anyway would mean a
    /// warning about a missing <c>google-services.json</c> on every launch, for a token nobody wants.
    /// </summary>
    private static async Task Resume(SpinePushOptions options)
    {
        if (options.Backend is null) return;

        await AndroidPushPlatform.FetchTokenAsync();
        await Services().GetRequiredService<IPushService>().RefreshAsync();
    }

    private static async Task Opened(Android.Content.Intent? intent)
    {
        if (PushNotifications.Read(intent) is not { } message) return;
        await MainThread.InvokeOnMainThreadAsync(() => OpenedAsync(Services(), message, action: null));
    }
}
