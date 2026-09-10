#if IOS || MACCATALYST
using AsyncAwaitBestPractices;
using Foundation;
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
                UNUserNotificationCenter.Current.Delegate = new NotificationDelegate(options);
                RegisterCategories(options);

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

        // No backend, or no push in the build, is what an app that only schedules local
        // notifications looks like: it has nobody to register with, and asking APNs for a token it
        // cannot use only earns a failure in the log. On Mac Catalyst this is the default — push
        // there follows the signing, see Plugin.Maui.Spine.Push.targets.
        if (options.Backend is null || !ApplePushPlatform.IsRemoteConfigured) return;

        if (platform.Status is PushStatus.Authorized or PushStatus.Provisional)
        {
            await MainThread.InvokeOnMainThreadAsync(application.RegisterForRemoteNotifications);
            await Services().GetRequiredService<IPushService>().RefreshAsync();
        }
    }

    private static async Task Resume(ApplePushPlatform platform, SpinePushOptions options)
    {
        await platform.RefreshStatusAsync();

        if (options.Backend is null || !ApplePushPlatform.IsRemoteConfigured) return;

        await Services().GetRequiredService<IPushService>().RefreshAsync();
    }

    /// <summary>
    /// Tells iOS which buttons each category has. It must happen before a notification naming one
    /// arrives — iOS shows no buttons for a category it has not been told about, and says nothing — so
    /// it is done at launch beside the delegate, not when a notification is scheduled.
    /// </summary>
    private static void RegisterCategories(SpinePushOptions options)
    {
        if (options.Categories.Count == 0) return;

        var categories = options.Categories
            .Select(category => UNNotificationCategory.FromIdentifier(
                category.Id, [.. category.Actions.Select(Button)], [], UNNotificationCategoryOptions.None))
            .ToArray();

        UNUserNotificationCenter.Current.SetNotificationCategories(new NSSet<UNNotificationCategory>(categories));

        static UNNotificationAction Button(PushAction action)
        {
            var flags = UNNotificationActionOptions.None;
            if (!action.RunsInBackground) flags |= UNNotificationActionOptions.Foreground;
            if (action.Destructive) flags |= UNNotificationActionOptions.Destructive;

            return action.Reply is { } placeholder
                ? UNTextInputNotificationAction.FromIdentifier(action.Id, action.Title, flags, action.Title, placeholder)
                : UNNotificationAction.FromIdentifier(action.Id, action.Title, flags);
        }
    }

    private static void WarnAboutMethodsTheAppOwns()
    {
        if (SpinePush.NotInstalled.Count == 0) return;

        Services().GetRequiredService<ILogger<IPushService>>().LogWarning(
            "Spine.Push: the app's AppDelegate already implements {Selectors}, so Spine did not add them. " +
            "Call the matching SpinePush.Forward member from each, or push will not reach the handler.",
            string.Join(", ", SpinePush.NotInstalled));
    }

    private sealed class NotificationDelegate(SpinePushOptions options) : UNUserNotificationCenterDelegate
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
            var content = response.Notification.Request.Content;
            var message = PushPayload.Read(content.UserInfo);

            // The default action means the notification itself was tapped, not one of its buttons.
            // The constant is spelled out because .NET iOS keeps UNNotificationActionIdentifier
            // internal; this is the value Apple documents for UNNotificationDefaultActionIdentifier.
            var action = response.ActionIdentifier == "com.apple.UNNotificationDefaultActionIdentifier"
                ? null
                : response.ActionIdentifier;

            if (action is not null && FindAction(options, content.CategoryIdentifier, action) is { RunsInBackground: true } button)
            {
                // iOS keeps the process running until the completion handler is called, so for a button
                // that does its work in the background it is called when the work is done — not at once,
                // as for a notification that opens the app.
                RunAsync().SafeFireAndForget();
                return;

                async Task RunAsync()
                {
                    try
                    {
                        await ActionAsync(Services(), message, button.Id, (response as UNTextInputNotificationResponse)?.UserText);
                    }
                    finally
                    {
                        completionHandler();
                    }
                }
            }

            completionHandler();
            MainThread.InvokeOnMainThreadAsync(() => OpenedAsync(Services(), message, action)).SafeFireAndForget();
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
#endif
