using System.Globalization;
using Azure.Messaging.ServiceBus;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.ServiceBus;

/// <summary>
/// The envelope as a Service Bus message: the payload is the body, the type is the subject, and the rest are application
/// properties. Metadata travels under its own keys, so correlation filters can match it; Spine's own start with
/// <c>spine-</c>.
/// </summary>
internal static class ServiceBusEnvelopeMapper
{
    public const string To = "spine-to";
    public const string Broadcast = "*";
    public const string Priority = "spine-priority";
    private const string Attempt = "spine-attempt";
    private const string Enqueued = "spine-enqueued";
    private const string Due = "spine-due";
    private const string Expires = "spine-expires";
    private const string Trace = "spine-trace";
    private const string Recipients = "spine-recipients";
    private const string Encoding = "spine-encoding";
    private const string PayloadLocation = "spine-payload";
    private const string PayloadLength = "spine-payload-length";

    public static string PriorityValue(MessagePriority priority) => priority switch
    {
        MessagePriority.High => "high",
        MessagePriority.Low => "low",
        _ => "normal",
    };

    public static ServiceBusMessage ToMessage(OutgoingMessage outgoing, DateTimeOffset now)
    {
        var envelope = outgoing.Envelope;
        var message = new ServiceBusMessage(envelope.Body)
        {
            MessageId = envelope.Id,
            Subject = envelope.Type,
            CorrelationId = envelope.CorrelationId,
            ContentType = "application/json",
        };

        var properties = message.ApplicationProperties;
        foreach (var (key, value) in envelope.Metadata)
            properties[key] = value;

        properties[To] = outgoing.IsDirect ? outgoing.Lanes[0].Subscriber : Broadcast;
        properties[Priority] = PriorityValue(envelope.Priority);
        properties[Attempt] = envelope.Attempt;
        properties[Enqueued] = envelope.EnqueuedAt;
        properties[Recipients] = envelope.Recipients;
        if (envelope.DeliverAt is { } due)
            properties[Due] = due;
        if (envelope.ExpiresAt is { } expires)
            properties[Expires] = expires;
        if (envelope.TraceParent is { } trace)
            properties[Trace] = trace;
        if (envelope.ContentEncoding is { } encoding)
            properties[Encoding] = encoding;
        if (envelope.Payload is { } payload)
        {
            properties[PayloadLocation] = payload.Location;
            properties[PayloadLength] = payload.Length;
        }

        var activeFrom = envelope.DeliverAt is { } at && at > now ? at : now;
        if (activeFrom > now)
            message.ScheduledEnqueueTime = activeFrom;
        if (envelope.ExpiresAt is { } expiresAt)
            message.TimeToLive = expiresAt - activeFrom > TimeSpan.FromSeconds(1) ? expiresAt - activeFrom : TimeSpan.FromSeconds(1);

        return message;
    }

    /// <summary>Reads a received message; one that cannot be read becomes an <see cref="MessageEnvelope.UnreadableType"/> envelope.</summary>
    public static MessageEnvelope ToEnvelope(ServiceBusReceivedMessage message)
    {
        try
        {
            var properties = message.ApplicationProperties;
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in properties)
            {
                if (!key.StartsWith(MetadataRulesPrefix, StringComparison.OrdinalIgnoreCase) && value is not null)
                    metadata[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            }

            return new MessageEnvelope
            {
                Id = message.MessageId,
                Type = message.Subject ?? throw new FormatException("The message has no subject (type)."),
                Metadata = metadata,
                Priority = properties.TryGetValue(Priority, out var priority) ? ParsePriority(priority as string) : MessagePriority.Normal,
                Attempt = properties.TryGetValue(Attempt, out var attempt) ? Convert.ToInt32(attempt, CultureInfo.InvariantCulture) : 1,
                EnqueuedAt = properties.TryGetValue(Enqueued, out var enqueued) && enqueued is DateTimeOffset at ? at : message.EnqueuedTime,
                DeliverAt = properties.TryGetValue(Due, out var due) && due is DateTimeOffset dueAt ? dueAt : null,
                ExpiresAt = properties.TryGetValue(Expires, out var expires) && expires is DateTimeOffset expiresAt ? expiresAt : null,
                CorrelationId = message.CorrelationId,
                TraceParent = properties.TryGetValue(Trace, out var trace) ? trace as string : null,
                Recipients = properties.TryGetValue(Recipients, out var recipients) ? Convert.ToInt32(recipients, CultureInfo.InvariantCulture) : 1,
                ContentEncoding = properties.TryGetValue(Encoding, out var encoding) ? encoding as string : null,
                Body = message.Body.ToMemory(),
                Payload = properties.TryGetValue(PayloadLocation, out var location) && location is string path
                    ? new PayloadReference(path, properties.TryGetValue(PayloadLength, out var length) ? Convert.ToInt64(length, CultureInfo.InvariantCulture) : 0)
                    : null,
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return new MessageEnvelope { Id = message.MessageId, Type = MessageEnvelope.UnreadableType, Body = message.Body.ToMemory() };
        }
    }

    private const string MetadataRulesPrefix = "spine-";

    private static MessagePriority ParsePriority(string? value) => value switch
    {
        "high" => MessagePriority.High,
        "low" => MessagePriority.Low,
        _ => MessagePriority.Normal,
    };
}
