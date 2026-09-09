using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Push.Services;
using UIKit;
using UserNotifications;

namespace Plugin.Maui.Spine.Push.Extensions;

public static partial class SpinePushExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpinePushOptions options)
    {
        var platform = new ApplePushPlatform(options);
        builder.Services.AddSingleton<IPushPlatform>(platform);
        builder.Services.AddSingleton<ILocalNotificationService, AppleLocalNotifications>();

        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios =>
        {
            ios.FinishedLaunching((application, _) =>
            {
                UNUserNotificationCenter.Current.Delegate = new NotificationDelegate();

                WarnAboutMethodsTheAppOwns();
                Start(platform, options, application).SafeFireAndForget();

                // Anything that arrived before the host existed — a cold start from a push.
                ApplePushPlatform.DrainPending();
                return true;
            });

            ios.WillEnterForeground(_ => Resume(platform, options).SafeFireAndForget());
        }));
    }

    private static async Task Start(ApplePushPlatform platform, SpinePushOptions options, UIApplication application)
    {
        await platform.RefreshStatusAsync();

        if (options.Permission is PushPermission.AtLaunch or PushPermission.Provisional &&
            platform.Status == PushStatus.NotDetermined)
        {
            await Services().GetRequiredService<IPushService>().RequestPermissionAsync();
            return;
        }

        // No backend is what an app that only schedules local notifications looks like: it has
        // nobody to register with, and asking APNs for a token it cannot use only earns a failure
        // in the log — and a build that needs the aps-environment entitlement.
        if (options.Backend is null) return;

        if (platform.Status is PushStatus.Authorized or PushStatus.Provisional)
        {
            await MainThread.InvokeOnMainThreadAsync(application.RegisterForRemoteNotifications);
            await Services().GetRequiredService<IPushService>().RefreshAsync();
        }
    }

    private static async Task Resume(ApplePushPlatform platform, SpinePushOptions options)
    {
        await platform.RefreshStatusAsync();

        if (options.Backend is null) return;

        await Services().GetRequiredService<IPushService>().RefreshAsync();
    }

    private static void WarnAboutMethodsTheAppOwns()
    {
        if (SpinePush.NotInstalled.Count == 0) return;

        Services().GetRequiredService<ILogger<IPushService>>().LogWarning(
            "Spine.Push: the app's AppDelegate already implements {Selectors}, so Spine did not add them. " +
            "Call the matching SpinePush.Forward member from each, or push will not reach the handler.",
            string.Join(", ", SpinePush.NotInstalled));
    }

    private sealed class NotificationDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
        {
            PresentAsync(notification, completionHandler).SafeFireAndForget();

            static async Task PresentAsync(UNNotification notification, Action<UNNotificationPresentationOptions> done)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));

                var message = PushPayload.Read(notification.Request.Content.UserInfo);
                var context = new PushContext(true, false, DateTimeOffset.UtcNow, deadline.Token);
                var presentation = await DeliverAsync(Services(), message, context);

                done(Translate(presentation));
            }
        }

        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            OpenAsync(response).SafeFireAndForget();
            completionHandler();

            static async Task OpenAsync(UNNotificationResponse response)
            {
                var message = PushPayload.Read(response.Notification.Request.Content.UserInfo);

                // The default action means the notification itself was tapped, not one of its buttons.
                // The constant is spelled out because .NET iOS keeps UNNotificationActionIdentifier
                // internal; this is the value Apple documents for UNNotificationDefaultActionIdentifier.
                var action = response.ActionIdentifier == "com.apple.UNNotificationDefaultActionIdentifier"
                    ? null
                    : response.ActionIdentifier;

                await MainThread.InvokeOnMainThreadAsync(() => OpenedAsync(Services(), message, action));
            }
        }

        private static UNNotificationPresentationOptions Translate(PushPresentation presentation)
        {
            var options = UNNotificationPresentationOptions.None;
            if (presentation.HasFlag(PushPresentation.Banner)) options |= UNNotificationPresentationOptions.Banner;
            if (presentation.HasFlag(PushPresentation.List)) options |= UNNotificationPresentationOptions.List;
            if (presentation.HasFlag(PushPresentation.Sound)) options |= UNNotificationPresentationOptions.Sound;
            if (presentation.HasFlag(PushPresentation.Badge)) options |= UNNotificationPresentationOptions.Badge;
            return options;
        }
    }
}
