using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.InMemory;

/// <summary>Runs messaging in memory.</summary>
public static class InMemoryMessagingExtensions
{
    /// <summary>
    /// Uses <see cref="InMemoryTransport"/> and <see cref="InMemoryMessageStore"/>: for tests, and for a single host that
    /// can lose its queue on restart. The clock is the registered <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="builder">The messaging declaration.</param>
    /// <param name="limits">What the transport carries inline; set a small limit to exercise offloading.</param>
    public static MessagingBuilder UseInMemory(this MessagingBuilder builder, TransportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(sp => new InMemoryTransport(sp.GetService<TimeProvider>(), limits));
        builder.Services.AddSingleton(sp => new InMemoryMessageStore(sp.GetService<TimeProvider>()));
        return builder
            .UseTransport(sp => sp.GetRequiredService<InMemoryTransport>())
            .UseStore(sp => sp.GetRequiredService<InMemoryMessageStore>());
    }
}
