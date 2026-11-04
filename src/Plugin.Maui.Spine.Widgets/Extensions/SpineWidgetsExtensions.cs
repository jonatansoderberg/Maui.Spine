using AsyncAwaitBestPractices;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Widgets.Services;

namespace Plugin.Maui.Spine.Widgets.Extensions;

/// <summary>Registers Spine widgets and Live Activities with the MAUI application builder.</summary>
public static partial class SpineWidgetsExtensions
{
    /// <summary>
    /// Adds <see cref="IWidgetService"/>, <see cref="ILiveActivityService"/> and <see cref="IControlService"/>, discovers
    /// <see cref="WidgetAttribute"/>- and <see cref="ControlAttribute"/>-decorated providers in the Spine assemblies, and routes the
    /// widget open URL back to <see cref="IWidgetLinkHandler"/>. <c>UseSpine()</c> calls it for an app
    /// that references this package; call it yourself only to configure the options, before or after
    /// <c>UseSpine()</c>. The first call registers the services; every call applies its
    /// <paramref name="configure"/> to the same options instance.
    /// </summary>
    public static MauiAppBuilder UseSpineWidgets(this MauiAppBuilder builder, Action<SpineWidgetsOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(SpineWidgetsOptions) && !d.IsKeyedService)?.ImplementationInstance
            is not SpineWidgetsOptions options)
        {
            options = new SpineWidgetsOptions
            {
                // With Spine.PushNotifications registered first; it turns this on itself when it comes second.
                LiveActivityPushTokens = services.Any(static d => d.ServiceType == typeof(PushNotificationsRegistered)),
            };

            services.AddSingleton(options);
            services.AddSingleton<WidgetRegistry>();
            services.AddSingleton<WidgetIconAssets>();
            services.AddSingleton<IWidgetService, WidgetService>();
            services.AddSingleton<ILiveActivityService, LiveActivityService>();
            services.AddSingleton<IControlService, ControlService>();

            // The background refresh as a task of Plugin.Maui.Spine.BackgroundTasks, when the app has it:
            // iOS allows one pending refresh request per app, so it cannot keep a schedule of its own beside
            // that package's. Without the package nobody reads this, and the platform code below books it.
            services.AddSingleton(new SpineBackgroundTaskRegistration(WidgetsTask, () => options.BackgroundRefreshInterval,
                static (services, _, cancellationToken) => RunBackgroundRefreshAsync(services, cancellationToken)));

            ConfigurePlatform(builder, options);
        }

        configure?.Invoke(options);

        if (options.BackgroundRefreshHandler is { } handler)
            services.Replace(ServiceDescriptor.Transient(typeof(IBackgroundRefreshHandler), handler));

        return builder;
    }

    /// <summary>The name of the widgets' background refresh among the tasks of Plugin.Maui.Spine.BackgroundTasks.</summary>
    internal const string WidgetsTask = "spine.widgets";

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpineWidgetsOptions options);

    /// <summary>Whether Plugin.Maui.Spine.BackgroundTasks runs the background refresh as its task <see cref="WidgetsTask"/>.</summary>
    internal static bool BackgroundTasksOwnRefresh(IServiceProvider services) => services.GetService<IBackgroundTasks>() is not null;

    /// <summary>
    /// One background run: the app's <see cref="IBackgroundRefreshHandler"/> first, if registered, then
    /// every widget and control. A failing handler is logged and does not stop them from being rebuilt.
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
        await services.GetRequiredService<IControlService>().RefreshAllAsync(cancellationToken);
    }

    /// <summary>
    /// A tapped <see cref="W.Button"/>: the provider's <see cref="IWidgetActionHandler"/> on the main thread,
    /// then the widget rebuilt so the tap's effect shows. A provider without a handler only gets the rebuild.
    /// </summary>
    internal static async Task HandleActionAsync(IServiceProvider services, string kind, string actionId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var registry = services.GetRequiredService<WidgetRegistry>();
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        if (registry.ProviderTypeFor(kind) is not { } providerType)
        {
            // Not a widget's kind: a Live Activity's, whose buttons go through the same intent and broadcast.
            await HandleActivityActionAsync(services, kind, actionId, at, logger);
            return;
        }

        if (typeof(IWidgetActionHandler).IsAssignableFrom(providerType))
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var handler = (IWidgetActionHandler)ActivatorUtilities.CreateInstance(services, providerType);
                    return handler.OnActionAsync(new WidgetAction(kind, actionId, at));
                });
            }
            catch (Exception e)
            {
                logger.LogError(e, "Handling action \"{Action}\" of widget \"{Kind}\" failed.", actionId, kind);
            }
        }

        await services.GetRequiredService<IWidgetService>().RefreshAsync(kind, cancellationToken);
    }

    /// <summary>
    /// A tapped button of a Live Activity: <see cref="LiveActivityActions.End"/> ends it; anything else goes to the app's
    /// <see cref="ILiveActivityActionHandler"/> on the main thread.
    /// </summary>
    private static async Task HandleActivityActionAsync(IServiceProvider services, string kind, string actionId, DateTimeOffset at, ILogger logger)
    {
        if (actionId == LiveActivityActions.End)
        {
            if (services.GetService<ILiveActivityService>()?.Active.FirstOrDefault(a => a.Kind == kind) is { } activity)
                await activity.EndAsync();
            return;
        }
        if (services.GetService<ILiveActivityActionHandler>() is not { } handler)
        {
            logger.LogWarning("\"{Kind}\" sent action \"{Action}\", but it is no widget's kind and the app registers no ILiveActivityActionHandler.", kind, actionId);
            return;
        }
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => handler.OnActionAsync(new LiveActivityAction(kind, actionId, at)));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Handling action \"{Action}\" of Live Activity \"{Kind}\" failed.", actionId, kind);
        }
    }

    /// <summary>
    /// A tapped control: the provider's <see cref="IControlProvider.OnActionAsync"/> on the main thread, then
    /// the control rebuilt, so what the tap changed shows — also when the handler refused it.
    /// </summary>
    internal static async Task HandleControlActionAsync(IServiceProvider services, string kind, bool? isOn, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var registry = services.GetRequiredService<WidgetRegistry>();
        var logger = services.GetRequiredService<ILogger<IControlService>>();
        if (registry.ControlProviderTypeFor(kind) is not { } providerType)
        {
            logger.LogWarning("Control \"{Kind}\" was tapped but no [Control] provider is registered for it.", kind);
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var provider = (IControlProvider)ActivatorUtilities.CreateInstance(services, providerType);
                return provider.OnActionAsync(new ControlAction(kind, isOn, at));
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Handling the tap on control \"{Kind}\" failed.", kind);
        }

        await services.GetRequiredService<IControlService>().RefreshAsync(kind, cancellationToken);
    }

    /// <summary>Runs <see cref="IWidgetService.RefreshAllAsync"/> and <see cref="IControlService.RefreshAllAsync"/> without blocking the caller; failures are logged.</summary>
    internal static void RefreshAllInBackground(IServiceProvider services)
    {
        var widgets = services.GetRequiredService<IWidgetService>();
        var controls = services.GetRequiredService<IControlService>();
        var logger = services.GetRequiredService<ILogger<IWidgetService>>();
        var options = services.GetRequiredService<SpineWidgetsOptions>();
        if (options.Templates.Count > 0)
        {
            try { services.GetRequiredService<IWidgetPlatform>().InstallTemplates([.. options.Templates]); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logger.LogError(e, "Installing the Live Activity templates failed."); }
        }
        // A pushed template draws its icons from the start, as a pushed layout does those listed.
        if (options.Icons.Concat(options.Templates.SelectMany(t => t.Icons)).Distinct(StringComparer.Ordinal).ToList() is { Count: > 0 } icons)
            services.GetRequiredService<WidgetIconAssets>().EnsureAsync(icons, CancellationToken.None)
                .SafeFireAndForget(e => logger.LogError(e, "Drawing the widget icons failed."));
        widgets.RefreshAllAsync().SafeFireAndForget(e => logger.LogError(e, "Background widget refresh failed."));
        controls.RefreshAllAsync().SafeFireAndForget(e => logger.LogError(e, "Background control refresh failed."));
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
        {
            // No handler of its own: open the page the options name, if any.
            if (services.GetRequiredService<SpineWidgetsOptions>().OpenPage is { } page)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    SetRoot(services.GetRequiredService<INavigationService>(), page)
                        .SafeFireAndForget(e => logger.LogError(e, "Opening {Page} from widget \"{Kind}\" failed.", page.Name, kind)));
            }

            return true;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var handler = (IWidgetLinkHandler)ActivatorUtilities.CreateInstance(services, providerType);
            handler.OnWidgetOpenedAsync(new WidgetLink(kind, url))
                .SafeFireAndForget(e => logger.LogError(e, "Handling the open of widget \"{Kind}\" failed.", kind));
        });
        return true;
    }

    private static Task SetRoot(INavigationService navigation, Type page) =>
        (Task)typeof(INavigationService).GetMethod(nameof(INavigationService.SetRootAsync))!
            .MakeGenericMethod(page)
            .Invoke(navigation, null)!;

    /// <summary>
    /// Opens the app at <typeparamref name="TPage"/> when a widget is tapped and its provider has no
    /// <see cref="IWidgetLinkHandler"/> of its own.
    /// </summary>
    public static SpineWidgetsOptions OpenWith<TPage>(this SpineWidgetsOptions options) where TPage : INavigable
    {
        options.OpenPage = typeof(TPage);
        return options;
    }
}
