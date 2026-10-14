using Xunit;

namespace Plugin.Maui.Spine.Server.Messaging.Tests;

public sealed class RetryTests
{
    private static void Billing(MessagingBuilder m)
    {
        m.Message<Order>();
        m.Subscriber("billing", s => s.Handles<Order>().Retry(3, first: TimeSpan.FromSeconds(10), max: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task A_failing_message_is_tried_again_after_a_backoff()
    {
        await using var bus = new TestBus(Billing);
        var result = await bus.Publisher.PublishAsync(new Order("o1"));
        bus.Journal.Failures[result.MessageId] = 2;

        Assert.Equal(1, await bus.DrainAsync("billing"));
        var retry = Assert.Single(bus.Transport.Peek("billing"));
        Assert.Equal(2, retry.Attempt);
        Assert.InRange(retry.DeliverAt!.Value - TestBus.Start, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));

        Assert.Equal(0, await bus.DrainAsync("billing"));
        bus.Time!.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(1, await bus.DrainAsync("billing"));

        bus.Time.Advance(TimeSpan.FromSeconds(24));
        Assert.Equal(1, await bus.DrainAsync("billing"));

        var context = Assert.Single(bus.Journal.Contexts);
        Assert.Equal(3, context.Attempt);
        Assert.Equal(result.MessageId, context.MessageId);
        Assert.Empty(await bus.FailedOfAsync());
    }

    [Fact]
    public async Task A_message_that_runs_out_of_attempts_is_kept_as_failed()
    {
        await using var bus = new TestBus(Billing);
        var result = await bus.Publisher.PublishAsync(new Order("o1"));
        bus.Journal.Failures[result.MessageId] = -1;

        for (var i = 0; i < 3; i++)
        {
            await bus.DrainAsync("billing");
            bus.Time!.Advance(TimeSpan.FromMinutes(2));
        }

        var failed = Assert.Single(await bus.FailedOfAsync("billing"));
        Assert.Equal(result.MessageId, failed.MessageId);
        Assert.Equal(3, failed.Attempts);
        Assert.Contains("Attempt 3 of 3", failed.Error);
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.ExceptionType);
        Assert.Empty(bus.Transport.Peek("billing"));
    }

    [Fact]
    public async Task An_unrecoverable_message_is_kept_as_failed_at_once()
    {
        await using var bus = new TestBus(Billing);
        var result = await bus.Publisher.PublishAsync(new Order("o1"));
        bus.Journal.Failures[result.MessageId] = -1;
        bus.Journal.Unrecoverable = true;

        await bus.DrainAsync("billing");

        Assert.Equal(1, Assert.Single(await bus.FailedOfAsync()).Attempts);
        Assert.Empty(bus.Transport.Peek("billing"));
    }

    [Fact]
    public async Task A_message_delivered_too_often_without_completing_is_kept_as_failed()
    {
        await using var bus = new TestBus(Billing);
        var result = await bus.Publisher.PublishAsync(new Order("o1"));
        var envelope = Assert.Single(bus.Transport.Peek("billing"));

        await bus.Dispatcher.DispatchAsync("billing", envelope, deliveryCount: 11, default);

        Assert.Empty(bus.Journal.Handled);
        Assert.Contains("Delivered 11 times", Assert.Single(await bus.FailedOfAsync()).Error);
    }

    [Fact]
    public void The_backoff_doubles_up_to_the_cap_with_jitter()
    {
        var policy = new RetryPolicy(10, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));

        Assert.InRange(policy.DelayAfter(1).TotalSeconds, 8, 12);
        Assert.InRange(policy.DelayAfter(2).TotalSeconds, 16, 24);
        Assert.InRange(policy.DelayAfter(3).TotalSeconds, 32, 48);
        Assert.InRange(policy.DelayAfter(9).TotalSeconds, 48, 72);
    }
}
