using AsyncAwaitBestPractices;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.PushNotifications.Services;

namespace Plugin.Maui.Spine.PushNotifications.Extensions;

public static partial class SpinePushNotificationsExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpinePushNotificationsOptions options)
    {
        var platform = new AndroidPushPlatform();
        builder.Services.AddSingleton<IPushPlatform>(platform);
        builder.Services.AddSingleton<ILocalNotificationService, AndroidLocalNotifications>();

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            android.OnApplicationCreate(application =>
            {
                AndroidNotifications.CreateChannels(application, [.. options.Channels]);
                AndroidNotifications.UseCategories([.. options.Categories]);
            });

            android.OnCreate((activity, _) =>
            {
                SpinePushNotificationsLifecycle.IsForeground = true;
                Start(options).SafeFireAndForget();
                Opened(activity.Intent).SafeFireAndForget();
            });

            // A notification tapped while the app is already running arrives as a new intent.
            android.OnNewIntent((_, intent) => Opened(intent).SafeFireAndForget());

            android.OnResume(_ =>
            {
                SpinePushNotificationsLifecycle.IsForeground = true;
                Resume(options).SafeFireAndForget();
            });

            android.OnPause(_ => SpinePushNotificationsLifecycle.IsForeground = false);
        }));
    }

    private static async Task Start(SpinePushNotificationsOptions options)
    {
        if (options.Permission is PushPermission.AtLaunch or PushPermission.Provisional)
        {
            await Services().GetRequiredService<IPushNotificationService>().RequestPermissionAsync();
            return;
        }

        await Resume(options);
    }

    /// <summary>
    /// Asks Firebase for a token and registers — unless there is no backend to register with, which
    /// is what an app that only schedules local notifications looks like. Asking anyway would mean a
    /// warning about a missing <c>google-services.json</c> on every launch, for a token nobody wants.
    /// </summary>
    private static async Task Resume(SpinePushNotificationsOptions options)
    {
        if (options.Backend is null) return;

        await AndroidPushPlatform.FetchTokenAsync();
        await Services().GetRequiredService<IPushNotificationService>().RefreshAsync();
    }

    private static async Task Opened(Android.Content.Intent? intent)
    {
        if (AndroidNotifications.Read(intent) is not { } message) return;

        var action = intent!.GetStringExtra(AndroidNotifications.ActionExtra);

        // Auto-cancel takes the notification down for a tap on the notification itself, not for a tap
        // on one of its buttons — so a button that opened the app does it here.
        if (action is not null && intent.GetIntExtra(AndroidNotifications.NotificationIdExtra, 0) is var id and not 0)
            AndroidX.Core.App.NotificationManagerCompat.From(Platform.AppContext)?.Cancel(id);

        await MainThread.InvokeOnMainThreadAsync(() => OpenedAsync(Services(), message, action));
    }
}
