using AsyncAwaitBestPractices;
using Microsoft.Windows.AppLifecycle;
using Plugin.Maui.Spine.Core;
using System.Diagnostics;
using System.Text;
using Windows.ApplicationModel.Activation;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineOptions options)
    {
        if (!options.Windows.AllowMultipleInstances)
        {
            var keyInstance = AppInstance.FindOrRegisterForKey("main");

            if (!keyInstance.IsCurrent)
            {
                _ = keyInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Process.GetCurrentProcess().Kill();
            }

            keyInstance.Activated += OnAppInstanceActivated;
        }
    }

    private static void OnAppInstanceActivated(object? sender, AppActivationArguments args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (args.Data is ILaunchActivatedEventArgs launchArgs)
                HandleActivationArguments(launchArgs.Arguments);
        });
    }

    private static void HandleActivationArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return;

        const string AppActionPrefix = "XE_APP_ACTIONS-"; //Same as: maui/src/Essentials/src/AppActions/AppActions.windows.cs

        if (!arguments.Contains(AppActionPrefix))
            return;

        var encoded = arguments.Substring(arguments.IndexOf(AppActionPrefix) + AppActionPrefix.Length);
        var actionId = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

        if (IPlatformApplication.Current?.Services.GetService(typeof(IShortcutHandler)) is IShortcutHandler shortcutHandler)
            shortcutHandler.InvokeAsync(actionId).SafeFireAndForget();
    }
}
