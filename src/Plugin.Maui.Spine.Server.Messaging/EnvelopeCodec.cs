using System.IO.Compression;
using System.Runtime.InteropServices;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// Fits a payload to the transport: gzip above the compression threshold, and the store (claim check) when it is still
/// too large. The other side reads it back without knowing which.
/// </summary>
internal sealed class EnvelopeCodec(MessagingRuntime runtime)
{
    public const string Gzip = "gzip";

    /// <summary>Encodes <paramref name="json"/> into the envelope.</summary>
    public Task<MessageEnvelope> PackAsync(MessageEnvelope envelope, byte[] json, CancellationToken cancellationToken)
    {
        if (json.Length > runtime.Settings.CompressionThreshold)
        {
            var compressed = Compress(json);
            if (compressed.Length < json.Length)
                return FitAsync(envelope with { Body = compressed, ContentEncoding = Gzip, Payload = null }, cancellationToken);
        }

        return FitAsync(envelope with { Body = json, ContentEncoding = null, Payload = null }, cancellationToken);
    }

    /// <summary>Moves an encoded body to the store when the transport cannot carry it inline.</summary>
    public async Task<MessageEnvelope> FitAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var limits = runtime.Transport.Limits;
        var length = envelope.Body.Length;
        var onWire = envelope.ContentEncoding is not null && limits.Base64Body ? (length + 2) / 3 * 4 : length;
        if (onWire <= limits.MaxBodyBytes)
            return envelope;

        var expiresAt = envelope.ExpiresAt ?? (envelope.DeliverAt ?? runtime.Time.GetUtcNow()) + runtime.Settings.Retention;
        var payload = await runtime.Store.PutPayloadAsync(envelope.Id, envelope.Body, expiresAt, cancellationToken);
        MessagingTelemetry.Offloaded.Add(1, MessagingTelemetry.Tags(envelope));
        return envelope with { Body = default, Payload = payload };
    }

    /// <summary>The payload as UTF-8 JSON; <see langword="null"/> when its offloaded copy is gone.</summary>
    public async Task<ReadOnlyMemory<byte>?> UnpackAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var body = envelope.Body;
        if (envelope.Payload is { } payload)
        {
            if (await runtime.Store.GetPayloadAsync(payload, cancellationToken) is not { } stored)
                return null;
            body = stored;
        }

        return envelope.ContentEncoding switch
        {
            null => body,
            Gzip => Decompress(body),
            var other => throw new UnrecoverableMessageException($"The payload is encoded as '{other}', which this version cannot read."),
        };
    }

    /// <summary>The envelope with an offloaded payload read back into it, for keeping as failed.</summary>
    public async Task<MessageEnvelope> InlineAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Payload is not { } payload)
            return envelope;

        return await runtime.Store.GetPayloadAsync(payload, cancellationToken) is { } body
            ? envelope with { Body = body, Payload = null }
            : envelope;
    }

    private static byte[] Compress(byte[] json)
    {
        using var output = new MemoryStream(json.Length / 3);
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(json);
        return output.ToArray();
    }

    private static ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> body)
    {
        using var input = MemoryMarshal.TryGetArray(body, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(body.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(body.Length * 4);
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
