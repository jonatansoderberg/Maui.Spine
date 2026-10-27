using System.Net.Http.Headers;
using System.Net.Http.Json;
using Anthropic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using Plugin.Maui.Spine.Server.AI;

namespace Plugin.Maui.Spine.Voice.LiveTests;

/// <summary>
/// The push sample server's AI features, hosted in the test on a free port with the keys from the environment: typed
/// chat (Assistant), Realtime (Voice), GPT-Live (VoiceLive), and a Claude pipeline (Claude, Transcribe, Read).
/// </summary>
internal sealed class LiveServer : IAsyncDisposable
{
    private const string Prompt = "Du är en vänlig röstassistent. Svara kort, på svenska. Klockan får du med funktionen show_time.";

    private readonly WebApplication _app;

    private LiveServer(WebApplication app, Uri prefix)
    {
        _app = app;
        Prefix = prefix;
    }

    /// <summary>The prefix given to MapSpineAI and MapSpineRealtimeSessions.</summary>
    public Uri Prefix { get; }

    /// <summary>How often the server's own show_time ran.</summary>
    public int ServerClock => _serverClock;

    private static int _serverClock;

    public static async Task<LiveServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(Settings());
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
            ai.Chat("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetChatClient(model).AsIChatClient());
            ai.Realtime("openai", AIRealtimeProviders.OpenAI);
            ai.Speech("openai", AISpeechProviders.OpenAI);
            ai.Transcription("openai", AITranscriptionProviders.OpenAI);
            ai.UseInMemoryUsageStore();
            // The phone's clock is the one asked for; the server has one too, so a model that runs it here is seen.
            ai.Functions.Add(SpineAIFunction.Create("show_time", "Says what time it is on the user's phone.", () =>
            {
                Interlocked.Increment(ref _serverClock);
                return "14:37";
            }), SpineAIFunctionSides.Both);
        });

        var app = builder.Build();
        app.MapSpineAI("/ai");
        app.UseWebSockets();
        app.MapSpineRealtimeSessions("/ai");
        await app.StartAsync();
        return new LiveServer(app, new Uri($"{app.Urls.First()}/ai"));
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary><paramref name="text"/> spoken by OpenAI's voice, as PCM16 at 24 kHz.</summary>
    public static async Task<byte[]> SpeakAsync(string text)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech")
        {
            Content = JsonContent.Create(new { model = "gpt-4o-mini-tts", voice = "alloy", input = text, response_format = "pcm", instructions = "Tala tydligt, på svenska." }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable(LiveFactAttribute.OpenAI));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static Dictionary<string, string?> Settings()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:Providers:openai:ApiKey"] = Environment.GetEnvironmentVariable(LiveFactAttribute.OpenAI),
            ["Ai:Providers:anthropic:ApiKey"] = Environment.GetEnvironmentVariable(LiveFactAttribute.Anthropic),
            ["Ai:Currency"] = "USD",
            ["Ai:Prices:openai:gpt-5.4-nano:InputPerMillion"] = "0.05",
            ["Ai:Prices:openai:gpt-5.4-nano:OutputPerMillion"] = "0.4",
            ["Ai:Prices:openai:gpt-realtime-2.1-mini:PerMinute"] = "0.05",
            ["Ai:Prices:openai:gpt-live-1:PerMinute"] = "0.05",
            ["Ai:Prices:openai:gpt-6-luna:InputPerMillion"] = "0.1",
            ["Ai:Prices:openai:gpt-6-luna:OutputPerMillion"] = "0.5",
            ["Ai:Prices:openai:gpt-4o-mini-transcribe:PerMinute"] = "0.003",
            ["Ai:Prices:openai:gpt-4o-mini-tts:PerMillionCharacters"] = "15",
            ["Ai:Prices:anthropic:claude-haiku-5-5:InputPerMillion"] = "1",
            ["Ai:Prices:anthropic:claude-haiku-5-5:OutputPerMillion"] = "5",
            ["Ai:Budget:DayHard"] = "2",
        };

        void Feature(string name, params (string Key, string Value)[] values)
        {
            settings[$"Ai:Features:{name}:Expose"] = "true";
            settings[$"Ai:Features:{name}:Callers"] = "Any";
            foreach (var (key, value) in values)
                settings[$"Ai:Features:{name}:{key}"] = value;
        }

        Feature("Assistant", ("Provider", "openai"), ("Model", "gpt-5.4-nano"), ("SystemPrompt", Prompt), ("Functions:0", "show_time"));
        Feature("Voice", ("Kind", "Realtime"), ("Provider", "openai"), ("Model", "gpt-realtime-2.1-mini"), ("SystemPrompt", Prompt),
            ("Functions:0", "show_time"), ("Realtime:Voice", "marin"), ("Realtime:TurnDetection", "semantic_vad"),
            ("Realtime:TranscriptionModel", "gpt-4o-mini-transcribe"), ("Realtime:Language", "sv"), ("Realtime:MaxMinutes", "2"));
        Feature("VoiceLive", ("Kind", "Realtime"), ("Provider", "openai"), ("Model", "gpt-live-1"),
            ("SystemPrompt", "Du är en vänlig röstassistent. Prata kort och naturligt, på svenska."), ("Functions:0", "show_time"),
            ("Realtime:Protocol", "Live"), ("Realtime:Voice", "marin"), ("Realtime:MaxMinutes", "2"),
            ("Realtime:Backend:Model", "gpt-6-luna"), ("Realtime:Backend:Reasoning", "low"),
            ("Realtime:Backend:Instructions", "Du hjälper röstassistenten. Svara kort och sakligt på svenska. Använd funktionen för klockan."));
        Feature("Claude", ("Provider", "anthropic"), ("Model", "claude-haiku-5-5"), ("MaxOutputTokens", "400"), ("Reasoning", "None"),
            ("SystemPrompt", Prompt + " Det du skriver läses upp: hela meningar, utan listor eller markdown."), ("Functions:0", "show_time"));
        Feature("Transcribe", ("Kind", "Transcription"), ("Provider", "openai"), ("Model", "gpt-4o-mini-transcribe"), ("Transcription:Language", "sv-SE"));
        Feature("Read", ("Kind", "Speech"), ("Provider", "openai"), ("Model", "gpt-4o-mini-tts"), ("Speech:Voice", "marin"));
        return settings;
    }
}
