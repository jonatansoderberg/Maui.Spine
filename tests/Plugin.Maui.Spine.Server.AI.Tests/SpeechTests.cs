using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

/// <summary>A speech provider that answers a second of silence with two visemes and remembers what it was asked.</summary>
internal sealed class FakeSpeechProvider : IAISpeechProvider
{
    public List<AISpeechRequest> Requests { get; } = [];
    public Exception? Throw { get; set; }

    public Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Throw is { } e) throw e;
        return Task.FromResult(new AISpeech(new byte[48_000], 24_000,
            [new AISpeechViseme(0, TimeSpan.Zero), new AISpeechViseme(21, TimeSpan.FromMilliseconds(412.5))]));
    }
}

public sealed class SpeechTests : IAsyncLifetime
{
    private readonly FakeSpeechProvider _provider = new();
    private readonly InMemoryAIUsageStore _store = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static readonly Dictionary<string, string?> Settings = new(TestHost.Settings(
        ("Ai:Features:Read:Kind", "Speech"),
        ("Ai:Features:Read:Provider", "voice"),
        ("Ai:Features:Read:Model", "neural"),
        ("Ai:Features:Read:Expose", "true"),
        ("Ai:Features:Read:Speech:Voice", "sv-SE-SofieNeural"),
        ("Ai:Features:Read:Speech:MaxCharacters", "20"),
        ("Ai:Features:Hidden:Kind", "Speech"),
        ("Ai:Features:Hidden:Provider", "voice"),
        ("Ai:Features:Hidden:Model", "neural"),
        ("Ai:Prices:voice:neural:PerMillionCharacters", "16")));

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestHost.Now));
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Speech("voice", _ => _provider);
            ai.UseUsageStore(_ => _store);
        });

        _app = builder.Build();
        _app.MapSpineAI();
        await _app.StartAsync();
        _http = _app.GetTestClient();
        _http.DefaultRequestHeaders.Add(AICaller.InstallationHeader, "phone-1");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private Task<HttpResponseMessage> Post(string feature, string body) =>
        _http.PostAsync($"/ai/speech/{feature}", new StringContent(body, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Text_comes_back_as_pcm_with_its_visemes()
    {
        var response = await Post("Read", """{"text":"Hej Nova","voice":"other"}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(24_000, (int?)answer["sampleRate"]);
        Assert.Equal(48_000, Convert.FromBase64String((string)answer["audio"]!).Length);
        Assert.Equal(1000, (double?)answer["durationMs"]);
        Assert.Equal(21, (int?)answer["visemes"]![1]!["id"]);
        Assert.Equal(412.5, (double?)answer["visemes"]![1]!["offsetMs"]);

        // The voice is the feature's; the app sends only the text.
        var asked = Assert.Single(_provider.Requests);
        Assert.Equal("Hej Nova", asked.Text);
        Assert.Equal("sv-SE-SofieNeural", asked.Options.Voice);
    }

    [Fact]
    public async Task Each_call_is_priced_by_its_characters()
    {
        await Post("Read", """{"text":"Hej Nova"}""");

        var record = Assert.Single(_store.Records);
        Assert.Equal("Read", record.Feature);
        Assert.Equal(8 * 16m / 1_000_000m, record.Cost);
        Assert.True(record.Complete);
    }

    [Fact]
    public async Task A_text_over_the_limit_is_413_and_never_reaches_the_provider()
    {
        var response = await Post("Read", """{"text":"Det här är längre än tjugo tecken."}""");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_provider.Requests);
    }

    [Theory]
    [InlineData("""{"text":""}""")]
    [InlineData("""{"words":"Hej"}""")]
    [InlineData("not json")]
    public async Task A_body_without_text_is_400(string body) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("Read", body)).StatusCode);

    [Theory]
    [InlineData("Hidden")]
    [InlineData("Nothing")]
    public async Task Only_exposed_speech_features_answer(string feature)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Post(feature, """{"text":"Hej"}""")).StatusCode);
        Assert.Empty(_provider.Requests);
    }

    [Fact]
    public async Task A_provider_failure_is_502_without_its_message()
    {
        _provider.Throw = new HttpRequestException("401 from wss://swedencentral.tts.speech.microsoft.com: key abc123");

        var response = await Post("Read", """{"text":"Hej"}""");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("abc123", await response.Content.ReadAsStringAsync());
        Assert.NotNull(Assert.Single(_store.Records).Error);
    }
}

public sealed class SpeechProviderTests
{
    private sealed class Recorder(HttpStatusCode status, byte[] answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new ByteArrayContent(answer) };
        }
    }

    private static IConfiguration Section(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static AISpeechRequest Request(AISpeechOptions options) => new("Read", "gpt-4o-mini-tts", "Hej & välkommen <3", options, AICaller.Installation("phone-1"));

    [Fact]
    public async Task OpenAI_asks_for_raw_pcm_with_the_feature_voice()
    {
        var recorder = new Recorder(HttpStatusCode.OK, new byte[4800]);
        var provider = AISpeechProviders.OpenAI(Section(("ApiKey", "sk-test")), new HttpClient(recorder));

        var speech = await provider.SynthesizeAsync(Request(new AISpeechOptions { Voice = "marin", Instructions = "Lugnt.", Speed = 1.2f }));

        Assert.Equal("https://api.openai.com/v1/audio/speech", recorder.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", recorder.Request.Headers.Authorization!.ToString());
        var body = JsonNode.Parse(recorder.Body!)!;
        Assert.Equal("gpt-4o-mini-tts", (string?)body["model"]);
        Assert.Equal("pcm", (string?)body["response_format"]);
        Assert.Equal("marin", (string?)body["voice"]);
        Assert.Equal("Lugnt.", (string?)body["instructions"]);
        Assert.Equal(24_000, speech.SampleRate);
        Assert.Equal(TimeSpan.FromMilliseconds(100), speech.Duration);
        Assert.Empty(speech.Visemes);
    }

    [Fact]
    public async Task Azure_speaks_the_websocket_protocol_and_returns_audio_with_visemes()
    {
        var received = new List<string>();
        string? key = null;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.UseWebSockets();
        app.Map("/tts/cognitiveservices/websocket/v1", async (HttpContext context) =>
        {
            key = context.Request.Headers["Ocp-Apim-Subscription-Key"];
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[64 * 1024];
            string? requestId = null;
            for (var i = 0; i < 3; i++)
            {
                var r = await socket.ReceiveAsync(buffer, CancellationToken.None);
                var text = Encoding.UTF8.GetString(buffer, 0, r.Count);
                received.Add(text);
                requestId = text.Split("\r\n").First(l => l.StartsWith("X-RequestId:")).Split(':')[1].Trim();
            }

            async Task Text(string path, string body) => await socket.SendAsync(
                Encoding.UTF8.GetBytes($"X-RequestId:{requestId}\r\nPath:{path}\r\nContent-Type:application/json\r\n\r\n{body}"),
                WebSocketMessageType.Text, true, CancellationToken.None);

            async Task Audio(byte[] pcm)
            {
                var headers = Encoding.ASCII.GetBytes($"X-RequestId:{requestId}\r\nPath:audio\r\nX-StreamId:1\r\n");
                var message = new byte[2 + headers.Length + pcm.Length];
                BinaryPrimitives.WriteInt16BigEndian(message, (short)headers.Length);
                headers.CopyTo(message, 2);
                pcm.CopyTo(message, 2 + headers.Length);
                await socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
            }

            await Text("turn.start", """{"context":{"serviceTag":"x"}}""");
            await Text("audio.metadata", """{"Metadata":[{"Type":"Viseme","Data":{"Offset":500000,"VisemeId":21,"AnimationChunk":"","IsLastAnimation":false}},{"Type":"WordBoundary","Data":{"Offset":10000,"Duration":100,"text":{"Text":"Hej","Length":3,"BoundaryType":"WordBoundary"}}}]}""");
            await Audio(new byte[2400]);
            await Text("audio.metadata", """{"Metadata":[{"Type":"Viseme","Data":{"Offset":0,"VisemeId":0}}]}""");
            await Audio(new byte[2400]);
            await Text("turn.end", "{}");
            await socket.ReceiveAsync(buffer, CancellationToken.None);
        });
        await app.StartAsync();
        var address = new Uri(app.Urls.First());

        var provider = AISpeechProviders.AzureSpeech(Section(
            ("ApiKey", "azure-key"), ("Endpoint", $"ws://127.0.0.1:{address.Port}/tts/cognitiveservices/websocket/v1")));
        var speech = await provider.SynthesizeAsync(Request(new AISpeechOptions { Voice = "sv-SE-SofieNeural" }));

        Assert.Equal("azure-key", key);
        Assert.Equal(["speech.config", "synthesis.context", "ssml"], received.Select(m => m.Split("\r\n")[0]["Path: ".Length..]));
        var context = JsonNode.Parse(received[1].Split("\r\n\r\n")[1])!;
        Assert.True((bool)context["synthesis"]!["audio"]!["metadataOptions"]!["visemeEnabled"]!);
        Assert.Equal("raw-24khz-16bit-mono-pcm", (string?)context["synthesis"]!["audio"]!["outputFormat"]);
        Assert.Contains("<voice name='sv-SE-SofieNeural'>Hej &amp; välkommen &lt;3</voice>", received[2]);
        Assert.Contains("xml:lang='sv-SE'", received[2]);

        Assert.Equal(4800, speech.Pcm.Length);
        Assert.Equal(24_000, speech.SampleRate);
        Assert.Equal([(0, TimeSpan.Zero), (21, TimeSpan.FromMilliseconds(50))], speech.Visemes.Select(v => (v.Id, v.Offset)));
    }
}
