using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.Messaging;
using Plugin.Maui.Spine.Server.Messaging.InMemory;

namespace Plugin.Maui.Spine.Server.AI.Assets.Tests;

public sealed class EndpointTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(AssetHost.Start);
    private readonly ArenaScript _script = new();
    private readonly FakePainter _painter = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Features:Arena:Kind"] = "Image",
            ["Ai:Features:Arena:Provider"] = "painter",
            ["Ai:Features:Arena:Model"] = "paint-1",
            ["Ai:Features:Arena:Asset:Expose"] = "true",
            ["Ai:Features:Hidden:Kind"] = "Image",
            ["Ai:Features:Hidden:Provider"] = "painter",
            ["Ai:Features:Hidden:Model"] = "paint-1",
        });
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton(_script);
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai => ai.Image("painter", (_, _) => _painter).UseInMemoryUsageStore());
        builder.Services.AddSpineMessaging(m => m.UseInMemory().AddGeneratedAssets());
        builder.Services.AddSpineAIAssets(builder.Configuration.GetSection("Ai"), a =>
        {
            a.Asset<ArenaKey, ArenaPrompt>("Arena");
            a.Asset<ArenaKey, ArenaPrompt>("Hidden");
            a.UseInMemoryStore();
        });

        _app = builder.Build();
        _app.MapSpineAIAssets();
        await _app.StartAsync();
        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(AICaller.InstallationHeader, "phone-1");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Pending_is_202_with_retry_after_and_ready_is_200_with_the_url()
    {
        var pending = await _client.GetAsync("/ai/assets/Arena/e1/summer");

        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(15), pending.Headers.RetryAfter?.Delta);
        Assert.Equal("pending", (await JsonOf(pending)).GetProperty("status").GetString());

        await DrainAsync();
        var ready = await _client.GetAsync("/ai/assets/Arena/e1/summer");
        var body = await JsonOf(ready);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("ready", body.GetProperty("status").GetString());
        Assert.StartsWith("https://assets.invalid/arena/v1/e1/summer.png?v=", body.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Failed_is_502_with_the_reason_and_when_it_is_ordered_again()
    {
        _script.PromptThrows = new UnrecoverableMessageException("Gone.");
        await _client.GetAsync("/ai/assets/Arena/e1/summer");
        await DrainAsync();

        var failed = await _client.GetAsync("/ai/assets/Arena/e1/summer");
        var body = await JsonOf(failed);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal("failed", body.GetProperty("status").GetString());
        Assert.Equal("prompt", body.GetProperty("reason").GetString());
        Assert.Equal(TimeSpan.FromHours(1), failed.Headers.RetryAfter?.Delta);
    }

    [Theory]
    [InlineData("/ai/assets/Hidden/e1/summer")]
    [InlineData("/ai/assets/Nothing/e1/summer")]
    public async Task A_feature_that_is_not_exposed_answers_like_one_that_is_off(string path)
    {
        var answer = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("notConfigured", (await JsonOf(answer)).GetProperty("off").GetString());
    }

    [Fact]
    public async Task An_unknown_key_is_404_not_found()
    {
        var answer = await _client.GetAsync("/ai/assets/Arena/e9/summer");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("notFound", (await JsonOf(answer)).GetProperty("status").GetString());
    }

    private async Task DrainAsync()
    {
        var topology = _app.Services.GetRequiredService<MessageTopology>();
        var dispatcher = _app.Services.GetRequiredService<MessageDispatcher>();
        var transport = _app.Services.GetRequiredService<Messaging.InMemory.InMemoryTransport>();
        foreach (var lane in topology.GetSubscriber(GeneratedAssetMessagingExtensions.DefaultSubscriber).Lanes)
        {
            while ((await transport.ReceiveAsync(lane, 1, wait: false, default)).FirstOrDefault() is { } delivery)
            {
                await dispatcher.DispatchAsync(GeneratedAssetMessagingExtensions.DefaultSubscriber, delivery.Envelope, delivery.DeliveryCount, default);
                await delivery.CompleteAsync(default);
            }
        }
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
