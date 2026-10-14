using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.AI;
using Plugin.Maui.Spine.Server.AI.Assets;
using Plugin.Maui.Spine.Server.Messaging;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Xunit;
using ClientOptions = Plugin.Maui.Spine.AI.SpineAIOptions;

namespace Plugin.Maui.Spine.AI.Tests;

/// <summary>Cities the server's prompt knows.</summary>
public sealed class CityPrompt : IGeneratedAssetPrompt<string>
{
    public static Exception? Throws { get; set; }

    public ValueTask<bool> AcceptsAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(key is "visby" or "kiruna" or "skane/ystad");

    public ValueTask<ImageGenerationRequest> BuildAsync(string key, CancellationToken cancellationToken) =>
        Throws is { } e ? throw e : ValueTask.FromResult(new ImageGenerationRequest($"A postcard of {key}."));
}

internal sealed class FakePainter : IImageGenerator
{
    public int Calls { get; private set; }

    public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>The app's asset lookup against a real Spine.Server.AI.Assets host, so the wire contract is tested from both ends.</summary>
[Collection(nameof(CityPrompt))]
public sealed class AssetClientTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly FakePainter _painter = new();
    private readonly List<(string? Installation, string? Path)> _seen = [];
    private WebApplication _app = null!;
    private AIAssets _assets = null!;

