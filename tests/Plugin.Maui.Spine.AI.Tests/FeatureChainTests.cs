using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.AI.Tests;

/// <summary>The fallback chain with two fake sources: one standing in for the device, one for the server.</summary>
public class FeatureChainTests
{
    private readonly FakeChatClient _device = new("Från enheten.");
    private readonly FakeChatClient _server = new("Från servern.");
    private readonly InMemoryAIDeviceUsageStore _usage = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));
    private bool _deviceAvailable = true;

    /// <summary>A feature whose sources are the two fakes; the server fake stands in on the device side of the chain.</summary>
    private IChatClient Feature(Action<AIFeatureChain> chain)
    {
        var options = new SpineAIOptions();
        options.Feature("Assistant", chain);
        var runtime = new SpineAIRuntime(options, _ => Task.FromResult<string?>(null), _usage, time: _time);
        return new SpineFeatureChatClient("Assistant", options.Features["Assistant"], runtime, new ServiceCollection().BuildServiceProvider());
    }

    private IChatClient DeviceThenServer() =>
        Feature(f => f.OnDevice(_ => _device, () => _deviceAvailable).OnDevice(_ => _server));

    private static async Task<(string Text, AIAnsweredBy? By)> StreamAsync(IChatClient client)
    {
        var text = "";
        AIAnsweredBy? by = null;
        await foreach (var update in client.GetStreamingResponseAsync("Hej!"))
        {
            by ??= update.AnsweredBy();
            text += update.Text;
        }

        return (text, by);
    }

    [Fact]
    public async Task The_first_source_answers_when_it_can()
    {
        var answer = await DeviceThenServer().GetResponseAsync("Hej!");

        Assert.Equal("Från enheten.", answer.Text);
        Assert.Equal(AIAnsweredBy.Device, answer.AnsweredBy());
        Assert.Equal(0, _server.Calls);
    }

    [Fact]
    public async Task An_unavailable_source_is_skipped_without_being_called()
    {
        _deviceAvailable = false;

        var (text, _) = await StreamAsync(DeviceThenServer());

        Assert.Equal("Från servern.", text);
        Assert.Equal(0, _device.Calls);
    }

    [Fact]
    public async Task A_source_that_fails_before_its_first_update_falls_back()
    {
        _device.ThrowAtOnce = new InvalidOperationException("Apple Intelligence is not ready");

        Assert.Equal("Från servern.", (await DeviceThenServer().GetResponseAsync("Hej!")).Text);
        Assert.Equal("Från servern.", (await StreamAsync(DeviceThenServer())).Text);
    }

    [Fact]
    public async Task After_the_first_update_there_is_no_fallback()
    {
        _device.ThrowAfterFirst = new InvalidOperationException("cut");

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => StreamAsync(DeviceThenServer()));

        Assert.Equal("cut", failed.Message);
        Assert.Equal(0, _server.Calls);
    }

    [Fact]
    public async Task A_cancelled_call_does_not_fall_back()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _device.ThrowAtOnce = new OperationCanceledException(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeviceThenServer().GetResponseAsync("Hej!", cancellationToken: cts.Token));

        Assert.Equal(0, _server.Calls);
    }

    [Fact]
    public async Task When_every_source_fails_the_last_failure_is_thrown()
    {
        _device.ThrowAtOnce = new InvalidOperationException("device");
        _server.ThrowAtOnce = new AIFeatureOffException("Assistant", AIOffReason.Budget);

        var off = await Assert.ThrowsAsync<AIFeatureOffException>(() => DeviceThenServer().GetResponseAsync("Hej!"));

        Assert.Equal(AIOffReason.Budget, off.Reason);
    }

    [Fact]
    public async Task With_no_source_available_the_feature_is_off()
    {
        _deviceAvailable = false;

        var off = await Assert.ThrowsAsync<AIFeatureOffException>(() => Feature(f => f.OnDevice(_ => _device, () => _deviceAvailable)).GetResponseAsync("Hej!"));

        Assert.Equal(AIOffReason.NotConfigured, off.Reason);
    }

    [Fact]
    public async Task A_source_whose_availability_check_throws_is_skipped()
    {
        var client = Feature(f => f.OnDevice(_ => _device, () => throw new PlatformNotSupportedException()).OnDevice(_ => _server));

        Assert.Equal("Från servern.", (await client.GetResponseAsync("Hej!")).Text);
    }

    [Fact]
    public async Task On_device_calls_are_counted_on_the_device()
    {
        var client = DeviceThenServer();
        await client.GetResponseAsync("Hej!");
        await StreamAsync(client);

        var usage = new AIUsage(new SpineAIRuntime(new SpineAIOptions(), _ => Task.FromResult<string?>(null), _usage, time: _time));

        Assert.Equal(new AIDeviceUsage(2, 2000, 400), usage.Today("Assistant"));
        Assert.Equal(AIDeviceUsage.Zero, usage.ForDay("Assistant", new DateOnly(2026, 10, 11)));
        Assert.Equal(new AIDeviceUsage(2, 2000, 400), usage.ForRange("assistant", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public async Task A_device_model_without_usage_is_counted_by_estimate()
    {
        _device.Usage = null;

        await DeviceThenServer().GetResponseAsync(new string('x', 40));

        // 40 characters in, and "Från enheten." (13) out, at four a token.
        Assert.Equal(new AIDeviceUsage(1, 10, 4), _usage.Get("Assistant", new DateOnly(2026, 10, 12)));
    }

    [Fact]
    public async Task A_device_call_that_fell_back_is_not_counted()
    {
        _device.ThrowAtOnce = new InvalidOperationException("not ready");

        await StreamAsync(DeviceThenServer());

        // The second fake is metered as a device source too, so one call is counted: the one that answered.
        Assert.Equal(1, _usage.Get("Assistant", new DateOnly(2026, 10, 12)).Calls);
    }

    [Fact]
    public void A_feature_needs_a_source()
    {
        Assert.Throws<ArgumentException>(() => new SpineAIOptions().Feature("Empty", _ => { }));
    }
}
