using Plugin.Maui.Spine.Server.Messaging.Transport;
using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class SchedulingTests
{
    private static void Mailer(MessagingBuilder m)
    {
        m.Message<Mail>();
        m.Subscriber("mailer", s => s.Handles<Mail>());
    }

    [Fact]
    public async Task A_scheduled_message_stays_dormant_until_it_is_due()
    {
        await using var bus = new TestBus(Mailer);

        var result = await bus.Publisher.PublishAsync(new Mail("a@b"), new PublishOptions { Delay = TimeSpan.FromHours(2) });

        Assert.Equal(TestBus.Start.AddHours(2), result.DeliverAt);
        Assert.Equal(0, await bus.DrainAsync("mailer"));

        bus.Time!.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await bus.DrainAsync("mailer"));
        Assert.Equal(TestBus.Start.AddHours(2), Assert.Single(bus.Journal.Contexts).DeliverAt);
    }

    [Fact]
    public async Task A_time_in_the_past_delivers_at_once()
    {
        await using var bus = new TestBus(Mailer);

        var result = await bus.Publisher.PublishAsync(new Mail("a@b"), new PublishOptions { DeliverAt = TestBus.Start.AddMinutes(-5) });

        Assert.Null(result.DeliverAt);
        Assert.Equal(1, await bus.DrainAsync("mailer"));
    }

    [Fact]
    public async Task A_message_due_beyond_the_transports_limit_is_sent_on_until_it_is_due()
    {
        var storageLike = new TransportLimits(46 * 1024, TimeSpan.FromDays(7), Base64Body: true);
        await using var bus = new TestBus(Mailer, storageLike);

        await bus.Publisher.PublishAsync(new Mail("a@b"), new PublishOptions { DeliverAt = TestBus.Start.AddDays(16) });

        bus.Time!.Advance(TimeSpan.FromDays(7));
        Assert.Equal(1, await bus.DrainAsync("mailer"));
        Assert.Empty(bus.Journal.Handled);

        bus.Time.Advance(TimeSpan.FromDays(7));
        Assert.Equal(1, await bus.DrainAsync("mailer"));
        Assert.Empty(bus.Journal.Handled);

        bus.Time.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await bus.DrainAsync("mailer"));
        Assert.Single(bus.Journal.Handled);
    }

    [Fact]
    public async Task A_cancelled_message_is_dropped_when_it_comes_due()
    {
        await using var bus = new TestBus(Mailer);

        var kept = await bus.Publisher.PublishAsync(new Mail("kept"), new PublishOptions { Delay = TimeSpan.FromDays(1) });
        var cancelled = await bus.Publisher.PublishAsync(new Mail("cancelled"), new PublishOptions { Delay = TimeSpan.FromDays(1) });
        await bus.Publisher.CancelAsync(cancelled);

        bus.Time!.Advance(TimeSpan.FromDays(1));
        await bus.DrainAsync("mailer");

        Assert.Equal(kept.MessageId, Assert.Single(bus.Journal.Contexts).MessageId);
        Assert.Empty(await bus.FailedOfAsync());
    }

    [Fact]
    public async Task Only_a_scheduled_message_can_be_cancelled()
    {
        await using var bus = new TestBus(Mailer);

        var now = await bus.Publisher.PublishAsync(new Mail("a@b"));

        await Assert.ThrowsAsync<ArgumentException>(() => bus.Publisher.CancelAsync(now));
    }

    [Fact]
    public async Task A_message_not_handled_within_its_time_to_live_is_dropped()
    {
        await using var bus = new TestBus(Mailer);

        await bus.Publisher.PublishAsync(new Mail("a@b"), new PublishOptions { TimeToLive = TimeSpan.FromMinutes(10) });
        bus.Time!.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(1, await bus.DrainAsync("mailer"));
        Assert.Empty(bus.Journal.Handled);
        Assert.Empty(await bus.FailedOfAsync());
    }
}
