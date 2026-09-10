using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;
using Plugin.Maui.Spine.Push.Services;

namespace Plugin.Maui.Spine.Push.Extensions;

/// <summary>Registers Spine.Push with the MAUI application builder.</summary>
public static partial class SpinePushExtensions
{
    /// <summary>
    /// Adds <see cref="IPushService"/>, wires the platform's push callbacks, and registers the app's
    /// <see cref="IPushHandler"/>. Call after <c>UseSpine</c>.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="configure">The backend, the permission policy, the channels and the handler.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// On iOS and Mac Catalyst the app must also call <c>SpinePush.Install()</c> in its
    /// <c>Program.Main</c>, before <c>UIApplication.Main</c> — see the wiki. UIKit reads which
    /// callbacks the delegate implements when the delegate is assigned, which is too early for this
    /// method to be the one that adds them.
    /// </remarks>
    public static MauiAppBuilder UseSpinePush(this MauiAppBuilder builder, Action<SpinePushOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new SpinePushOptions();
        configure?.Invoke(options);

        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<PushRegistrationClient>();
        services.AddSingleton<IPushService>(sp => new PushService(
            sp.GetRequiredService<IPushPlatform>(),
            sp.GetRequiredService<PushRegistrationClient>(),
            options,
            sp,
            sp.GetRequiredService<ILogger<PushService>>(),
            sp.GetService<TimeProvider>()));

        // When the app also uses Plugin.Maui.Spine.Widgets, Live Activity push tokens are what make
        // UpdateLiveActivityAsync work from a server, so they are on by default once both are present.
        // UseSpineWidgets has to run first for this to see it; the wiki says so.
        foreach (var registered in services)
        {
            if (registered.ServiceType != typeof(SpineWidgetsOptions)) continue;
            if (registered.ImplementationInstance is SpineWidgetsOptions widgets) widgets.LiveActivityPushTokens = true;
        }

        if (options.HandlerType is { } handler)
            services.AddTransient(typeof(IPushHandler), handler);

        ConfigurePlatform(builder, options);

        // Windows has no implementation in v1, and neither will any platform added to the TFM list
        // before its platform layer exists. TryAdd after ConfigurePlatform means the real one wins
        // wherever there is one, and the rule is in the code rather than in the order of two calls.
        services.TryAddSingleton<IPushPlatform, UnsupportedPushPlatform>();
        services.TryAddSingleton<ILocalNotificationService, UnsupportedLocalNotifications>();

        return builder;
    }

    static partial void ConfigurePlatform(MauiAppBuilder builder, SpinePushOptions options);

    /// <summary>
    /// One received message: the app's handler decides what to show. Without a handler the system
    /// shows what it would have shown anyway.
    /// </summary>
    internal static async Task<PushPresentation> DeliverAsync(
        IServiceProvider services, PushMessage message, PushContext context)
    {
        var logger = services.GetRequiredService<ILogger<IPushService>>();

        if (services.GetService<IPushHandler>() is not { } handler)
            return DefaultPresentation;

        try
        {
            return await handler.OnReceivedAsync(message, context);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Spine.Push: the handler threw on a received message.");
            return DefaultPresentation;
        }
    }

    /// <summary>
    /// What the system shows in the foreground when the app has no handler: the same thing it shows
    /// in the background, which is the least surprising default.
    /// </summary>
    internal const PushPresentation DefaultPresentation =
        PushPresentation.Banner | PushPresentation.Sound | PushPresentation.List;

    /// <summary>The user opened a notification. Runs on the main thread.</summary>
    internal static async Task OpenedAsync(IServiceProvider services, PushMessage message, string? action)
    {
        var logger = services.GetRequiredService<ILogger<IPushService>>();

        if (services.GetService<IPushHandler>() is not { } handler) return;

        try
        {
            await handler.OnOpenedAsync(message, action);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Spine.Push: the handler threw on an opened notification.");
        }
    }

