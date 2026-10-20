using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>Registers Spine sign-in on the server.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the sign-in services and a JwtBearer scheme named <see cref="SpineAuthDefaults.AuthenticationScheme"/> for
    /// Spine's access tokens, as the default scheme, so the app's own endpoints use <c>[Authorize]</c>.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="configure">Sets the issuer, the signing key, the providers and the store.</param>
    /// <returns>The same services, for chaining.</returns>
    /// <exception cref="InvalidOperationException">Something required is missing; the message says what.</exception>
    public static IServiceCollection AddSpineAuthentication(this IServiceCollection services, Action<SpineAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SpineAuthOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddHttpClient(IdTokenValidator.HttpClientName);
        services.AddSingleton(sp => options.StoreFactory!(sp));
        services.AddSingleton(sp => options.UsersFactory!(sp));
        services.AddSingleton<ISpineUserLookup>(sp => new SpineUserLookup(sp.GetRequiredService<IUserResolver>()));

        if (options.Providers.TryGetValue("apple", out var appleProvider) && appleProvider is SpineAppleOptions { CanRevoke: true } apple)
            services.AddSingleton(sp => new AppleTokenClient(apple, sp.GetRequiredService<IHttpClientFactory>(), TimeOf(sp), LoggerOf(sp)));

        services.AddSingleton(sp => new SpineTokenService(
            options, sp.GetRequiredService<ISpineAuthStore>(), sp.GetRequiredService<IUserResolver>(), TimeOf(sp), LoggerOf(sp)));
        services.AddSingleton(sp => new IdTokenValidator(
            sp.GetRequiredService<ISpineAuthStore>(), sp.GetRequiredService<IHttpClientFactory>(), TimeOf(sp), options));
        services.AddSingleton(sp => new SpineAccounts(
            options, sp.GetRequiredService<IUserResolver>(), sp.GetRequiredService<ISpineAuthStore>(), sp.GetRequiredService<SpineTokenService>(),
            sp.GetRequiredService<IdTokenValidator>(), sp.GetService<AppleTokenClient>(), sp.GetService<ISpineAuthEmailSender>(), TimeOf(sp), LoggerOf(sp)));

        services.AddSingleton(sp => new SpineBroker(
            options, sp.GetRequiredService<ISpineAuthStore>(), sp.GetRequiredService<IUserResolver>(), sp.GetRequiredService<IdTokenValidator>(),
            sp.GetRequiredService<SpineAccounts>(), sp.GetRequiredService<SpineTokenService>(), sp.GetService<AppleTokenClient>(),
            sp.GetRequiredService<IHttpClientFactory>(), TimeOf(sp), LoggerOf(sp)));

        if (options.PasskeyOptions is not null)
        {
            services.AddSingleton(sp => new SpinePasskeys(
                options, sp.GetRequiredService<IUserResolver>(), sp.GetRequiredService<ISpineAuthStore>(), sp.GetRequiredService<SpineTokenService>(),
                TimeOf(sp), LoggerOf(sp)));
        }

        services.AddAuthentication(SpineAuthDefaults.AuthenticationScheme).AddJwtBearer(SpineAuthDefaults.AuthenticationScheme);
        services.AddOptions<JwtBearerOptions>(SpineAuthDefaults.AuthenticationScheme).Configure<SpineTokenService>((bearer, tokens) =>
        {
            bearer.MapInboundClaims = false;
            bearer.TokenValidationParameters = tokens.ValidationParameters();
        });

        services.AddHostedService(sp => new StartupCheck(sp, options));
        return services;
    }

    private static ILogger LoggerOf(IServiceProvider services) =>
        (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("Plugin.Maui.Spine.Server.Authentication");

    private static TimeProvider TimeOf(IServiceProvider services) => services.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Builds the services at startup, so a bad key or a missing email sender stops the host instead of the first sign-in.</summary>
    private sealed class StartupCheck(IServiceProvider services, SpineAuthOptions options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = services.GetRequiredService<SpineTokenService>();

            if (options.EmailPasswordOptions is not null && services.GetService<ISpineAuthEmailSender>() is null)
                throw new InvalidOperationException(
                    "AddSpineAuthentication: EmailPassword() sends verification and reset codes; register an ISpineAuthEmailSender in the services.");

            if (options.Providers.TryGetValue("apple", out var apple) && apple is SpineAppleOptions { CanRevoke: false })
                LoggerOf(services).LogWarning(
                    "Sign in with Apple has no TeamId, KeyId and PrivateKey, so Apple's tokens are not revoked when an account is deleted (App Store rule, TN3194).");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
