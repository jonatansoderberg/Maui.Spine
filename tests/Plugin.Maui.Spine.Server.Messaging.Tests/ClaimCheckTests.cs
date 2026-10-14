using System.Security.Cryptography;
using Plugin.Maui.Spine.Server.Messaging.Transport;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class ClaimCheckTests
{
    private static readonly TransportLimits Small = new(4 * 1024, TimeSpan.MaxValue, Base64Body: true);

    private static void Archive(MessagingBuilder m)
    {
        m.Message<Big>();
        m.Subscriber("archive", s => s.Handles<Big>());
    }

    [Fact]
    public async Task A_compressible_payload_is_gzipped_and_stays_inline()
    {
        await using var bus = new TestBus(Archive, Small);
        var text = string.Concat(Enumerable.Repeat("The same line, again and again. ", 400));

        await bus.Publisher.PublishAsync(new Big(text));

        var envelope = Assert.Single(bus.Transport.Peek("archive"));
        Assert.Equal("gzip", envelope.ContentEncoding);
        Assert.Null(envelope.Payload);
        await bus.DrainAsync("archive");
        Assert.Equal(text, ((Big)Assert.Single(bus.Journal.Handled).Message).Text);
    }

    [Fact]
    public async Task A_payload_too_large_for_the_transport_goes_to_the_store_and_back()
    {
        await using var bus = new TestBus(Archive, Small);
        var text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(20 * 1024));

        await bus.Publisher.PublishAsync(new Big(text));

        var envelope = Assert.Single(bus.Transport.Peek("archive"));
        Assert.NotNull(envelope.Payload);
        Assert.True(envelope.Body.IsEmpty);
        Assert.Equal(1, bus.Store.PayloadCount);

        await bus.DrainAsync("archive");

        Assert.Equal(text, ((Big)Assert.Single(bus.Journal.Handled).Message).Text);
        Assert.Equal(0, bus.Store.PayloadCount);
    }

    [Fact]
    public async Task A_fanned_out_payload_stays_until_it_expires()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Big>();
            m.Subscriber("archive", s => s.Handles<Big>());
            m.Subscriber("search", s => s.Handles<Big>());
        }, Small);

        await bus.Publisher.PublishAsync(new Big(Convert.ToBase64String(RandomNumberGenerator.GetBytes(20 * 1024))));
        await bus.DrainAsync("archive");
        await bus.DrainAsync("search");

        Assert.Equal(2, bus.Journal.Handled.Count);
        Assert.Equal(1, bus.Store.PayloadCount);

        bus.Time!.Advance(TimeSpan.FromDays(15));
        await bus.Janitor.RunAsync();
        Assert.Equal(0, bus.Store.PayloadCount);
    }

    [Fact]
    public async Task A_failed_message_keeps_its_payload_inline()
    {
        await using var bus = new TestBus(m =>
        {
            m.Message<Big>();
            m.Subscriber("archive", s => s.Handles<Big>().Retry(1));
        }, Small);
        var text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(20 * 1024));
        var result = await bus.Publisher.PublishAsync(new Big(text));
        bus.Journal.Failures[result.MessageId] = 1;

        await bus.DrainAsync("archive");

        var failed = Assert.Single(await bus.FailedOfAsync());
        Assert.Null(failed.Envelope.Payload);
        Assert.False(failed.Envelope.Body.IsEmpty);
        Assert.Equal(0, bus.Store.PayloadCount);

        await bus.Failed.ReplayAsync(failed);
        Assert.NotNull(Assert.Single(bus.Transport.Peek("archive")).Payload);
        await bus.DrainAsync("archive");
        Assert.Equal(text, ((Big)Assert.Single(bus.Journal.Handled).Message).Text);
    }
}
