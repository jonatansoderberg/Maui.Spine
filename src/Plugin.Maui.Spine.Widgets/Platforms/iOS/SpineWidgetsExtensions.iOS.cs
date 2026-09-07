using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

public static partial class SpineWidgetsExtensions
{
    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<IWidgetPlatform, WidgetPlatform>();

        builder.ConfigureLifecycleEvents(events => events.AddiOS(ios =>
        {
            // Once at launch so a freshly installed widget has content, then every time the app is
            // backgrounded so the home screen shows the state the user just left.
            ios.FinishedLaunching((_, _) =>
            {
                RefreshAllInBackground(Services());
                return true;
            });
            if (options.RefreshOnBackground)
                ios.DidEnterBackground(_ => RefreshAllInBackground(Services()));

            ios.OpenUrl((_, url, _) => HandleLink(url));
            ios.SceneOpenUrl((_, contexts) =>
            {
                var handled = false;
                foreach (var context in contexts)
                    handled |= HandleLink(context.Url);
                return handled;
            });
        }));

        static IServiceProvider Services() => IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The MAUI application has not started.");

        static bool HandleLink(Foundation.NSUrl url) =>
            url.AbsoluteString is { } value
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && TryHandleLink(Services(), uri);
    }
}