    public async Task InitializeAsync()
    {
        CityPrompt.Throws = null;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Features:Postcard:Kind"] = "Image",
            ["Ai:Features:Postcard:Provider"] = "painter",
            ["Ai:Features:Postcard:Model"] = "paint-1",
            ["Ai:Features:Postcard:Asset:Expose"] = "true",
            ["Ai:Features:Postcard:Asset:OrdersPerMinute:Installation"] = "2",
            ["Ai:Features:Closed:Kind"] = "Image",
            ["Ai:Features:Closed:Provider"] = "painter",
            ["Ai:Features:Closed:Model"] = "paint-1",
            ["Ai:Features:Closed:Enabled"] = "false",
            ["Ai:Features:Closed:Asset:Expose"] = "true",
        });
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Image("painter", (_, _) => _painter);
            ai.UseInMemoryUsageStore();
            ai.Identify = request =>
            {
                _seen.Add((request.Headers["Spine-Installation"].ToString(), request.Path.Value));
                return ValueTask.FromResult(AICaller.Installation(request.Headers["Spine-Installation"].ToString()));
            };
        });
        builder.Services.AddSpineMessaging(m => m.UseInMemory().AddGeneratedAssets());
        builder.Services.AddSpineAIAssets(builder.Configuration.GetSection("Ai"), a =>
        {
            a.Asset<string, CityPrompt>("Postcard");
            a.Asset<string, CityPrompt>("Closed");
            a.UseInMemoryStore(new Uri("https://cdn.example.com/"));
        });

        _app = builder.Build();
        _app.MapSpineAIAssets("/ai");
        await _app.StartAsync();

        _assets = Assets(new Uri("http://localhost/ai"));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private AIAssets Assets(Uri? backend) => new(new SpineAIRuntime(
        new ClientOptions { Backend = backend, HttpHandler = _app.GetTestServer().CreateHandler() },
        _ => Task.FromResult<string?>("phone-1"), new InMemoryAIDeviceUsageStore(), time: _time));

    private async Task DrainAsync()
    {
        var topology = _app.Services.GetRequiredService<MessageTopology>();
        var dispatcher = _app.Services.GetRequiredService<MessageDispatcher>();
        var transport = _app.Services.GetRequiredService<InMemoryTransport>();
        foreach (var lane in topology.GetSubscriber(GeneratedAssetMessagingExtensions.DefaultSubscriber).Lanes)
        {
            while ((await transport.ReceiveAsync(lane, 1, wait: false, default)).FirstOrDefault() is { } delivery)
            {
                await dispatcher.DispatchAsync(GeneratedAssetMessagingExtensions.DefaultSubscriber, delivery.Envelope, delivery.DeliveryCount, default);
                await delivery.CompleteAsync(default);
            }
        }
    }

    [Fact]
    public async Task Pending_then_ready_with_the_url_and_the_installation_header()
    {
        var pending = await _assets.LookUpAsync("Postcard", "visby");
        await DrainAsync();
        var ready = await _assets.LookUpAsync("Postcard", "visby");

        Assert.Equal(AIAssetStatus.Pending, pending.Status);
        Assert.Equal(_time.GetUtcNow().AddSeconds(15), pending.RetryAfter);
        Assert.Equal(AIAssetStatus.Ready, ready.Status);
        Assert.StartsWith("https://cdn.example.com/postcard/v1/visby.png?v=", ready.Url?.ToString());
        Assert.All(_seen, s => Assert.Equal("phone-1", s.Installation));
    }

    [Fact]
    public async Task A_key_with_segments_keeps_its_slash_and_escapes_the_rest()
    {
        await _assets.LookUpAsync("Postcard", "skane/ystad");
        await DrainAsync();
        var ready = await _assets.LookUpAsync("Postcard", "skane/ystad");
        var unsafeKey = await _assets.LookUpAsync("Postcard", "skåne/ystad");

        Assert.Equal(AIAssetStatus.Ready, ready.Status);
        Assert.EndsWith("/postcard/v1/skane/ystad.png", ready.Url?.GetLeftPart(UriPartial.Path));
        // 'å' travels escaped, and the server's safe-path rule refuses it.
        Assert.Equal(AIAssetStatus.NotFound, unsafeKey.Status);
    }

    [Fact]
    public async Task Unknown_keys_off_features_and_failures_have_their_own_status()
    {
        var unknown = await _assets.LookUpAsync("Postcard", "atlantis");
        var off = await _assets.LookUpAsync("Closed", "visby");

        CityPrompt.Throws = new UnrecoverableMessageException("Gone.");
        await _assets.LookUpAsync("Postcard", "kiruna");
        await DrainAsync();
        var failed = await _assets.LookUpAsync("Postcard", "kiruna");

        Assert.Equal(AIAssetStatus.NotFound, unknown.Status);
        Assert.Equal(AIAssetStatus.Off, off.Status);
        Assert.Equal(AIOffReason.Disabled, off.Off);
        Assert.Equal(AIAssetStatus.Failed, failed.Status);
        Assert.Equal("prompt", failed.Failure);
        Assert.Equal(_time.GetUtcNow().AddHours(1), failed.RetryAfter);
    }

    [Fact]
    public async Task Too_many_orders_are_rate_limited_with_a_time_to_ask_again()
    {
        await _assets.LookUpAsync("Postcard", "visby");
        await _assets.LookUpAsync("Postcard", "kiruna");

        var limited = await _assets.LookUpAsync("Postcard", "skane/ystad");
        var pending = await _assets.LookUpAsync("Postcard", "visby");

        Assert.Equal(AIAssetStatus.RateLimited, limited.Status);
        Assert.True(limited.RetryAfter > _time.GetUtcNow());
        Assert.Equal(AIAssetStatus.Pending, pending.Status);
    }

    [Fact]
    public async Task A_missing_route_is_an_error_that_says_where_it_went()
    {
        var wrong = Assets(new Uri("http://localhost/nothing"));

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => wrong.LookUpAsync("Postcard", "visby"));

        Assert.Equal(HttpStatusCode.NotFound, e.StatusCode);
        Assert.Contains("http://localhost/nothing/assets/Postcard/visby", e.Message);
    }

    [Fact]
    public async Task No_backend_says_how_to_set_it()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Assets(null).LookUpAsync("Postcard", "visby"));

        Assert.Contains("Backend", e.Message);
    }

    [Fact]
    public async Task A_watch_asks_again_after_retry_after_and_ends_when_ready()
    {
        await using var watch = _assets.WatchAsync("Postcard", "visby").GetAsyncEnumerator();

        Assert.True(await watch.MoveNextAsync());
        Assert.Equal(AIAssetStatus.Pending, watch.Current.Status);

        var next = watch.MoveNextAsync().AsTask();
        await DrainAsync();
        _time.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(50);
        Assert.False(next.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AIAssetStatus.Ready, watch.Current.Status);
        Assert.False(await watch.MoveNextAsync());
        Assert.Equal(1, _painter.Calls);
    }

    [Fact]
    public async Task A_watch_ends_pending_after_its_longest_wait()
    {
        await using var watch = _assets.WatchAsync("Postcard", "visby", new AIAssetWatch { MaxWait = TimeSpan.FromSeconds(40) }).GetAsyncEnumerator();

        Assert.True(await watch.MoveNextAsync());
        var first = watch.Current;
        var next = watch.MoveNextAsync().AsTask();

        // Each step waits for the lookup the last one caused, so the fake clock never moves under a watch that is not waiting yet.
        for (var asked = 2; asked <= 3; asked++)
        {
            await WhenAskedAsync(asked - 1);
            _time.Advance(TimeSpan.FromSeconds(15));
        }
        await WhenAskedAsync(3);

        // Nothing changed after the first answer, so the watch ends without yielding again.
        Assert.False(await next.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(AIAssetStatus.Pending, first.Status);
        Assert.Equal(3, _seen.Count);
    }

    [Fact]
    public async Task An_empty_segment_is_not_found_without_asking()
    {
        Assert.Equal(AIAssetStatus.NotFound, (await _assets.LookUpAsync("Postcard", "/visby")).Status);
        Assert.Empty(_seen);
    }

    private async Task WhenAskedAsync(int count)
    {
        for (var i = 0; i < 200 && _seen.Count < count; i++)
            await Task.Delay(10);
        Assert.True(_seen.Count >= count, $"Expected {count} lookups, saw {_seen.Count}.");
        // The watch registers its delay right after the answer; give it the turn.
        await Task.Delay(20);
    }

    [Fact]
    public async Task A_watch_ends_at_failed_without_waiting_for_the_cool_down()
    {
        CityPrompt.Throws = new UnrecoverableMessageException("Gone.");
        await _assets.LookUpAsync("Postcard", "kiruna");
        await DrainAsync();

        var answers = new List<AIAsset>();
        await foreach (var asset in _assets.WatchAsync("Postcard", "kiruna"))
            answers.Add(asset);

        Assert.Equal(AIAssetStatus.Failed, Assert.Single(answers).Status);
    }

    [Fact]
    public async Task A_cancelled_watch_ends_with_operation_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        await using var watch = _assets.WatchAsync("Postcard", "visby", cancellationToken: cancel.Token).GetAsyncEnumerator();
        await watch.MoveNextAsync();

        var next = watch.MoveNextAsync().AsTask();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }
}
