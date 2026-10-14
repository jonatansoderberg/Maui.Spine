namespace Plugin.Maui.Spine.Server.AI.Assets.Tests;

public sealed class LookupTests
{
    [Fact]
    public async Task A_missing_image_is_ordered_once_made_once_and_then_ready_with_a_cache_buster()
    {
        await using var host = new AssetHost();
        await host.StartAsync();

        var first = await host.LookUpAsync("e1/summer");
        var again = await host.LookUpAsync("e1/summer", AssetHost.Phone(2));

        Assert.Equal(GeneratedAssetStatus.Pending, first.Status);
        Assert.Equal(AssetHost.Start + TimeSpan.FromSeconds(15), first.RetryAfter);
        Assert.Equal(GeneratedAssetStatus.Pending, again.Status);
        Assert.Equal(1, await host.DrainAsync());
        Assert.Equal(1, host.Painter.Calls);

        var ready = await host.LookUpAsync("e1/summer");

        Assert.Equal(GeneratedAssetStatus.Ready, ready.Status);
        Assert.Equal($"https://assets.invalid/arena/v1/e1/summer.png?v={AssetHost.Start.ToUnixTimeSeconds()}", ready.Url?.ToString());
        Assert.Equal("image/png", host.Store.Read("arena/v1/e1/summer.png")?.ContentType);
    }

    [Fact]
    public async Task A_burst_of_lookups_on_two_servers_orders_once()
    {
        await using var host = new AssetHost();

        await host.LookUpAsync("e1/summer");
        // A second server instance has no memory of the first's order; the publish's deduplication catches it.
        var other = new GeneratedAssets(
            host.Services.GetRequiredService<SpineAIAssetsOptions>(), host.Services.GetRequiredService<AssetSettings>(),
            host.Store, host.Services.GetRequiredService<IAIFeatures>(), host.Services.GetRequiredService<Messaging.IMessagePublisher>(),
            host.Services.GetRequiredService<IServiceScopeFactory>(), host.Time, NullLogger<GeneratedAssets>.Instance);
        var answer = await other.LookUpAsync("Arena", "e1/summer", AssetHost.Phone(2));

        Assert.Equal(GeneratedAssetStatus.Pending, answer.Status);
        Assert.Equal(1, await host.DrainAsync());
    }

    [Fact]
    public async Task A_version_bump_remakes_lazily_and_keeps_the_old_version()
    {
        await using var host = new AssetHost();
        await host.LookUpAsync("e1/summer");
        await host.LookUpAsync("e2/winter");
        await host.DrainAsync();

        host.Set("Ai:Features:Arena:Asset:Version", "2");
        var bumped = await host.LookUpAsync("e1/summer");
        await host.DrainAsync();

        Assert.Equal(GeneratedAssetStatus.Pending, bumped.Status);
        Assert.Equal(3, host.Painter.Calls);
        Assert.Contains("arena/v2/e1/summer.png", host.Store.Paths);
        Assert.DoesNotContain("arena/v2/e2/winter.png", host.Store.Paths);
        Assert.Contains("arena/v1/e2/winter.png", host.Store.Paths);
        Assert.Contains("/v2/", (await host.LookUpAsync("e1/summer")).Url?.ToString());
    }

    [Fact]
    public async Task An_order_for_an_old_version_is_dropped()
    {
        await using var host = new AssetHost();
        await host.LookUpAsync("e1/summer");

        host.Set("Ai:Features:Arena:Asset:Version", "2");
        await host.DrainAsync();

        Assert.Equal(0, host.Painter.Calls);
        Assert.Empty(host.Store.Paths);
    }

    [Theory]
    [InlineData("e9/summer")]      // the prompt does not know e9
    [InlineData("e1")]             // does not parse
    [InlineData("e1/../secrets")]  // three segments, so no ArenaKey
    [InlineData("e1/.hidden")]     // a segment must start with a letter or digit
    [InlineData("")]
    public async Task Unknown_unparseable_and_unsafe_keys_are_not_found_and_order_nothing(string key)
    {
        await using var host = new AssetHost();

        var answer = await host.LookUpAsync(key);

        Assert.Equal(GeneratedAssetStatus.NotFound, answer.Status);
        Assert.Equal(0, await host.DrainAsync());
    }

    [Fact]
    public async Task An_unknown_feature_is_not_found()
    {
        await using var host = new AssetHost();

        Assert.Equal(GeneratedAssetStatus.NotFound, (await host.Assets.LookUpAsync("Story", "e1/summer", AssetHost.Phone())).Status);
    }

    [Fact]
    public async Task The_typed_lookup_uses_the_keys_text()
    {
        await using var host = new AssetHost();

        await host.Assets.LookUpAsync("arena", new ArenaKey("e1", "summer"), AICaller.System);
        await host.DrainAsync();

        Assert.Equal(GeneratedAssetStatus.Ready, (await host.LookUpAsync("e1/summer")).Status);
    }

    [Fact]
    public async Task Orders_are_limited_per_caller_but_lookups_of_pending_images_are_not()
    {
        await using var host = new AssetHost(("Ai:Features:Arena:Asset:OrdersPerMinute:Installation", "2"));

        Assert.Equal(GeneratedAssetStatus.Pending, (await host.LookUpAsync("e1/summer")).Status);
        Assert.Equal(GeneratedAssetStatus.Pending, (await host.LookUpAsync("e2/summer")).Status);
        var limited = await host.LookUpAsync("e3/summer");
        var pending = await host.LookUpAsync("e1/summer");
        var other = await host.LookUpAsync("e3/summer", AssetHost.Phone(2));
        var server = await host.Assets.LookUpAsync("Arena", "e4/summer", AICaller.System);

        Assert.Equal(GeneratedAssetStatus.RateLimited, limited.Status);
        Assert.NotNull(limited.RetryAfter);
        Assert.Equal(GeneratedAssetStatus.Pending, pending.Status);
        Assert.Equal(GeneratedAssetStatus.Pending, other.Status);
        Assert.Equal(GeneratedAssetStatus.Pending, server.Status);
        Assert.Equal(4, await host.DrainAsync());
    }

    [Fact]
    public async Task An_archived_image_is_served_while_the_feature_is_off_but_nothing_new_is_ordered()
    {
        await using var host = new AssetHost();
        await host.LookUpAsync("e1/summer");
        await host.DrainAsync();

        host.Set("Ai:Features:Arena:Enabled", "false");

        Assert.Equal(GeneratedAssetStatus.Ready, (await host.LookUpAsync("e1/summer")).Status);
        var off = await host.LookUpAsync("e2/summer");
        Assert.Equal(GeneratedAssetStatus.Off, off.Status);
        Assert.Equal(AIOffReason.Disabled, off.Off);
        Assert.Equal(0, await host.DrainAsync());
    }

    [Fact]
    public async Task A_configured_media_type_is_asked_for_and_names_the_file()
    {
        await using var host = new AssetHost(("Ai:Features:Arena:Asset:MediaType", "image/webp"));
        await host.LookUpAsync("e1/summer");
        await host.DrainAsync();

        Assert.Equal("image/webp", host.Painter.LastOptions?.MediaType);
        Assert.Contains("arena/v1/e1/summer.webp", host.Store.Paths);
    }

    [Fact]
    public async Task An_unknown_media_type_stops_the_host_at_startup()
    {
        await using var host = new AssetHost(("Ai:Features:Arena:Asset:MediaType", "image/tiff"));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(host.StartAsync);
        Assert.Contains("image/tiff", e.Message);
    }
}
