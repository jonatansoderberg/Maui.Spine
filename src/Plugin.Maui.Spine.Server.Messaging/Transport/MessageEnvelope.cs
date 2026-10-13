namespace Plugin.Maui.Spine.Server.Messaging.Transport;

/// <summary>
/// A message as it travels: the payload, encoded, and everything about it. Transports map it to their own message;
/// handlers see it through <see cref="MessageContext"/>.
/// </summary>
public sealed record MessageEnvelope
{
    internal static readonly IReadOnlyDictionary<string, string> NoMetadata = new Dictionary<string, string>();

    /// <summary>The type of a message a transport could not read. It goes straight to the failed messages.</summary>
    public const string UnreadableType = "spine-unreadable";

    /// <summary>The message's id, the same on every attempt and in every subscriber's copy.</summary>
    public required string Id { get; init; }

    /// <summary>The message type's name.</summary>
    public required string Type { get; init; }

    /// <summary>The metadata it was published with.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = NoMetadata;

    /// <summary>Its priority.</summary>
    public MessagePriority Priority { get; init; }

    /// <summary>Which attempt the copy is for, from 1.</summary>
    public int Attempt { get; init; } = 1;

    /// <summary>When it was published.</summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>When it is due; <see langword="null"/> for at once.</summary>
    public DateTimeOffset? DeliverAt { get; init; }

    /// <summary>After this the message is dropped unhandled.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The correlation id it was published with.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The W3C trace parent of the publish, so that handling shows in the same trace.</summary>
    public string? TraceParent { get; init; }

    /// <summary>How many subscribers it went to. With one, an offloaded payload is deleted as soon as it is handled.</summary>
    public int Recipients { get; init; } = 1;

    /// <summary><c>gzip</c> when <see cref="Body"/> (or the offloaded payload) is compressed; otherwise <see langword="null"/>.</summary>
    public string? ContentEncoding { get; init; }

    /// <summary>The payload as UTF-8 JSON, compressed by <see cref="ContentEncoding"/>. Empty when it is offloaded.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Where the payload is when it was too large for the transport.</summary>
    public PayloadReference? Payload { get; init; }
}

/// <summary>A payload kept in the <see cref="IMessageStore"/> because it was too large for the transport.</summary>
/// <param name="Location">The store's key for it.</param>
/// <param name="Length">Its size in bytes, as stored.</param>
public sealed record PayloadReference(string Location, long Length);
