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
    /// <exception cref="InvalidOperationException">No platform or no register was configured.</exception>
    public static IServiceCollection AddSpinePush(this IServiceCollection services, Action<SpinePushOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SpinePushOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(sp => options.StoreFactory!(sp));

        return services;
    }
}
