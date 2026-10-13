using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

/// <summary>The in-memory transport, with provisioning and the next sends made to fail on demand.</summary>
public sealed class FlakyTransport(InMemoryTransport inner) : IMessageTransport
{
    public bool FailProvisioning { get; set; }
    public int FailSends { get; set; }
    public int ProvisionAttempts { get; private set; }

    public TransportLimits Limits => inner.Limits;

    public Task ProvisionAsync(MessageTopology topology, CancellationToken cancellationToken)
    {
        ProvisionAttempts++;
        return FailProvisioning ? throw new InvalidOperationException("No right to create queues.") : inner.ProvisionAsync(topology, cancellationToken);
    }

    public Task SendAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken cancellationToken)
    {
        if (FailSends > 0)
        {
            FailSends--;
            throw new IOException("The queue did not answer.");
        }
        return inner.SendAsync(messages, cancellationToken);
    }

    public Task<IReadOnlyList<ITransportDelivery>> ReceiveAsync(MessageLane lane, int maxMessages, bool wait, CancellationToken cancellationToken) =>
        inner.ReceiveAsync(lane, maxMessages, wait, cancellationToken);

    public Task<IReadOnlyList<ITransportDelivery>> ReceiveDeadLettersAsync(MessageLane lane, int maxMessages, CancellationToken cancellationToken) =>
        inner.ReceiveDeadLettersAsync(lane, maxMessages, cancellationToken);
}
