using Microsoft.Extensions.AI;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

/// <summary>Every default call costs 0.006: 1000 tokens in at 3 and 200 out at 15, per million.</summary>
public class BudgetTests
{
    [Fact]
    public async Task A_spent_hard_budget_turns_the_feature_off_until_the_next_day()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:Budget:DayHard", "0.01")));

        await host.Story().GetResponseAsync("1");
        await host.Story().GetResponseAsync("2");

        // 0.012 spent: the second call overshot by one call, and the third is refused.
        var off = await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story().GetResponseAsync("3"));
        Assert.Equal(AIOffReason.Budget, off.Reason);
        Assert.Equal(AIFeatureState.BudgetSpent, host.Features.StateOf("Story"));
        Assert.Null(host.Features.Chat("Story", AICaller.System));
        Assert.Equal(2, host.Chat.Calls);

        host.Time.Advance(TimeSpan.FromDays(1));

        Assert.Equal(AIFeatureState.On, host.Features.StateOf("Story"));
        await host.Story().GetResponseAsync("4");
    }

    [Fact]
    public async Task The_app_wide_budget_covers_every_feature()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:Budget:MonthHard", "0.005"),
            ("Ai:Features:Other:Provider", "fake"),
            ("Ai:Features:Other:Model", TestHost.Model)));

        await host.Story().GetResponseAsync("1");

        var other = host.Features.Chat("Other", AICaller.System)!;
        await Assert.ThrowsAsync<AIFeatureOffException>(() => other.GetResponseAsync("2"));
        Assert.Equal(AIFeatureState.BudgetSpent, host.Features.StateOf("Other"));
    }

    [Fact]
    public async Task A_day_follows_the_configured_time_zone()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:TimeZone", "Europe/Stockholm"),
            ("Ai:Features:Story:Budget:DayHard", "0.005")));

        // 21:30 UTC is 23:30 in Stockholm in October: still the same day there.
        host.Time.SetUtcNow(new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero));
        await host.Story().GetResponseAsync("1");
        await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story().GetResponseAsync("2"));

        // 22:00 UTC is midnight in Stockholm.
        host.Time.SetUtcNow(new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.Zero));
        await host.Story().GetResponseAsync("3");
    }

    [Fact]
    public async Task Passing_a_soft_limit_tells_the_owner_once()
    {
        var alerts = new List<AIBudgetAlert>();
        using var host = new TestHost(
            TestHost.Settings(("Ai:Features:Story:Budget:DaySoft", "0.005"), ("Ai:Currency", "SEK")),
            ai => ai.OnBudgetThreshold = (alert, _, _) => { alerts.Add(alert); return Task.CompletedTask; });

        for (var i = 0; i < 4; i++)
            await host.Story().GetResponseAsync($"{i}");

        var alert = Assert.Single(alerts);
        Assert.Equal(AIBudgetLevel.Soft, alert.Level);
        Assert.Equal(AIUsageScope.ForFeature("Story"), alert.Scope);
        Assert.Equal(0.006m, alert.Spent);
        Assert.Equal("Story spent 0.006 SEK on 2026-10-12, over the soft limit of 0.005 SEK.", alert.ToString());
    }

    [Fact]
    public async Task A_failing_alert_does_not_fail_the_call()
    {
        using var host = new TestHost(
            TestHost.Settings(("Ai:Features:Story:Budget:DaySoft", "0")),
            ai => ai.OnBudgetThreshold = (_, _, _) => throw new InvalidOperationException("push is down"));

        await host.Story().GetResponseAsync("1");
    }

    [Fact]
    public async Task Installations_and_signed_in_users_have_their_own_allowances()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:Features:Story:Budget:PerCaller:SignedIn:DayCalls", "3"),
            ("Ai:Features:Story:Budget:PerCaller:Installation:DayCalls", "1")));

        await host.Story(AICaller.Installation("a")).GetResponseAsync("1");
        await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story(AICaller.Installation("a")).GetResponseAsync("2"));

        await host.Story(AICaller.Installation("b")).GetResponseAsync("3");
        await host.Story(AICaller.Anonymous("10.0.0.1")).GetResponseAsync("4");

        for (var i = 0; i < 3; i++)
            await host.Story(AICaller.SignedIn("anna")).GetResponseAsync($"{i}");
        await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story(AICaller.SignedIn("anna")).GetResponseAsync("5"));

        // One caller's allowance does not turn the feature off for anyone else.
        Assert.Equal(AIFeatureState.On, host.Features.StateOf("Story"));
        await host.Story(AICaller.System).GetResponseAsync("6");
    }

    [Fact]
    public async Task Without_an_installation_block_installations_get_the_signed_in_limits()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:Budget:PerCaller:SignedIn:DayHard", "0.005")));

        await host.Story(AICaller.Installation("a")).GetResponseAsync("1");
        await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story(AICaller.Installation("a")).GetResponseAsync("2"));
    }

    [Fact]
    public async Task A_feature_for_signed_in_users_turns_installations_away()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:Callers", "SignedIn")));

        var off = await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story(AICaller.Installation("a")).GetResponseAsync("1"));
        Assert.Equal(AIOffReason.SignInRequired, off.Reason);

        await host.Story(AICaller.SignedIn("anna")).GetResponseAsync("2");
        await host.Story(AICaller.System).GetResponseAsync("3");
    }

    [Fact]
    public async Task Too_many_calls_a_minute_are_refused_with_a_retry_time()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:Features:Story:RequestsPerMinute:SignedIn", "5"),
            ("Ai:Features:Story:RequestsPerMinute:Installation", "2")));

        await host.Story(AICaller.Installation("a")).GetResponseAsync("1");
        await host.Story(AICaller.Installation("a")).GetResponseAsync("2");

        var limited = await Assert.ThrowsAsync<AIRateLimitedException>(() => host.Story(AICaller.Installation("a")).GetResponseAsync("3"));
        Assert.NotNull(limited.RetryAfter);
        Assert.Equal(2, host.Store.Records.Count);

        await host.Story(AICaller.Installation("b")).GetResponseAsync("4");
        await host.Story(AICaller.SignedIn("anna")).GetResponseAsync("5");
        await host.Story(AICaller.SignedIn("anna")).GetResponseAsync("6");
        await host.Story(AICaller.SignedIn("anna")).GetResponseAsync("7");
    }

    [Fact]
    public async Task A_running_stream_holds_its_callers_place_until_it_ends()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:ConcurrentPerCaller", "1")));
        host.Chat.HoldStream = new TaskCompletionSource();

        var stream = host.Story().GetStreamingResponseAsync("1").GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());

        var limited = await Assert.ThrowsAsync<AIRateLimitedException>(() => host.Story().GetResponseAsync("2"));
        Assert.Null(limited.RetryAfter);
        await host.Story(AICaller.Installation("other")).GetResponseAsync("3");

        host.Chat.HoldStream.SetResult();
        while (await stream.MoveNextAsync()) { }
        await stream.DisposeAsync();

        host.Chat.HoldStream = null;
        await host.Story().GetResponseAsync("4");
    }

    [Fact]
    public async Task A_refused_call_is_not_recorded()
    {
        using var host = new TestHost(TestHost.Settings(("Ai:Features:Story:Budget:DayHard", "0")));

        await Assert.ThrowsAsync<AIFeatureOffException>(() => host.Story().GetResponseAsync("1"));

        Assert.Empty(host.Store.Records);
        Assert.Equal(0, host.Chat.Calls);
    }
}
