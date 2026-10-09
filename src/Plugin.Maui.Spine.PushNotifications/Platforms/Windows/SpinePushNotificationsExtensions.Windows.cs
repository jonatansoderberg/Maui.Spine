using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.PushNotifications;
using Plugin.Maui.Spine.PushNotifications.Services;
using Windows.ApplicationModel.Activation;
using static Plugin.Maui.Spine.PushNotifications.WindowsLog;

namespace Plugin.Maui.Spine.PushNotifications.Extensions;

public static partial class SpinePushNotificationsExtensions
{
    private static bool _registered;
    private static bool _listening;

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpinePushNotificationsOptions options)
    {
        var platform = new WindowsPushPlatform(options);
        builder.Services.AddSingleton<IPushPlatform>(platform);
        builder.Services.AddSingleton<ILocalNotificationService>(new WindowsLocalNotifications(options));

        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows =>
        {
            windows.OnLaunched((_, _) => Start(platform, options));

            windows.OnActivated((_, args) =>
            {
                platform.IsForeground = args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated;

                // MAUI activates the first window inside MauiWinUIApplication.OnLaunched, before it raises the
                // OnLaunched event that registers; Start asks for the channel itself once it has.
                if (platform.IsForeground && platform.IsRegistered) ResumeAsync(platform, options).SafeFireAndForget();
            });
        }));
    }

    /// <summary>
    /// In the order the Windows App SDK requires: every handler before its manager's <c>Register()</c>,
    /// and both before the activation arguments are read — a cold start from a toast or a raw push is in them.
    /// </summary>
    private static void Start(WindowsPushPlatform platform, SpinePushNotificationsOptions options)
    {
        // MAUI raises OnLaunched again for a later launch of a running app; the Windows App SDK wants one registration.
        // Each Register() logs its own failure, and a second call would only add a second handler.
        if (!_registered)
        {
            _registered = true;
            RegisterToasts(options);
            platform.Register();
        }

        ListenForActivations(platform, options);
        ResumeAsync(platform, options).SafeFireAndForget();
    }

    /// <summary>
    /// The activation the process started with, and later ones redirected here by Spine's single-instance
    /// handling: a toast tapped while the app ran in another process, or a raw push that started one.
    /// A failure is logged and tried again on the next launch rather than taking the app down with it.
    /// </summary>
    private static void ListenForActivations(WindowsPushPlatform platform, SpinePushNotificationsOptions options)
    {
        if (_listening) return;

        AppInstance instance;
        try
        {
            instance = AppInstance.GetCurrent();
            instance.Activated += (_, args) => Activated(platform, options, args, isColdStart: false);
        }
        catch (Exception e)
        {
            Logger?.LogError(e,
                "Spine.PushNotifications: AppInstance.GetCurrent() failed (HRESULT 0x{HResult:X8}); a toast that starts the app, or is tapped while it runs in another process, does not reach the handler.",
                e.HResult);
            return;
        }

        _listening = true;

        AppActivationArguments arguments;
        try
        {
            arguments = instance.GetActivatedEventArgs();
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Spine.PushNotifications: AppInstance.GetActivatedEventArgs() failed (HRESULT 0x{HResult:X8}); the toast or push that started the app is not handled.", e.HResult);
            return;
        }

        Activated(platform, options, arguments, isColdStart: true);
    }

    private static async Task ResumeAsync(WindowsPushPlatform platform, SpinePushNotificationsOptions options)
    {
        if (options.Backend is null || platform.Unsupported is not null) return;

        await platform.ChannelAsync();
        await Services().GetRequiredService<IPushNotificationService>().RefreshAsync();
    }

    /// <summary>
    /// Makes tapped toasts reach the app — WNS's and the app's own alike. A packaged app also needs the
    /// activator in its manifest, without which <c>Register()</c> fails; that is logged, not thrown.
    /// </summary>
    private static void RegisterToasts(SpinePushNotificationsOptions options)
    {
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                Logger?.LogWarning("Spine.PushNotifications: AppNotificationManager.IsSupported() is false; tapped notifications will not reach the handler.");
                return;
            }

            AppNotificationManager.Default.NotificationInvoked += (_, args) => Invoked(options, args.Argument, args.UserInput);
            AppNotificationManager.Default.Register();
        }
        catch (Exception e)
        {
            Logger?.LogError(e,
                "Spine.PushNotifications: AppNotificationManager.Register() failed (HRESULT 0x{HResult:X8}); tapped notifications will not reach the handler. " +
                "A packaged app needs the toast activator in Package.appxmanifest — see the wiki's Windows section.", e.HResult);
        }
    }

    private static void Activated(WindowsPushPlatform platform, SpinePushNotificationsOptions options, AppActivationArguments args, bool isColdStart)
    {
        try
        {
            switch (args.Data)
            {
                case AppNotificationActivatedEventArgs tapped:
                    Invoked(options, tapped.Argument, tapped.UserInput);
                    break;

                // A packaged app without the Windows App SDK's toast activator is started the classic way.
                case IToastNotificationActivatedEventArgs classic:
                    Invoked(options, classic.Argument, classic.UserInput.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? ""));
                    break;

                case PushNotificationReceivedEventArgs push:
                    platform.Receive(push, isColdStart);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "Spine.PushNotifications: handling the {Kind} activation failed (HRESULT 0x{HResult:X8}).", args.Kind, e.HResult);
        }
    }

    /// <summary>
    /// A toast or one of its buttons was tapped. A declared button that does its work in the background
    /// goes to <see cref="IPushNotificationHandler.OnActionAsync"/>; everything else opens the app.
    /// </summary>
    private static void Invoked(SpinePushNotificationsOptions options, string? argument, IDictionary<string, string>? userInput)
    {
        var (message, action, text) = WindowsNotifications.Read(argument, userInput);

        if (action is not null && FindAction(options, message.Category, action) is { RunsInBackground: true } button)
        {
            ActionAsync(Services(), message, button.Id, text).SafeFireAndForget();
            return;
        }

        // Posted rather than run inline: on a cold start this is called from OnLaunched, and the handler
        // should navigate once the first page is up.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // A toast tapped while the app is behind other windows does not bring it forward by itself.
            (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.Activate();
            OpenedAsync(Services(), message, action).SafeFireAndForget();
        });
    }
}
