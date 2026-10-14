namespace Plugin.Maui.Spine.Server.AI.Assets.Tests;

public sealed class FailureTests
{
    [Fact]
    public async Task A_refused_image_is_made_again_and_the_one_that_passes_is_kept()
    {
        await using var host = new AssetHost();
        host.Script.Verdicts.Enqueue(GeneratedAssetCheckResult.Fail("The wall is gone."));
        await host.LookUpAsync("e1/summer");

        await host.DrainWithRetriesAsync();

        Assert.Equal(2, host.Painter.Calls);
        Assert.Equal(GeneratedAssetStatus.Ready, (await host.LookUpAsync("e1/summer")).Status);
    }

    [Fact]
    public async Task After_the_last_attempt_the_failure_is_visible_and_the_image_is_ordered_again_after_the_cool_down()
    {
        await using var host = new AssetHost(("Ai:Features:Arena:Asset:FailureCoolDown", "01:00:00"));
        host.Painter.FailuresLeft = 3;
        await host.LookUpAsync("e1/summer");

        await host.DrainWithRetriesAsync();
        var failed = await host.LookUpAsync("e1/summer");

        Assert.Equal(3, host.Painter.Calls);
        Assert.Equal(GeneratedAssetStatus.Failed, failed.Status);
        Assert.Equal(GeneratedAssetFailure.Model, failed.Failure);
        Assert.Single(await host.FailedMessagesAsync());

        // Within the cool-down, lookups order nothing.
        host.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(GeneratedAssetStatus.Failed, (await host.LookUpAsync("e1/summer")).Status);
        Assert.Equal(0, await host.DrainAsync());

        host.Time.Advance(TimeSpan.FromMinutes(31));
        var again = await host.LookUpAsync("e1/summer");
        await host.DrainAsync();

        Assert.Equal(GeneratedAssetStatus.Pending, again.Status);
        Assert.Equal(4, host.Painter.Calls);
        Assert.Equal(GeneratedAssetStatus.Ready, (await host.LookUpAsync("e1/summer")).Status);
        Assert.Null(await host.Store.GetFailureAsync("arena/v1/e1/summer", default));
    }

    [Fact]
    public async Task A_check_that_refuses_every_image_fails_as_check()
    {
        await using var host = new AssetHost();
        for (var i = 0; i < 3; i++)
            host.Script.Verdicts.Enqueue(GeneratedAssetCheckResult.Fail("No."));
        await host.LookUpAsync("e1/summer");

        await host.DrainWithRetriesAsync();

        Assert.Equal(GeneratedAssetFailure.Check, (await host.LookUpAsync("e1/summer")).Failure);
    }

    [Fact]
    public async Task A_prompt_that_can_never_be_built_fails_at_once()
    {
        await using var host = new AssetHost();
        host.Script.PromptThrows = new Messaging.UnrecoverableMessageException("The competition has no arena.");
        await host.LookUpAsync("e1/summer");

        await host.DrainWithRetriesAsync();
        var failed = await host.LookUpAsync("e1/summer");

        Assert.Equal(0, host.Painter.Calls);
        Assert.Equal(GeneratedAssetFailure.Prompt, failed.Failure);
        Assert.Equal(1, (await host.FailedMessagesAsync()).Single().Attempts);
    }

    [Fact]
    public async Task A_spent_budget_fails_at_once_as_budget_and_new_keys_answer_off()
    {
        // One image costs 0.04, over the feature's hard limit of 0.03 a day.
        await using var host = new AssetHost(("Ai:Features:Arena:Budget:DayHard", "0.03"));
        await host.LookUpAsync("e1/summer");
        await host.LookUpAsync("e2/summer");

        await host.DrainWithRetriesAsync();

        Assert.Equal(1, host.Painter.Calls);
        var spent = await host.LookUpAsync(host.Store.Paths.Single().Contains("e1") ? "e2/summer" : "e1/summer");
        Assert.Equal(GeneratedAssetStatus.Failed, spent.Status);
        Assert.Equal(GeneratedAssetFailure.Budget, spent.Failure);
        Assert.Equal(1, (await host.FailedMessagesAsync()).Single().Attempts);

        var off = await host.LookUpAsync("e3/summer");
        Assert.Equal(GeneratedAssetStatus.Off, off.Status);
        Assert.Equal(AIOffReason.Budget, off.Off);
    }

    [Fact]
    public async Task The_providers_message_never_reaches_the_failure_record()
    {
        await using var host = new AssetHost();
        host.Painter.FailuresLeft = 3;
        await host.LookUpAsync("e1/summer");
        await host.DrainWithRetriesAsync();

        var record = await host.Store.GetFailureAsync("arena/v1/e1/summer", default);

        Assert.NotNull(record);
        Assert.DoesNotContain("sk-secret", record.ToString());
    }
}
