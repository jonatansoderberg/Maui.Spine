using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class DeduplicationTests
{
    private static void Billing(MessagingBuilder m)
    {
        m.Message<Order>();
        m.Subscriber("billing", s => s.Handles<Order>());
    }

    [Fact]
    public async Task A_repeat_within_the_window_is_dropped()
    {
        await using var bus = new TestBus(Billing);
        var options = new PublishOptions { DeduplicationId = "order-o1", DeduplicationWindow = TimeSpan.FromMinutes(30) };

        var first = await bus.Publisher.PublishAsync(new Order("o1"), options);
        var repeat = await bus.Publisher.PublishAsync(new Order("o1"), options);

        Assert.False(first.IsDuplicate);
        Assert.True(repeat.IsDuplicate);
        Assert.Empty(repeat.Subscribers);
        Assert.Single(bus.Transport.Peek("billing"));
    }

    [Fact]
    public async Task After_the_window_it_is_published_again()
    {
        await using var bus = new TestBus(Billing);
        var options = new PublishOptions { DeduplicationId = "order-o1", DeduplicationWindow = TimeSpan.FromMinutes(30) };

        await bus.Publisher.PublishAsync(new Order("o1"), options);
        bus.Time!.Advance(TimeSpan.FromMinutes(31));
        var later = await bus.Publisher.PublishAsync(new Order("o1"), options);

        Assert.False(later.IsDuplicate);
        Assert.Equal(2, bus.Transport.Peek("billing").Count);
    }

    [Fact]
    public async Task A_batch_cannot_deduplicate()
    {
        await using var bus = new TestBus(Billing);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            bus.Publisher.PublishBatchAsync([new Order("a")], new PublishOptions { DeduplicationId = "x" }));
    }

    [Fact]
    public async Task A_batch_is_sent_together()
    {
        await using var bus = new TestBus(Billing);

        var results = await bus.Publisher.PublishBatchAsync(Enumerable.Range(0, 50).Select(i => new Order($"o{i}")));

        Assert.Equal(50, results.Select(r => r.MessageId).Distinct().Count());
        Assert.Equal(50, bus.Transport.Peek("billing").Count);
    }
}
