using Microsoft.Extensions.Logging;
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

        if (options.HandlerType is { } handler)
            services.AddTransient(typeof(IPushHandler), handler);

        ConfigurePlatform(builder, options);

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

    internal static IServiceProvider Services() => IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("The MAUI application has not started.");
}
