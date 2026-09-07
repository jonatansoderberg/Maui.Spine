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
        services.AddSingleton<WidgetIconAssets>();
        services.AddSingleton<IWidgetService, WidgetService>();
        services.AddSingleton<ILiveActivityService, LiveActivityService>();
        if (options.BackgroundRefreshHandler is { } handler)
            services.AddTransient(typeof(IBackgroundRefreshHandler), handler);

        ConfigurePlatform(builder, options);

        return builder;
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options);

    /// <summary>
    /// One background run: the app's <see cref="IBackgroundRefreshHandler"/> first, if registered, then
    /// every widget. A failing handler is logged and does not stop the widgets from being rebuilt.
    /// </summary>
    internal static async Task RunBackgroundRefreshAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        if (services.GetService<IBackgroundRefreshHandler>() is { } handler)
        {
            try { await handler.RefreshAsync(cancellationToken); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogError(e, "The background refresh handler failed."); }
        }
        await services.GetRequiredService<IWidgetService>().RefreshAllAsync(cancellationToken);
    }

    /// <summary>
    /// A tapped <see cref="W.Button"/>: the provider's <see cref="IWidgetActionHandler"/> on the main thread,
    /// then the widget rebuilt so the tap's effect shows. A provider without a handler only gets the rebuild.
    /// </summary>
    internal static async Task HandleActionAsync(IServiceProvider services, string kind, string actionId)
    {
        var registry = services.GetRequiredService<WidgetRegistry>();
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        if (registry.ProviderTypeFor(kind) is not { } providerType)
        {
            logger.LogWarning("Widget \"{Kind}\" sent action \"{Action}\" but no provider is registered for it.", kind, actionId);
            return;
        }

        if (typeof(IWidgetActionHandler).IsAssignableFrom(providerType))
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var handler = (IWidgetActionHandler)ActivatorUtilities.CreateInstance(services, providerType);
                    return handler.OnActionAsync(new WidgetAction(kind, actionId));
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Handling action \"{Action}\" of widget \"{Kind}\" failed.", actionId, kind);
            }
        }

        await services.GetRequiredService<IWidgetService>().RefreshAsync(kind);
    }

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