    /// <summary>
    /// A button that does not open the app, or a reply. Runs without UI; the caller holds on to the
    /// platform's background time until this returns, so the process is not frozen mid-way.
    /// </summary>
    internal static async Task ActionAsync(IServiceProvider services, PushMessage message, string action, string? text)
    {
        var logger = services.GetRequiredService<ILogger<IPushService>>();

        if (services.GetService<IPushHandler>() is not { } handler)
        {
            logger.LogWarning("Spine.Push: button '{Action}' was tapped, but the app registered no IPushHandler to run it.", action);
            return;
        }

        try
        {
            await handler.OnActionAsync(message, action, text);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Spine.Push: the handler threw on button '{Action}'.", action);
        }
    }

    /// <summary>The declared button <paramref name="action"/> in <paramref name="category"/>, when there is one.</summary>
    internal static PushAction? FindAction(SpinePushOptions options, string? category, string action) =>
        options.Categories.FirstOrDefault(c => c.Id == category)?.Actions.FirstOrDefault(a => a.Id == action);

    /// <summary>
    /// Messages Spine handles itself: a widget rebuild, and a Live Activity on the platforms that
    /// render one in the app's own process. Both services live in <c>Plugin.Maui.Spine.Common</c>, so
    /// this package works whether or not the app also uses <c>Plugin.Maui.Spine.Widgets</c> — without
    /// it they are simply not registered and nothing happens.
    /// </summary>
    /// <returns><see langword="true"/> when Spine dealt with the message and nothing should be shown.</returns>
    internal static async Task<bool> HandleInternallyAsync(
        IServiceProvider services, PushMessage message, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILogger<IPushService>>();

        try
        {
            switch (message.Kind)
            {
                case PushKind.Widget when services.GetService<IWidgetService>() is { } widgets:
                    if (message.Data.GetValueOrDefault(PushKeys.Widget) is { Length: > 0 } kind)
                        await widgets.RefreshAsync(kind, cancellationToken);
                    else
                        await widgets.RefreshAllAsync(cancellationToken);
                    return true;

                case PushKind.LiveActivity when services.GetService<ILiveActivityService>() is { } activities:
                    return await LiveActivityAsync(activities, message, cancellationToken);

                default:
                    return false;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Spine.Push: handling a {Kind} message failed.", message.Kind);
            return false;
        }
    }

    private static async Task<bool> LiveActivityAsync(
        ILiveActivityService activities, PushMessage message, CancellationToken cancellationToken)
    {
        if (message.Data.GetValueOrDefault(PushKeys.Activity) is not { Length: > 0 } kind) return false;

        var running = activities.Active.FirstOrDefault(a => a.Kind == kind && !a.IsEnded);

        switch (message.Data.GetValueOrDefault("spine.event"))
        {
            case "end":
                if (running is not null) await running.EndAsync();
                return true;

            case "start" when running is null:
                if (Layout(message) is { } starting)
                    await activities.StartAsync(kind, starting, StaleAt(message));
                return true;

            default:
                // An update for an activity that is not running is the same situation as a start, and
                // the sender cannot know which it is: the device may have been restarted since.
                if (Layout(message) is not { } layout) return false;

                if (running is null) await activities.StartAsync(kind, layout, StaleAt(message));
                else await running.UpdateAsync(layout, StaleAt(message));

                return true;
        }

        static LiveActivityLayout? Layout(PushMessage message) =>
            message.Data.GetValueOrDefault(PushKeys.Layout) is { Length: > 0 } json
                ? WidgetJson.DeserializeLayout(json)
                : null;

        static DateTimeOffset? StaleAt(PushMessage message) =>
            message.Data.GetValueOrDefault("spine.stale") is { Length: > 0 } value &&
            long.TryParse(value, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : null;
    }

    internal static IServiceProvider Services() => IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI application has not started.");
}
