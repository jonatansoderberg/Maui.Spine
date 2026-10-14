using System.Buffers;
using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>
/// The envelope as a queue message's text: compact JSON with the payload inline as raw JSON (or base64 when gzipped), so
/// an uncompressed payload is not encoded twice.
/// </summary>
internal static class StorageQueueWire
{
    public static string Encode(MessageEnvelope envelope)
    {
        var buffer = new ArrayBufferWriter<byte>(envelope.Body.Length + 512);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("id", envelope.Id);
            json.WriteString("type", envelope.Type);
            if (envelope.Metadata.Count > 0)
            {
                json.WriteStartObject("meta");
                foreach (var (key, value) in envelope.Metadata)
                    json.WriteString(key, value);
                json.WriteEndObject();
            }
            if (envelope.Priority != MessagePriority.Normal)
                json.WriteNumber("prio", (int)envelope.Priority);
            json.WriteNumber("att", envelope.Attempt);
            json.WriteString("enq", envelope.EnqueuedAt);
            if (envelope.DeliverAt is { } due)
                json.WriteString("due", due);
            if (envelope.ExpiresAt is { } expires)
                json.WriteString("exp", expires);
            if (envelope.CorrelationId is { } correlation)
                json.WriteString("corr", correlation);
            if (envelope.TraceParent is { } trace)
                json.WriteString("trace", trace);
            if (envelope.Recipients != 1)
                json.WriteNumber("rcpt", envelope.Recipients);
            if (envelope.ContentEncoding is { } encoding)
                json.WriteString("enc", encoding);
            if (envelope.Payload is { } payload)
            {
                json.WriteStartObject("pay");
                json.WriteString("loc", payload.Location);
                json.WriteNumber("len", payload.Length);
                json.WriteEndObject();
            }
            else if (envelope.ContentEncoding is null)
            {
                json.WritePropertyName("body");
                json.WriteRawValue(envelope.Body.Span, skipInputValidation: true);
            }
            else
            {
                json.WriteBase64String("body64", envelope.Body.Span);
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Reads a queue message's text. Accepts it base64-encoded as well, the way Azure Functions may hand it over. A
    /// message that cannot be read becomes an <see cref="MessageEnvelope.UnreadableType"/> envelope with the raw text.
    /// </summary>
    public static MessageEnvelope Decode(BinaryData body, string fallbackId)
    {
        try
        {
            var bytes = body.ToMemory();
            if (bytes.Length > 0 && bytes.Span[0] != (byte)'{')
                bytes = Convert.FromBase64String(Encoding.ASCII.GetString(bytes.Span));
            return Read(bytes);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return new MessageEnvelope { Id = fallbackId, Type = MessageEnvelope.UnreadableType, Body = body.ToMemory() };
        }
    }

    private static MessageEnvelope Read(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Dictionary<string, string>? metadata = null;
        if (root.TryGetProperty("meta", out var meta))
        {
            metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in meta.EnumerateObject())
                metadata[property.Name] = property.Value.GetString()!;
        }

        ReadOnlyMemory<byte> body = default;
        if (root.TryGetProperty("body", out var raw))
            body = Encoding.UTF8.GetBytes(raw.GetRawText());
        else if (root.TryGetProperty("body64", out var encoded))
            body = encoded.GetBytesFromBase64();

        PayloadReference? payload = null;
        if (root.TryGetProperty("pay", out var pay))
            payload = new PayloadReference(pay.GetProperty("loc").GetString()!, pay.GetProperty("len").GetInt64());

        return new MessageEnvelope
        {
            Id = root.GetProperty("id").GetString()!,
            Type = root.GetProperty("type").GetString()!,
            Metadata = metadata ?? MessageEnvelope.NoMetadata,
            Priority = root.TryGetProperty("prio", out var priority) ? (MessagePriority)priority.GetInt32() : MessagePriority.Normal,
            Attempt = root.GetProperty("att").GetInt32(),
            EnqueuedAt = root.GetProperty("enq").GetDateTimeOffset(),
            DeliverAt = root.TryGetProperty("due", out var due) ? due.GetDateTimeOffset() : null,
            ExpiresAt = root.TryGetProperty("exp", out var expires) ? expires.GetDateTimeOffset() : null,
            CorrelationId = root.TryGetProperty("corr", out var correlation) ? correlation.GetString() : null,
            TraceParent = root.TryGetProperty("trace", out var trace) ? trace.GetString() : null,
            Recipients = root.TryGetProperty("rcpt", out var recipients) ? recipients.GetInt32() : 1,
            ContentEncoding = root.TryGetProperty("enc", out var encoding) ? encoding.GetString() : null,
            Body = body,
            Payload = payload,
        };
    }
}
