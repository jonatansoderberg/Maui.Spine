using Android.Content;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

public static partial class SpineWidgetsExtensions
{
    private const string HandledExtra = "plugin.maui.spine.widgets.HANDLED";

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options)
    {
        builder.Services.AddSingleton<IWidgetPlatform, WidgetPlatform>();

        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android =>
        {
            // The link trampoline is an activity too; its stop and its intent are not the app's.
            android.OnCreate((activity, state) =>
            {
                if (activity is SpineWidgetLinkActivity) return;
                if (state is null) RefreshAllInBackground(Services());
                HandleLink(activity.Intent);
            });
            android.OnNewIntent((activity, intent) =>
            {
                if (activity is not SpineWidgetLinkActivity) HandleLink(intent);
            });
            // Same as iOS at foreground: the notification the user swiped away may have gone while the app was not running.
            android.OnResume(activity =>
            {
                if (activity is SpineWidgetLinkActivity) return;
                if (Services().GetService<ILiveActivityService>() is LiveActivityService activities) activities.Reconcile();
            });
            android.OnStop(activity =>
            {
                if (activity is SpineWidgetLinkActivity) return;
                if (options.RefreshOnBackground) RefreshAllInBackground(Services());
                if (WidgetStore.Kinds(activity).Length > 0) SpineBackgroundReceiver.Schedule(activity, options.BackgroundRefreshInterval);
            });
        }));

        static IServiceProvider Services() => IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The MAUI application has not started.");

        // The intent is re-delivered on every recreation of the activity, so a handled link is marked on it.
        static void HandleLink(Intent? intent)
        {
            if (intent?.Data?.ToString() is not { } value || intent.GetBooleanExtra(HandledExtra, false)) return;
            if (Uri.TryCreate(value, UriKind.Absolute, out var url) && TryHandleLink(Services(), url))
                intent.PutExtra(HandledExtra, true);
        }
    }
}
