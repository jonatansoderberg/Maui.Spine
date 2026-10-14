using System.Diagnostics;
using System.Diagnostics.Metrics;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// The name of messaging's <see cref="ActivitySource"/> and <see cref="Meter"/>. Add it to OpenTelemetry's tracing and
/// metrics (Aspire's service defaults: <c>.AddSource(MessagingTelemetry.Name)</c> and <c>.AddMeter(MessagingTelemetry.Name)</c>)
/// to see a message from publish to handler in one trace.
/// </summary>
public static class MessagingTelemetry
{
    /// <summary><c>Plugin.Maui.Spine.Server.Messaging</c>.</summary>
    public const string Name = "Plugin.Maui.Spine.Server.Messaging";

    internal static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    internal static readonly Counter<long> Published = Meter.CreateCounter<long>("spine.messaging.published", "{message}", "Messages published, one per subscriber.");
    internal static readonly Counter<long> Handled = Meter.CreateCounter<long>("spine.messaging.handled", "{message}", "Messages handled.");
    internal static readonly Counter<long> Retried = Meter.CreateCounter<long>("spine.messaging.retried", "{message}", "Failed attempts sent back for another try.");
    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>("spine.messaging.failed", "{message}", "Messages kept as failed.");
    internal static readonly Counter<long> Offloaded = Meter.CreateCounter<long>("spine.messaging.offloaded", "{message}", "Payloads too large for the transport, kept in the store.");
    internal static readonly Histogram<double> HandleDuration = Meter.CreateHistogram<double>("spine.messaging.handle.duration", "ms", "Time in the handler.");
    internal static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("spine.messaging.delivery.latency", "ms", "From due (or published) to the handler starting.");

    internal static TagList Tags(MessageEnvelope envelope, string? subscriber = null)
    {
        var tags = new TagList { { "messaging.message.type", envelope.Type }, { "messaging.priority", envelope.Priority.ToString() } };
        if (subscriber is not null)
            tags.Add("messaging.subscriber", subscriber);
        return tags;
    }
}
