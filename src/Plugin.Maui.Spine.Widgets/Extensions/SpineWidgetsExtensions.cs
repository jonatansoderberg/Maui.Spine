using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

/// <summary>Registers Spine widgets and Live Activities with the MAUI application builder.</summary>
public static partial class SpineWidgetsExtensions
{
    /// <summary>
    /// Adds <see cref="IWidgetService"/> and <see cref="ILiveActivityService"/>, discovers
    /// <see cref="WidgetAttribute"/>-decorated providers in the Spine assemblies, and routes the
    /// widget open URL back to <see cref="IWidgetLinkHandler"/>. Call after <c>UseSpine</c>.
    /// </summary>
    public static MauiAppBuilder UseSpineWidgets(this MauiAppBuilder builder, Action<SpineWidgetsOptions>? configure = null)
    {
        var options = new SpineWidgetsOptions();
        configure?.Invoke(options);

        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<WidgetRegistry>();
        services.AddSingleton<IWidgetService, WidgetService>();
        services.AddSingleton<ILiveActivityService, LiveActivityService>();

        ConfigurePlatform(builder, options);

        return builder;
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options);

    /// <summary>Runs <see cref="IWidgetService.RefreshAllAsync"/> without blocking the caller; failures are logged.</summary>
    internal static void RefreshAllInBackground(IServiceProvider services)
    {
        var widgets = services.GetRequiredService<IWidgetService>();
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        widgets.RefreshAllAsync().SafeFireAndForget(e => logger.LogError(e, "Background widget refresh failed."));
    }

    /// <summary>
    /// Routes <c>&lt;ApplicationId&gt;://widget/&lt;kind&gt;</c> to the provider's
    /// <see cref="IWidgetLinkHandler"/>. Returns <see langword="false"/> when the URL is not a widget link.
    /// </summary>
    internal static bool TryHandleLink(IServiceProvider services, Uri url)
    {
        if (!string.Equals(url.Host, "widget", StringComparison.OrdinalIgnoreCase)) return false;
        var kind = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/'));
        if (kind.Length == 0) return false;

        var registry = services.GetRequiredService<WidgetRegistry>();
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        if (registry.ProviderTypeFor(kind) is not { } providerType)
        {
            logger.LogWarning("Opened from widget \"{Kind}\" but no provider is registered for it.", kind);
            return true;
        }

        if (!typeof(IWidgetLinkHandler).IsAssignableFrom(providerType))
            return true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var handler = (IWidgetLinkHandler)ActivatorUtilities.CreateInstance(services, providerType);
            handler.OnWidgetOpenedAsync(new WidgetLink(kind, url))
                .SafeFireAndForget(e => logger.LogError(e, "Handling the open of widget \"{Kind}\" failed.", kind));
        });
        return true;
    }
}
