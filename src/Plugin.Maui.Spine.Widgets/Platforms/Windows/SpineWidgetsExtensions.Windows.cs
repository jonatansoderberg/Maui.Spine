using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

public static partial class SpineWidgetsExtensions
{
    private static bool _started;

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<NativeWidgetPlatform>();
        builder.Services.AddSingleton<IWidgetPlatform>(static services => services.GetRequiredService<NativeWidgetPlatform>());

        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows =>
        {
            // MAUI raises OnLaunched again for a later launch of a running app; the provider is registered once.
            windows.OnLaunched((_, _) => Start());
            // As on the other platforms, the widgets get the state the user just left.
            windows.OnVisibilityChanged((_, args) =>
            {
                if (!args.Visible && options.RefreshOnBackground) RefreshAllInBackground(Services());
            });
        }));

        static IServiceProvider Services() => IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The MAUI application has not started.");
    }

    /// <summary>
    /// The provider registered with COM first, since the board may have started the process for it and waits; then
    /// the widgets rebuilt. A tap on a widget reaches the provider as an action, so there is no link to read here.
    /// </summary>
    private static void Start()
    {
        if (_started) return;
        _started = true;

        var services = IPlatformApplication.Current!.Services;
        var platform = services.GetRequiredService<NativeWidgetPlatform>();
        if (platform.ClassId is { } classId)
        {
            WidgetBoardProvider.Instance.Platform = platform;
            WidgetBoardServer.Register(classId, platform.Logger);
        }

        RefreshAllInBackground(services);
    }
}
