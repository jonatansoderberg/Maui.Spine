using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>Registers Spine's AI features on the server.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the named features in <paramref name="section"/>, the providers they run on, the meter and the budgets.
    /// Call it once in the host's service configuration.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="section">The <c>Ai</c> section: providers, features, prices and the budget.</param>
    /// <param name="configure">Registers providers and the usage store, and sets the callbacks.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>App code asks <see cref="IAIFeatures"/> for a feature's client on behalf of a caller. Each feature named in
    /// <paramref name="section"/> at startup can also be injected as <c>[FromKeyedServices("Name")] IChatClient</c>
    /// (or <c>IImageGenerator</c>), which calls as <see cref="AICaller.System"/>.</para>
    /// <para>A feature that cannot run is logged at startup with the reason, and answers as "off".</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No usage store was chosen.</exception>
    public static IServiceCollection AddSpineAI(this IServiceCollection services, IConfiguration section, Action<SpineAIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        var options = new SpineAIOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddSingleton(sp => options.StoreFactory!(sp));

        services.AddSingleton(sp => new AIFeatureRegistry(section, options, sp, LoggerFactoryOf(sp)));
        services.AddSingleton(sp => new AIUsageMeter(
            sp.GetRequiredService<AIFeatureRegistry>(), sp.GetRequiredService<IAIUsageStore>(), TimeOf(sp), LoggerOf(sp)));
        services.AddSingleton(sp => new AIBudgetGate(
            sp.GetRequiredService<AIFeatureRegistry>(), sp.GetRequiredService<IAIUsageStore>(), options, sp,
            sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), TimeOf(sp), LoggerOf(sp)));
        services.AddSingleton(sp => new AIFeatures(
            sp.GetRequiredService<AIFeatureRegistry>(), sp.GetRequiredService<AIBudgetGate>(), sp.GetRequiredService<AIUsageMeter>(), options, sp));
        services.AddSingleton<IAIFeatures>(sp => sp.GetRequiredService<AIFeatures>());

        // Builds the features once at startup, so a missing key is in the log before the first call rather than after it.
        services.AddHostedService(sp => new StartupLog(sp.GetRequiredService<AIFeatureRegistry>()));

        foreach (var feature in section.GetSection("Features").GetChildren())
        {
            var name = feature.Key;

            if (string.Equals(feature["Kind"], nameof(AIFeatureKind.Image), StringComparison.OrdinalIgnoreCase))
                services.AddKeyedSingleton<IImageGenerator>(name, (sp, _) => new FeatureImageGenerator(name, sp.GetRequiredService<AIFeatures>()));
            else
                services.AddKeyedSingleton<IChatClient>(name, (sp, _) => new FeatureChatClient(name, sp.GetRequiredService<AIFeatures>()));
        }

        return services;
    }

    private static ILoggerFactory LoggerFactoryOf(IServiceProvider services) => services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

    private static ILogger LoggerOf(IServiceProvider services) => LoggerFactoryOf(services).CreateLogger("Plugin.Maui.Spine.Server.AI");

    private static TimeProvider TimeOf(IServiceProvider services) => services.GetService<TimeProvider>() ?? TimeProvider.System;

    private sealed class StartupLog(AIFeatureRegistry registry) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = registry.Current;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
