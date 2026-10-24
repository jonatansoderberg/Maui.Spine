using System.Buffers.Binary;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

/// <summary>A transcription provider that answers a fixed sentence and remembers what it was sent.</summary>
internal sealed class FakeTranscriptionProvider : IAITranscriptionProvider
{
    public List<AITranscriptionRequest> Requests { get; } = [];

    public Task<AITranscription> TranscribeAsync(AITranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(new AITranscription("Vad är klockan?"));
    }
}

/// <summary>A speech provider that streams three pieces, holding the second until told to go on.</summary>
internal sealed class FakeStreamingSpeechProvider : IAIStreamingSpeechProvider
{
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int SampleRate => 24_000;

    public Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AISpeech(new byte[3 * 4800], SampleRate, []));

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamAsync(AISpeechRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new byte[4800];
        await Release.Task.WaitAsync(cancellationToken);
        yield return new byte[4800];
        yield return new byte[4800];
    }
}

public sealed class PipelineServerTests : IAsyncLifetime
{
    private readonly FakeTranscriptionProvider _transcriber = new();
    private readonly FakeStreamingSpeechProvider _streamer = new();
    private readonly FakeSpeechProvider _whole = new();
    private readonly InMemoryAIUsageStore _store = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private static readonly Dictionary<string, string?> Settings = new(TestHost.Settings(
        ("Ai:Features:Transcribe:Kind", "Transcription"),
        ("Ai:Features:Transcribe:Provider", "ears"),
        ("Ai:Features:Transcribe:Model", "transcribe-1"),
        ("Ai:Features:Transcribe:Expose", "true"),
        ("Ai:Features:Transcribe:Transcription:Language", "sv-SE"),
        ("Ai:Features:Transcribe:Transcription:MaxSeconds", "5"),
        ("Ai:Features:Hidden:Kind", "Transcription"),
        ("Ai:Features:Hidden:Provider", "ears"),
        ("Ai:Features:Hidden:Model", "transcribe-1"),
        ("Ai:Features:Stream:Kind", "Speech"),
        ("Ai:Features:Stream:Provider", "streamer"),
        ("Ai:Features:Stream:Model", "tts-1"),
        ("Ai:Features:Stream:Expose", "true"),
        ("Ai:Features:Whole:Kind", "Speech"),
        ("Ai:Features:Whole:Provider", "voice"),
        ("Ai:Features:Whole:Model", "neural"),
        ("Ai:Features:Whole:Expose", "true"),
        ("Ai:Prices:ears:transcribe-1:PerMinute", "0.006"),
        ("Ai:Prices:streamer:tts-1:PerMillionCharacters", "15")));

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestHost.Now));
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Transcription("ears", _ => _transcriber);
            ai.Speech("streamer", _ => _streamer);
            ai.Speech("voice", _ => _whole);
            ai.UseUsageStore(_ => _store);
        });

        _app = builder.Build();
        _app.MapSpineAI();
        await _app.StartAsync();
        _http = _app.GetTestClient();
        _http.DefaultRequestHeaders.Add(AICaller.InstallationHeader, "phone-1");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>A WAV file of <paramref name="seconds"/> of silence: PCM16, mono, 24 kHz.</summary>
    internal static byte[] Wav(double seconds)
    {
        var data = (int)(seconds * 24_000) * 2;
        var wav = new byte[44 + data];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + data);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 24_000);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 48_000);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), data);
        return wav;
    }

    private Task<HttpResponseMessage> Transcribe(string feature, byte[] body) =>
        _http.PostAsync($"/ai/transcribe/{feature}", new ByteArrayContent(body) { Headers = { ContentType = new("audio/wav") } });

    [Fact]
    public async Task Audio_comes_back_as_text_and_is_metered_by_its_minutes()
    {
        var response = await Transcribe("Transcribe", Wav(3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Vad är klockan?", (string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["text"]);
        var asked = Assert.Single(_transcriber.Requests);
        Assert.Equal("sv-SE", asked.Options.Language);
        Assert.Equal(AICaller.Installation("phone-1"), asked.Caller);

        var records = new List<AIUsageRecord>();
        await foreach (var record in _store.QueryAsync(TestHost.Now.AddDays(-1), TestHost.Now.AddDays(1), "Transcribe")) records.Add(record);
        var recorded = Assert.Single(records);
        Assert.Equal(0.006m * 3 / 60, recorded.Cost);
        Assert.True(recorded.Complete);
    }

    [Fact]
    public async Task Audio_longer_than_the_feature_allows_is_413()
    {
        var response = await Transcribe("Transcribe", Wav(6));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(_transcriber.Requests);
    }

    [Theory]
    [InlineData("Hidden")]
    [InlineData("Nothing")]
    [InlineData("Stream")]
    public async Task Only_exposed_transcription_features_answer(string feature)
    {
        var response = await Transcribe(feature, Wav(1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_body_that_is_not_wav_is_400()
    {
        var response = await Transcribe("Transcribe", Encoding.UTF8.GetBytes("not audio"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Speak(string feature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/ai/speech/{feature}")
        {
            Content = new StringContent("""{"text":"Hej Nova"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("audio/pcm");
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    [Fact]
    public async Task Streamed_speech_arrives_piece_by_piece_with_its_rate()
    {
        var response = await Speak("Stream");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/pcm", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("24000", response.Headers.GetValues("Spine-Sample-Rate").Single());

        // The first piece is there before the provider has made the rest.
        await using var body = await response.Content.ReadAsStreamAsync();
        var first = new byte[4800];
        await body.ReadExactlyAsync(first);

        _streamer.Release.SetResult();
        using var rest = new MemoryStream();
        await body.CopyToAsync(rest);
        Assert.Equal(2 * 4800, rest.Length);
    }

    [Fact]
    public async Task A_provider_that_does_not_stream_sends_its_whole_audio_as_one_piece()
    {
        var response = await Speak("Whole");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("24000", response.Headers.GetValues("Spine-Sample-Rate").Single());
        Assert.Equal(48_000, (await response.Content.ReadAsByteArrayAsync()).Length);
    }
}
