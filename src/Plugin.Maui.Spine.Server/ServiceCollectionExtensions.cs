using Microsoft.Extensions.DependencyInjection;

namespace Plugin.Maui.Spine.Server;

/// <summary>Registers Spine.Push on the server.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the register and the transports Spine.Push sends through. Call it once in the host's
    /// service configuration.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">Credentials, register, and the tag policy.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// No register was configured, or a configured platform is missing a credential. A server with no
    /// platform at all is allowed: it can register devices, and sending simply reaches nobody.
    /// </exception>
    public static IServiceCollection AddSpinePush(this IServiceCollection services, Action<SpinePushOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SpinePushOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(sp => options.StoreFactory!(sp));
        if (options.AppleOptions is { } apple)
        {
            services.AddSingleton<IPushTransport>(sp => new ApnsTransport(apple, sp.GetService<TimeProvider>()));
            services.AddSingleton<IPushChannels>(sp => new ApnsChannels(apple, sp.GetService<TimeProvider>()));
        }

        if (options.AndroidOptions is { } android)
            services.AddSingleton<IPushTransport>(_ => new FcmTransport(android));

        if (options.WindowsOptions is { } windows)
            services.AddSingleton<IPushTransport>(sp => new WnsTransport(windows, sp.GetService<TimeProvider>()));

        services.AddSingleton<IPushSender>(sp => new PushSender(
            sp.GetRequiredService<IPushInstallationStore>(),
            sp.GetServices<IPushTransport>(),
            options,
            sp.GetService<TimeProvider>()));

        return services;
    }
}
