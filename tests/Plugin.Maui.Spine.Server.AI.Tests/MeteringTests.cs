using Microsoft.Extensions.AI;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

public class MeteringTests
{
    [Fact]
    public async Task A_call_is_recorded_with_its_tokens_its_cost_and_its_caller()
    {
        using var host = new TestHost(TestHost.Settings());
        host.Chat.Usage = new() { InputTokenCount = 1000, CachedInputTokenCount = 400, OutputTokenCount = 200 };

        await host.Story(AICaller.SignedIn("anna")).GetResponseAsync("Skriv något.");

        var record = Assert.Single(host.Store.Records);
        Assert.Equal("Story", record.Feature);
        Assert.Equal("fake", record.Provider);
        Assert.Equal(TestHost.Model, record.Model);
        Assert.Equal("user:anna", record.Caller);
        Assert.Equal(1000, record.InputTokens);
        Assert.Equal(400, record.CachedInputTokens);
        Assert.Equal(200, record.OutputTokens);
        // 600 × 3 + 400 × 0.3 + 200 × 15, per million.
        Assert.Equal(0.00492m, record.Cost);
        Assert.True(record.Complete);
        Assert.Null(record.Error);
        Assert.Equal(TestHost.Now, record.At);
    }

    [Fact]
    public async Task A_stream_is_recorded_once_with_the_usage_its_updates_carried()
    {
        using var host = new TestHost(TestHost.Settings());

        var text = "";
        await foreach (var update in host.Story().GetStreamingResponseAsync("Skriv något."))
            text += update.Text;

        Assert.Equal("Hej då.", text);
        var record = Assert.Single(host.Store.Records);
        Assert.Equal(1000, record.InputTokens);
        Assert.Equal(200, record.OutputTokens);
        Assert.True(record.Complete);
    }

    [Fact]
    public async Task A_stream_the_caller_leaves_early_is_still_recorded()
    {
        using var host = new TestHost(TestHost.Settings());

        await foreach (var _ in host.Story().GetStreamingResponseAsync("Skriv något."))
            break;

        var record = Assert.Single(host.Store.Records);
        Assert.False(record.Complete);
        Assert.Equal(3, record.InputTokens);
    }

    [Fact]
    public async Task A_provider_that_reports_no_usage_is_recorded_with_an_estimate_and_marked_incomplete()
    {
        using var host = new TestHost(TestHost.Settings());
        host.Chat.Usage = null;

        await host.Story().GetResponseAsync(new string('x', 400));

        var record = Assert.Single(host.Store.Records);
        Assert.False(record.Complete);
        Assert.Equal(100, record.InputTokens);
        Assert.Equal(0, record.OutputTokens);
        Assert.Equal(0.0003m, record.Cost);
    }

    [Fact]
    public async Task A_failed_call_is_recorded_with_its_error_and_the_error_still_reaches_the_caller()
    {
        using var host = new TestHost(TestHost.Settings());
        host.Chat.Throw = new HttpRequestException("upstream 529");

        await Assert.ThrowsAsync<HttpRequestException>(() => host.Story().GetResponseAsync("Skriv något."));

        var record = Assert.Single(host.Store.Records);
        Assert.Equal("HttpRequestException: upstream 529", record.Error);
        Assert.False(record.Complete);
    }

    [Fact]
    public async Task A_stream_that_fails_midway_is_recorded_with_its_error()
    {
        using var host = new TestHost(TestHost.Settings());
        host.Chat.Throw = new HttpRequestException("cut");

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in host.Story().GetStreamingResponseAsync("Skriv något.")) { }
        });

        Assert.Equal("HttpRequestException: cut", Assert.Single(host.Store.Records).Error);
    }

    [Fact]
    public async Task A_model_without_a_price_is_recorded_without_a_cost()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:Model", "unpriced")));

        await host.Story().GetResponseAsync("Skriv något.");

        var record = Assert.Single(host.Store.Records);
        Assert.Equal("unpriced", record.Model);
        Assert.Null(record.Cost);
    }

    [Fact]
    public async Task The_model_a_call_asks_for_is_the_one_it_is_priced_by()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Prices:fake:fake-big:InputPerMillion", "30")));

        await host.Story().GetResponseAsync("Skriv något.", new ChatOptions { ModelId = "fake-big" });

        var record = Assert.Single(host.Store.Records);
        Assert.Equal("fake-big", record.Model);
        Assert.Equal(0.03m, record.Cost);
    }

    [Fact]
    public async Task Totals_are_kept_for_the_app_the_feature_and_the_caller_by_day_and_month()
    {
        using var host = new TestHost(TestHost.Settings());

        await host.Story(AICaller.Installation("a")).GetResponseAsync("1");
        await host.Story(AICaller.Installation("a")).GetResponseAsync("2");
        await host.Story(AICaller.Installation("b")).GetResponseAsync("3");

        var day = AIPeriod.Day(TestHost.Now, TimeZoneInfo.Utc);
        var month = AIPeriod.Month(TestHost.Now, TimeZoneInfo.Utc);

        Assert.Equal(3, (await host.Store.GetTotalsAsync(AIUsageScope.App, day)).Calls);
        Assert.Equal(3, (await host.Store.GetTotalsAsync(AIUsageScope.ForFeature("Story"), month)).Calls);
        Assert.Equal(2, (await host.Store.GetTotalsAsync(AIUsageScope.ForCaller("Story", AICaller.Installation("a")), day)).Calls);
        Assert.Equal(0.018m, (await host.Store.GetTotalsAsync(AIUsageScope.App, day)).Cost);
    }
}
