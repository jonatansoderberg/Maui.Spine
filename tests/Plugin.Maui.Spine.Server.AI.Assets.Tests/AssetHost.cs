using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.Messaging;
using Plugin.Maui.Spine.Server.Messaging.InMemory;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.AI.Assets.Tests;

/// <summary>A competition's picture for a season: <c>e1/summer</c>.</summary>
public sealed record ArenaKey(string Event, string Season) : IParsable<ArenaKey>
{
    public override string ToString() => $"{Event}/{Season}";

    public static ArenaKey Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var key) ? key : throw new FormatException($"'{s}' is not an arena key.");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out ArenaKey result)
    {
        result = s?.Split('/') is [{ Length: > 0 } e, { Length: > 0 } season] ? new ArenaKey(e, season) : null;
        return result is not null;
    }
}

/// <summary>What the prompt and the check know and say, set by each test.</summary>
public sealed class ArenaScript
{
    public HashSet<string> KnownEvents { get; } = ["e1", "e2", "e3", "e4", "e5"];
    public ConcurrentQueue<GeneratedAssetCheckResult> Verdicts { get; } = new();
    public Exception? PromptThrows { get; set; }
    public List<ArenaKey> Built { get; } = [];
}

public sealed class ArenaPrompt(ArenaScript script) : IGeneratedAssetPrompt<ArenaKey>
{
    public ValueTask<bool> AcceptsAsync(ArenaKey key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(script.KnownEvents.Contains(key.Event));

    public ValueTask<ImageGenerationRequest> BuildAsync(ArenaKey key, CancellationToken cancellationToken)
    {
        if (script.PromptThrows is { } e) throw e;
        script.Built.Add(key);
        return ValueTask.FromResult(new ImageGenerationRequest($"An arena in {key.Season}."));
    }
}

public sealed class ArenaCheck(ArenaScript script) : IGeneratedAssetCheck<ArenaKey>
{
    public ValueTask<GeneratedAssetCheckResult> CheckAsync(ArenaKey key, DataContent image, CancellationToken cancellationToken) =>
        ValueTask.FromResult(script.Verdicts.TryDequeue(out var verdict) ? verdict : GeneratedAssetCheckResult.Pass);
}

/// <summary>A painter that answers with a tiny PNG, or throws for as many calls as asked.</summary>
public sealed class FakePainter : IImageGenerator
{
    public int Calls { get; private set; }
    public int FailuresLeft { get; set; }
    public ImageGenerationOptions? LastOptions { get; private set; }
    public byte[] Png { get; set; } = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastOptions = options;
        if (FailuresLeft != 0)
        {
            FailuresLeft--;
            throw new HttpRequestException("The vendor said: quota for key sk-secret exceeded.");
        }

        return Task.FromResult(new ImageGenerationResponse([new DataContent(Png, "image/png")]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Server.AI, messaging in memory and the assets, on a fake clock, with a <see cref="DrainAsync"/> that does what the
/// pump does, so tests are deterministic.
/// </summary>
public sealed class AssetHost : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    public AssetHost(params (string Key, string? Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:Features:Arena:Kind"] = "Image",
            ["Ai:Features:Arena:Provider"] = "painter",
            ["Ai:Features:Arena:Model"] = "paint-1",
            ["Ai:Features:Arena:Asset:Version"] = "1",
            ["Ai:Features:Arena:Asset:Expose"] = "true",
            ["Ai:Prices:painter:paint-1:PerImage"] = "0.04",
        };
        foreach (var (key, value) in extra) settings[key] = value;
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton(Script);
        services.AddSpineAI(Configuration.GetSection("Ai"), ai => ai.Image("painter", (_, _) => Painter).UseInMemoryUsageStore());
        services.AddSpineMessaging(m => m.UseInMemory().AddGeneratedAssets());
        services.AddSpineAIAssets(Configuration.GetSection("Ai"), a =>
        {
            a.Asset<ArenaKey, ArenaPrompt>("Arena").Check<ArenaCheck>();
            a.UseInMemoryStore();
        });
        Services = services.BuildServiceProvider();
    }

    public FakeTimeProvider Time { get; } = new(Start);
    public IConfigurationRoot Configuration { get; }
    public ArenaScript Script { get; } = new();
    public FakePainter Painter { get; } = new();
    public ServiceProvider Services { get; }

    public IGeneratedAssets Assets => Services.GetRequiredService<IGeneratedAssets>();
    public InMemoryGeneratedAssetStore Store => (InMemoryGeneratedAssetStore)Services.GetRequiredService<IGeneratedAssetStore>();
    public InMemoryTransport Transport => Services.GetRequiredService<InMemoryTransport>();
    public IFailedMessages Failed => Services.GetRequiredService<IFailedMessages>();

    public static AICaller Phone(int n = 1) => AICaller.Installation($"phone-{n}");

    public async Task StartAsync()
    {
        foreach (var hosted in Services.GetServices<IHostedService>())
            await hosted.StartAsync(default);
    }

    public Task<GeneratedAsset> LookUpAsync(string key, AICaller? caller = null) =>
        Assets.LookUpAsync("Arena", key, caller ?? Phone());

    /// <summary>Changes a setting and raises the reload, as a changed appsettings.json would.</summary>
    public void Set(string key, string? value)
    {
        Configuration[key] = value;
        Configuration.Reload();
    }

    /// <summary>Receives and dispatches the assets' queue until it is empty at the current time.</summary>
    /// <returns>How many deliveries it handled.</returns>
    public async Task<int> DrainAsync()
    {
        var topology = Services.GetRequiredService<MessageTopology>();
        var dispatcher = Services.GetRequiredService<MessageDispatcher>();
        var count = 0;
        foreach (var lane in topology.GetSubscriber(GeneratedAssetMessagingExtensions.DefaultSubscriber).Lanes)
        {
            while ((await Transport.ReceiveAsync(lane, 1, wait: false, default)).FirstOrDefault() is { } delivery)
            {
                await dispatcher.DispatchAsync(GeneratedAssetMessagingExtensions.DefaultSubscriber, delivery.Envelope, delivery.DeliveryCount, default);
                await delivery.CompleteAsync(default);
                count++;
            }
        }
        return count;
    }

    /// <summary>Drains, moves the clock past the longest retry wait, and drains again, <paramref name="rounds"/> times.</summary>
    public async Task<int> DrainWithRetriesAsync(int rounds = 4)
    {
        var count = 0;
        for (var i = 0; i < rounds; i++)
        {
            count += await DrainAsync();
            Time.Advance(TimeSpan.FromMinutes(6));
        }
        return count;
    }

    public async Task<List<FailedMessage>> FailedMessagesAsync()
    {
        var list = new List<FailedMessage>();
        await foreach (var message in Failed.ListAsync())
            list.Add(message);
        return list;
    }

    public ValueTask DisposeAsync() => Services.DisposeAsync();
}
