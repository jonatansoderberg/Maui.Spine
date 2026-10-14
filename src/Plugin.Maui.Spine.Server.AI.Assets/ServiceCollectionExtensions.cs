using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>Registers generated assets.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds generated assets for the image features declared in <paramref name="configure"/>. Needs
    /// <c>AddSpineAI</c> with the same <paramref name="section"/>, and Spine messaging with
    /// <see cref="GeneratedAssetMessagingExtensions.AddGeneratedAssets"/>.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="section">The <c>Ai</c> section; each feature's asset settings are under <c>Features:&lt;name&gt;:Asset</c>.</param>
    /// <param name="configure">The features, their prompts and checks, and the store.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">No store was chosen, or no feature makes assets.</exception>
    public static IServiceCollection AddSpineAIAssets(this IServiceCollection services, IConfiguration section, Action<SpineAIAssetsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SpineAIAssetsOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(new AssetSettings(section));
        services.AddSingleton(sp => options.StoreFactory!(sp));
        services.AddSingleton<GeneratedAssets>();
        services.AddSingleton<IGeneratedAssets>(sp => sp.GetRequiredService<GeneratedAssets>());
        services.AddHttpClient(GenerateAssetHandler.HttpClientName);
        foreach (var asset in options.Assets.Values)
            asset.Register(services);

        services.AddHostedService<AssetsStartup>();
        return services;
    }

    /// <summary>Checks the setup and provisions the store, so a mistake shows when the host starts rather than at the first order.</summary>
    private sealed class AssetsStartup(IServiceProvider services, SpineAIAssetsOptions options, AssetSettings settings, IGeneratedAssetStore store, ILogger<GeneratedAssets> logger)
        : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (services.GetService<IAIFeatures>() is null)
                throw new InvalidOperationException("AddSpineAIAssets needs AddSpineAI: the images are made by its features.");
            if (services.GetService<GeneratedAssetQueue>() is not { } queue || services.GetService<IMessagePublisher>() is null)
                throw new InvalidOperationException("AddSpineAIAssets needs Spine messaging with the assets' subscriber: call AddGeneratedAssets() in AddSpineMessaging.");

            // A window shorter than the retries lets a lookup order again while the first order is still being made.
            if (options.OrderWindow < queue.LongestRun)
                logger.LogWarning("Generated assets: OrderWindow ({Window}) is shorter than an order can take with {Attempts} attempts (about {Run}), so an image may be made twice. Raise OrderWindow or lower the attempts.",
                    options.OrderWindow, queue.Attempts, queue.LongestRun);

            foreach (var asset in options.Assets.Values)
            {
                var config = settings.For(asset.Feature);
                _ = AssetPaths.ExtensionOf(config.MediaType);

                if (!string.Equals(settings.KindOf(asset.Feature), nameof(AIFeatureKind.Image), StringComparison.OrdinalIgnoreCase))
                    logger.LogError("Generated assets: the feature '{Feature}' is not an image feature. Set Ai:Features:{Feature}:Kind to Image, or nothing is ever made.", asset.Feature, asset.Feature);
            }

            await store.ProvisionAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>Declares the assets' messages and subscriber on Spine messaging.</summary>
public static class GeneratedAssetMessagingExtensions
{
    /// <summary>The subscriber's default name.</summary>
    public const string DefaultSubscriber = "spine-ai-assets";

    /// <summary>
    /// Declares the order message and the subscriber that makes the images. Call it in <c>AddSpineMessaging</c> in
    /// every host that orders or makes images, then run the subscriber with <c>AddSpineMessagePump</c> or a trigger.
    /// </summary>
    /// <param name="builder">The messaging declaration.</param>
    /// <param name="subscriber">The subscriber's name, which is also its queue's.</param>
    /// <param name="attempts">Attempts per order, the first included; a failure is shown to the app after the last.</param>
    /// <param name="maxConcurrency">Images one pump makes at a time.</param>
    /// <returns>The same builder.</returns>
    public static MessagingBuilder AddGeneratedAssets(this MessagingBuilder builder, string subscriber = DefaultSubscriber, int attempts = 3, int maxConcurrency = 2)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var queue = new GeneratedAssetQueue(subscriber, attempts, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
        builder.Message<GenerateAsset>("spine.ai.asset");
        builder.Subscriber(subscriber, s => s
            .Handles<GenerateAsset>()
            .Retry(attempts, queue.FirstDelay, queue.MaxDelay)
            .MaxConcurrency(maxConcurrency));
        builder.Services.AddSingleton(queue);
        builder.Services.AddMessageHandler<GenerateAsset, GenerateAssetHandler>();
        return builder;
    }
}
