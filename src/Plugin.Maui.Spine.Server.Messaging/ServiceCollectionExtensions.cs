using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>Registers Spine messaging.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds messaging: the transport, the message types and the subscribers. Call it with the same declaration in every
    /// host that publishes or handles, typically from a method in a shared library.
    /// </summary>
    /// <remarks>
    /// Registers <see cref="IMessagePublisher"/>, <see cref="MessageDispatcher"/>, <see cref="IFailedMessages"/>,
    /// <see cref="MessagingJanitor"/> and <see cref="MessageTopology"/>. The transport's queues and the store are
    /// provisioned when the host starts.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The declaration is incomplete or inconsistent; the message says how.</exception>
    public static IServiceCollection AddSpineMessaging(this IServiceCollection services, Action<MessagingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new MessagingBuilder(services);
        configure(builder);
        var settings = builder.Build();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddSingleton(settings.Topology);
        services.AddSingleton(sp => settings.Transport(sp));
        services.AddSingleton(sp => settings.Store(sp));
        services.AddSingleton<MessagingRuntime>();
        services.AddSingleton<EnvelopeCodec>();
        services.AddSingleton<IMessagePublisher, MessagePublisher>();
        services.AddSingleton(sp => new MessageDispatcher(
            sp.GetRequiredService<MessagingRuntime>(), sp.GetRequiredService<EnvelopeCodec>(), sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<IFailedMessages, FailedMessages>();
        services.AddSingleton(sp => new MessagingJanitor(sp.GetRequiredService<MessagingRuntime>(), sp.GetRequiredService<MessageDispatcher>()));
        services.AddHostedService<MessagingStartup>();
        return services;
    }

    /// <summary>Registers the handler for <typeparamref name="TMessage"/>, resolved in a new scope for every message.</summary>
    public static IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        return services;
    }

    /// <summary>
    /// Handles <paramref name="subscribers"/>' messages in this host, with a pump per subscriber. For ASP.NET Core and
    /// worker services; in Azure Functions, triggers call <see cref="MessageDispatcher"/> instead.
    /// </summary>
    public static IServiceCollection AddSpineMessagePump(this IServiceCollection services, params string[] subscribers)
    {
        ArgumentNullException.ThrowIfNull(subscribers);
        foreach (var subscriber in subscribers)
        {
            services.AddSingleton<IHostedService>(sp => new MessagePump(subscriber, sp.GetRequiredService<MessagingRuntime>(), sp.GetRequiredService<MessageDispatcher>()));
        }
        return services;
    }

    /// <summary>
    /// Runs <see cref="MessagingJanitor"/> in this host every <see cref="MessagingBuilder.JanitorInterval"/>. Any number
    /// of instances may run it; one at a time cleans up.
    /// </summary>
    public static IServiceCollection AddSpineMessagingJanitor(this IServiceCollection services)
    {
        services.AddHostedService<JanitorService>();
        return services;
    }

    private sealed class MessagingStartup(MessagingRuntime runtime) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await runtime.EnsureReadyAsync(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Logged by the runtime, and tried again on first use: a storage account that is still starting (Azurite
                // under Aspire) should not stop the host.
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
