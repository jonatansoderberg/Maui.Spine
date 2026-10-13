using Azure.Messaging.ServiceBus;
using Plugin.Maui.Spine.Server.Messaging.ServiceBus;
using Plugin.Maui.Spine.Server.Messaging.Transport;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class ServiceBusMapperTests
{
    private static readonly MessageLane Billing = new("billing", MessagePriority.Normal, "billing");

    private static MessageEnvelope Envelope() => new()
    {
        Id = "m1",
        Type = "order",
        Metadata = new Dictionary<string, string> { ["region"] = "north" },
        EnqueuedAt = DateTimeOffset.UtcNow,
        Body = "{}"u8.ToArray(),
    };

    private static ServiceBusReceivedMessage Received(ServiceBusMessage sent, IDictionary<string, object>? extra = null)
    {
        var properties = new Dictionary<string, object>(sent.ApplicationProperties);
        foreach (var (key, value) in extra ?? new Dictionary<string, object>())
            properties[key] = value;
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: sent.Body, messageId: sent.MessageId, subject: sent.Subject, properties: properties);
    }

    [Fact]
    public void Properties_others_add_are_not_read_as_metadata()
    {
        var sent = ServiceBusEnvelopeMapper.ToMessage(new OutgoingMessage(Envelope(), [Billing], IsDirect: false), DateTimeOffset.UtcNow);

        var envelope = ServiceBusEnvelopeMapper.ToEnvelope(Received(sent, new Dictionary<string, object> { ["Diagnostic-Id"] = "00-abc-01" }));

        Assert.Equal(new Dictionary<string, string> { ["region"] = "north" }, envelope.Metadata);
    }

    [Fact]
    public void A_copy_has_a_message_id_of_its_own_and_keeps_the_messages()
    {
        var original = ServiceBusEnvelopeMapper.ToMessage(new OutgoingMessage(Envelope(), [Billing], IsDirect: false), DateTimeOffset.UtcNow);
        var retry = ServiceBusEnvelopeMapper.ToMessage(new OutgoingMessage(Envelope() with { Attempt = 2 }, [Billing], IsDirect: true), DateTimeOffset.UtcNow);

        Assert.Equal("m1", original.MessageId);
        Assert.NotEqual("m1", retry.MessageId);
        Assert.Equal("m1", ServiceBusEnvelopeMapper.ToEnvelope(Received(retry)).Id);
        Assert.Equal(2, ServiceBusEnvelopeMapper.ToEnvelope(Received(retry)).Attempt);
    }
}
