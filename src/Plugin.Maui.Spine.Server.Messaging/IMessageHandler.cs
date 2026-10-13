namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Handles one type of message. Register it with
/// <see cref="ServiceCollectionExtensions.AddMessageHandler{TMessage, THandler}"/>; it is resolved in a new scope for
/// every message.
/// </summary>
/// <remarks>
/// A message is delivered at least once, so a handler must be safe to run twice. To fail a message, throw: it is retried
/// by the subscriber's <see cref="RetryPolicy"/>, and kept among the failed messages when the attempts run out. Throw
/// <see cref="UnrecoverableMessageException"/> for a message that no retry can fix.
/// </remarks>
/// <typeparam name="T">The message type.</typeparam>
public interface IMessageHandler<in T>
{
    /// <summary>Handles the message.</summary>
    Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>What a handler knows about the message beside its payload.</summary>
public sealed class MessageContext
{
    /// <summary>The message's id, the same on every attempt.</summary>
    public required string MessageId { get; init; }

    /// <summary>The message type's name, as registered with <see cref="MessagingBuilder.Message{T}"/>.</summary>
    public required string Type { get; init; }

    /// <summary>The subscriber that handles it.</summary>
    public required string Subscriber { get; init; }

    /// <summary>The metadata it was published with.</summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    /// <summary>Its priority.</summary>
    public MessagePriority Priority { get; init; }

    /// <summary>Which attempt this is, from 1.</summary>
    public int Attempt { get; init; }

    /// <summary>When it was published.</summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>When it was due, for a scheduled message or a retry.</summary>
    public DateTimeOffset? DeliverAt { get; init; }

    /// <summary>The correlation id it was published with.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Thrown by a handler for a message no retry can fix, such as one that refers to something that no longer exists. The
/// message goes straight to the failed messages.
/// </summary>
public class UnrecoverableMessageException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UnrecoverableMessageException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public UnrecoverableMessageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
