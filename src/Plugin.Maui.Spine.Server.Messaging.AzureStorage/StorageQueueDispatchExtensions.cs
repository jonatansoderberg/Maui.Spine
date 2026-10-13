using Azure.Storage.Queues.Models;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>Hands an Azure Functions queue trigger's message to Spine.</summary>
public static class StorageQueueDispatchExtensions
{
    /// <summary>
    /// Dispatches a message from a <c>[QueueTrigger]</c> on one of <paramref name="subscriber"/>'s queues. Bind the trigger
    /// to <see cref="QueueMessage"/>, one function per lane:
    /// <code>
    /// [Function("images")]
    /// public Task Images([QueueTrigger("images", Connection = "queues")] QueueMessage message, CancellationToken ct)
    ///     => dispatcher.DispatchAsync("images", message, ct);
    /// </code>
    /// When it returns, the trigger completes the message. It throws only when messaging itself failed, and the queue
    /// then hands the message out again.
    /// </summary>
    public static Task DispatchAsync(this MessageDispatcher dispatcher, string subscriber, QueueMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(message);
        return dispatcher.DispatchAsync(subscriber, StorageQueueWire.Decode(message.Body, message.MessageId), (int)message.DequeueCount, cancellationToken);
    }
}
