using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Voice.Tests;

public sealed class ServerSpeechTests
{
    private sealed class Server(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        /// <summary>Holds the answer until the test has subscribed.</summary>
        public TaskCompletionSource Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            await Answer.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    // A tenth of a second at 24 kHz, with a p, b, m at 40 ms.
    private static readonly string Answer = new JsonObject
    {
        ["feature"] = "Read",
        ["sampleRate"] = 24_000,
        ["audio"] = Convert.ToBase64String(new byte[4800]),
        ["visemes"] = new JsonArray(new JsonObject { ["id"] = 0, ["offsetMs"] = 0 }, new JsonObject { ["id"] = 21, ["offsetMs"] = 40 }),
        ["durationMs"] = 100,
    }.ToJsonString();

    private readonly FakePlatform _engine = new(new FakeTimeProvider());

    private ServerSpeech Create(Server server, string? feature = "Read") => new(
        _engine,
        new SpineVoiceOptions { SessionEndpoint = new Uri("https://api.test/ai/"), HttpHandler = server, SpeechFeature = feature },
        _ => Task.FromResult<string?>("phone-1"),
        action => action());

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task The_server_audio_plays_without_the_microphone_and_its_visemes_are_raised()
    {
        var server = new Server(HttpStatusCode.OK, Answer);
        await using var speech = Create(server);
        var visemes = new List<SpeechViseme>();

        var utterance = speech.Speak("Hej Nova");
        utterance.Viseme += (_, v) => { lock (visemes) visemes.Add(v); };
        server.Answer.SetResult();
        await Until(() => utterance.IsSpeaking && visemes.Count == 2);

        Assert.Equal("https://api.test/ai/speech/Read", server.Request!.RequestUri!.ToString());
        Assert.Equal("phone-1", server.Request.Headers.GetValues("Spine-Installation").Single());
        Assert.Equal("Hej Nova", (string?)JsonNode.Parse(server.Body!)!["text"]);
        Assert.False(_engine.StartedWith!.Input);
        Assert.Equal(24_000, _engine.StartedWith.OutputRate);
        Assert.Equal([new SpeechViseme(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(40)), new SpeechViseme(21, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(60))], visemes);

        // The utterance's position is what the engine has played.
        _engine.Render(1200, 24_000);
        Assert.Equal(50, utterance.Position.TotalMilliseconds, 0.5);

        _engine.Render(1200, 24_000);
        Assert.Equal(SpeechEndReason.Completed, await utterance.Completed.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Without_visemes_the_words_arrive_as_the_audio_reaches_them()
    {
        var answer = JsonNode.Parse(Answer)!;
        answer["visemes"] = new JsonArray();
        var server = new Server(HttpStatusCode.OK, answer.ToJsonString());
        await using var speech = Create(server);
        var words = new List<SpeechWord>();

        var utterance = speech.Speak("Hej Nova");
        utterance.Word += (_, w) => { lock (words) words.Add(w); };
        server.Answer.SetResult();
        await Until(() => utterance.IsSpeaking && words.Count == 1);

        // "Hej" is three of seven letters, so "Nova" starts three sevenths in: 43 ms of 100.
        _engine.Render(960, 24_000);
        await Task.Delay(150);
        Assert.Single(words);
        _engine.Render(240, 24_000);
        await Until(() => words.Count == 2);
        Assert.Equal((4, 4), (words[1].Start, words[1].Length));

        _engine.Render(2400, 24_000);
        Assert.Equal(SpeechEndReason.Completed, await utterance.Completed.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Stop_cuts_the_audio()
    {
        var server = new Server(HttpStatusCode.OK, Answer);
        await using var speech = Create(server);
        var utterance = speech.Speak("Hej");
        server.Answer.SetResult();
        await Until(() => utterance.IsSpeaking);

        utterance.Stop();

        Assert.Equal(SpeechEndReason.Stopped, await utterance.Completed.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(_engine.IsPlaying);
    }

    [Fact]
    public async Task A_refusal_fails_the_utterance_and_says_why()
    {
        var server = new Server(HttpStatusCode.NotFound, """{"error":"The feature is off.","off":"budget"}""");
        server.Answer.SetResult();
        await using var speech = Create(server);
        Exception? failure = null;
        speech.Failed += e => failure = e;

        var utterance = speech.Speak("Hej");

        Assert.Equal(SpeechEndReason.Failed, await utterance.Completed.WaitAsync(TimeSpan.FromSeconds(2)));
        var off = Assert.IsType<AIFeatureOffException>(failure);
        Assert.Equal("Read", off.Feature);
    }

    [Fact]
    public async Task Without_a_feature_it_fails_with_what_to_set()
    {
        var server = new Server(HttpStatusCode.OK, Answer);
        server.Answer.SetResult();
        await using var speech = Create(server, feature: null);
        Exception? failure = null;
        speech.Failed += e => failure = e;

        Assert.Equal(SpeechEndReason.Failed, await speech.Speak("Hej").Completed.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("SpeechFeature", failure!.Message);
    }
}
