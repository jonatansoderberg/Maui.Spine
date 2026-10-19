using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.AI;

/// <summary>Registers <c>Plugin.Maui.Spine.AI</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineAIExtensions
{
    static readonly SpineAIOptions Options = new() { Functions = SpineAIFunctions.App };

    /// <summary>
    /// Registers the app's AI features: each one is injected as <c>[FromKeyedServices("Name")] IChatClient</c>,
    /// <see cref="IAIUsage"/> reads what they used on the device, and <see cref="IAIAssets"/> (with
    /// <see cref="GeneratedImage"/>) looks up the backend's generated images. <c>UseSpine()</c> calls it for an app that references
    /// this package; call it yourself with the backend and the features, before or after <c>UseSpine()</c>. A second
    /// call applies its options and registers the features it adds.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseSpineAI(o =>
    /// {
    ///     o.Backend = new Uri("https://api.example.com/ai");
    ///     o.Feature("Assistant", f => f.OnDevice(_ => new AppleIntelligenceChatClient()).ThenServer());
    /// });
    /// </code>
    /// </example>
    public static MauiAppBuilder UseSpineAI(this MauiAppBuilder builder, Action<SpineAIOptions>? configure = null)
    {
        configure?.Invoke(Options);

        if (!builder.Services.Any(static s => s.ServiceType == typeof(SpineAIRuntime)))
        {
            builder.Services.TryAddSingleton(Options);
            builder.Services.AddSingleton(static services => new SpineAIRuntime(
                Options,
                InstallationIdAsync,
                new PreferencesAIDeviceUsageStore(),
                services.GetService<ILoggerFactory>(),
                services.GetService<TimeProvider>(),
                AppForeground.WhenActiveAsync));
            builder.Services.AddSingleton<IAIUsage>(static services => new AIUsage(services.GetRequiredService<SpineAIRuntime>()));
            builder.Services.AddSingleton<IAIAssets>(static services => new AIAssets(services.GetRequiredService<SpineAIRuntime>()));
        }

        foreach (var feature in Options.Features.Keys)
        {
            if (builder.Services.Any(s => s.IsKeyedService && s.ServiceType == typeof(IChatClient) && Equals(s.ServiceKey, feature))) continue;

            builder.Services.AddKeyedSingleton<IChatClient>(feature, (services, _) =>
                services.GetRequiredService<SpineAIRuntime>().Feature(feature, services)!);
        }

        return builder;
    }

    /// <summary>
    /// The installation id the server knows the app by. It is the push installation id when the app has
    /// Spine.PushNotifications, the same key in <see cref="SecureStorage"/>, so the server sees one id per installation;
    /// otherwise it is made here and push adopts it later.
    /// </summary>
    static async Task<string?> InstallationIdAsync(CancellationToken cancellationToken)
    {
        if (await SecureStorage.Default.GetAsync(SpineInstallation.IdKey) is { Length: > 0 } existing)
            return existing;

        var created = Guid.NewGuid().ToString("N");
        await SecureStorage.Default.SetAsync(SpineInstallation.IdKey, created);
        return created;
    }
}
