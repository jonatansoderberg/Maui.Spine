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

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            android.OnApplicationCreate(application =>
                PushNotifications.CreateChannels(application, [.. options.Channels]));

            android.OnCreate((activity, _) =>
            {
                SpinePushLifecycle.IsForeground = true;
                Start(platform, options).SafeFireAndForget();
                Opened(activity.Intent).SafeFireAndForget();
            });

            // A notification tapped while the app is already running arrives as a new intent.
            android.OnNewIntent((_, intent) => Opened(intent).SafeFireAndForget());

            android.OnResume(_ =>
            {
                SpinePushLifecycle.IsForeground = true;
                Resume().SafeFireAndForget();
            });

            android.OnPause(_ => SpinePushLifecycle.IsForeground = false);
        }));
    }

    private static async Task Start(AndroidPushPlatform platform, SpinePushOptions options)
    {
        if (options.Permission is PushPermission.AtLaunch or PushPermission.Provisional)
        {
            await Services().GetRequiredService<IPushService>().RequestPermissionAsync();
            return;
        }

        await AndroidPushPlatform.FetchTokenAsync();
        await Services().GetRequiredService<IPushService>().RefreshAsync();
    }

    private static async Task Resume()
    {
        await AndroidPushPlatform.FetchTokenAsync();
        await Services().GetRequiredService<IPushService>().RefreshAsync();
    }

    private static async Task Opened(Android.Content.Intent? intent)
    {
        if (PushNotifications.Read(intent) is not { } message) return;
        await MainThread.InvokeOnMainThreadAsync(() => OpenedAsync(Services(), message, action: null));
    }
}
