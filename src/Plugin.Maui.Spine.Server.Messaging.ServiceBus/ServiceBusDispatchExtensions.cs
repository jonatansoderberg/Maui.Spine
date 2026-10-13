using Azure.Messaging.ServiceBus;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>Hands an Azure Functions Service Bus trigger's message to Spine.</summary>
public static class ServiceBusDispatchExtensions
{
    /// <summary>
    /// Dispatches a message from a <c>[ServiceBusTrigger]</c> on one of <paramref name="subscriber"/>'s subscriptions, one
    /// function per lane:
    /// <code>
    /// [Function("images")]
    /// public Task Images([ServiceBusTrigger("spine-messages", "images", Connection = "servicebus")] ServiceBusReceivedMessage message, CancellationToken ct)
    ///     => dispatcher.DispatchAsync("images", message, ct);
    /// </code>
    /// When it returns, the trigger completes the message. It throws only when messaging itself failed, and Service Bus
    /// then hands the message out again.
    /// </summary>
    public static Task DispatchAsync(this MessageDispatcher dispatcher, string subscriber, ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(message);
        return dispatcher.DispatchAsync(subscriber, ServiceBusEnvelopeMapper.ToEnvelope(message), message.DeliveryCount, cancellationToken);
    }
}
