using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Server.AI.Tests;

/// <summary>A provider that answers from a script and remembers what it was asked.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public string Answer { get; set; } = "Hej då.";
    public UsageDetails? Usage { get; set; } = new() { InputTokenCount = 1000, OutputTokenCount = 200 };

    /// <summary>The stream's text, one update per piece. Usage comes in an update of its own at the end.</summary>
    public string[] Pieces { get; set; } = ["Hej", " då", "."];

    public Exception? Throw { get; set; }
    public TaskCompletionSource? HoldStream { get; set; }

    public int Calls { get; private set; }
    public List<ChatMessage> LastMessages { get; private set; } = [];
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Remember(messages, options);
        if (Throw is { } e) throw e;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)) { Usage = Usage, ModelId = options?.ModelId });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Remember(messages, options);

        for (var i = 0; i < Pieces.Length; i++)
        {
            if (Throw is { } e && i == 1) throw e;
            yield return new ChatResponseUpdate(ChatRole.Assistant, Pieces[i]);
            if (i == 0 && HoldStream is { } hold) await hold.Task.WaitAsync(cancellationToken);
        }

        if (Usage is { } usage)
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(usage)] };
    }

    private void Remember(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        Calls++;
        LastMessages = messages.ToList();
        LastOptions = options;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>A provider that paints nothing and says it made one picture.</summary>
internal sealed class FakeImageGenerator : IImageGenerator
{
    public ImageGenerationOptions? LastOptions { get; private set; }

    public Task<ImageGenerationResponse> GenerateAsync(ImageGenerationRequest request, ImageGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1, 2, 3 }, "image/png")]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>A service provider with AddSpineAI on in-memory configuration, a fake provider and a fake clock.</summary>
internal sealed class TestHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    public const string Model = "fake-1";

    public TestHost(Dictionary<string, string?> settings, Action<SpineAIOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<TimeProvider>(Time);
        services?.Invoke(collection);
        collection.AddSpineAI(Configuration.GetSection("Ai"), ai =>
        {
            ai.Chat("fake", (_, _) => Chat);
            ai.Image("painter", (_, _) => Image);
            ai.UseUsageStore(_ => Store);
            configure?.Invoke(ai);
        });

        Services = collection.BuildServiceProvider();
    }

    /// <summary>One chat feature "Story" on the fake provider at 3 per million in and 15 per million out.</summary>
    public static Dictionary<string, string?> Settings(params (string Key, string? Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:Features:Story:Provider"] = "fake",
            ["Ai:Features:Story:Model"] = Model,
            ["Ai:Prices:fake:fake-1:InputPerMillion"] = "3",
            ["Ai:Prices:fake:fake-1:CachedInputPerMillion"] = "0.3",
            ["Ai:Prices:fake:fake-1:OutputPerMillion"] = "15",
        };

        foreach (var (key, value) in extra) settings[key] = value;
        return settings;
    }

    public IConfigurationRoot Configuration { get; }
    public FakeTimeProvider Time { get; } = new(Now);
    public FakeChatClient Chat { get; } = new();
    public FakeImageGenerator Image { get; } = new();
    public InMemoryAIUsageStore Store { get; } = new();
    public ServiceProvider Services { get; }

    public IAIFeatures Features => Services.GetRequiredService<IAIFeatures>();

    public IChatClient Story(AICaller? caller = null) =>
        Features.Chat("Story", caller ?? AICaller.Installation("phone-1")) ?? throw new InvalidOperationException("Story is off.");

    public void Dispose() => Services.Dispose();
}
