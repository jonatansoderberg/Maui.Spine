using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>The server's transcription and streamed speech: answers per feature, and remembers what it was asked.</summary>
internal sealed class PipelineServerHandler : HttpMessageHandler
{
    public ConcurrentQueue<(string Path, byte[] Body)> Requests { get; } = new();
    public Dictionary<string, (HttpStatusCode Status, string Body)> Transcriptions { get; } = new() { ["Transcribe"] = (HttpStatusCode.OK, """{ "text": "Vad är klockan?" }""") };
    public HttpStatusCode SpeechStatus { get; set; } = HttpStatusCode.OK;

    public IEnumerable<string> Spoken => Requests.Where(r => r.Path.Contains("/speech/")).Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("text").GetString()!);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue((path, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
        if (path.Contains("/transcribe/", StringComparison.Ordinal))
        {
            var (status, body) = Transcriptions.GetValueOrDefault(path.Split('/')[^1], (HttpStatusCode.NotFound, """{ "off": "notExposed" }"""));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        if (SpeechStatus != HttpStatusCode.OK)
            return new HttpResponseMessage(SpeechStatus) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        // 100 ms of audio per sentence, at 24 kHz.
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Signals.Bytes(Signals.Pcm(Signals.Sine(300, 24_000, 2400, 0.3f)))) };
        response.Content.Headers.ContentType = new("audio/pcm");
        response.Headers.Add("Spine-Sample-Rate", "24000");
        return response;
    }
}

/// <summary>A chat model that answers in pieces and remembers what it was given.</summary>
internal sealed class ScriptedChat(params string[] pieces) : IChatClient
{
    public List<List<ChatMessage>> Asked { get; } = [];
    public Exception? Throws { get; set; }
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Hangs up before answering, as a model does when the user says goodbye: the function invoking client runs the tool.</summary>
    public bool HangsUp { get; set; }

    public List<string> Offered { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Asked.Add([.. messages]);
        Offered.AddRange(options?.Tools?.Select(t => t.Name) ?? []);
        if (Throws is { } e)
            throw e;
        if (HangsUp && options?.Tools?.OfType<AIFunction>().FirstOrDefault(t => t.Name == VoiceSession.EndConversationTool) is { } hangUp)
        {
            var told = await hangUp.InvokeAsync(cancellationToken: cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", hangUp.Name)]);
            yield return new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", told)]);
        }
        for (var i = 0; i < pieces.Length; i++)
        {
            if (i == 1 && Hold is { } hold)
                await hold.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, pieces[i]);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>A chat model that calls <c>show_time</c> first and then reads its result out.</summary>
internal sealed class ClockChat : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().FirstOrDefault() is { } result)
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"Klockan är {result.Result}.");
        else if (options?.Tools?.Any(t => t.Name == "show_time") == true)
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call_1", "show_time")]);
        else
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Jag vet inte.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

public sealed class TurnDetectorTests
{
    private static byte[] Tone(int milliseconds, float amplitude) =>
        Signals.Bytes(Signals.Pcm(Signals.Sine(220, 24_000, 24 * milliseconds, amplitude)));

    [Fact]
    public void Speech_starts_a_turn_after_its_start_time_and_quiet_ends_it()
    {
        var detector = new TurnDetector(24_000, 0.02f, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(30));

        Assert.Equal(TurnEdge.None, detector.Feed(Tone(100, 0.3f), answerPlaying: false));
        Assert.Equal(TurnEdge.Started, detector.Feed(Tone(200, 0.3f), answerPlaying: false));
        Assert.Equal(TurnEdge.None, detector.Feed(Tone(500, 0f), answerPlaying: false));
        Assert.Equal(TurnEdge.Ended, detector.Feed(Tone(400, 0f), answerPlaying: false));
        Assert.False(detector.InTurn);
    }

    [Fact]
    public void A_short_sound_is_not_a_turn()
    {
        var detector = new TurnDetector(24_000, 0.02f, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(30));

        detector.Feed(Tone(100, 0.3f), answerPlaying: false);
        Assert.Equal(TurnEdge.None, detector.Feed(Tone(100, 0f), answerPlaying: false));
        Assert.Equal(TurnEdge.None, detector.Feed(Tone(100, 0.3f), answerPlaying: false));
    }

    [Fact]
    public void An_answers_echo_needs_twice_the_level_to_count_as_the_user()
    {
        var detector = new TurnDetector(24_000, 0.02f, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(30));
        var echo = Tone(400, 0.04f);    // RMS about 0.028

        Assert.Equal(TurnEdge.None, detector.Feed(echo, answerPlaying: true));
        Assert.Equal(TurnEdge.Started, detector.Feed(echo, answerPlaying: false));
    }

    [Fact]
    public void A_noisy_room_raises_the_threshold()
    {
        var detector = new TurnDetector(24_000, 0.02f, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(30));
        detector.Feed(Tone(5000, 0.018f), answerPlaying: false);    // a hum just under the floor, for five seconds

        Assert.True(detector.Threshold > 0.04f, $"the threshold is {detector.Threshold}");
        Assert.Equal(TurnEdge.None, detector.Feed(Tone(400, 0.04f), answerPlaying: false));
    }
}

public sealed class SentenceSplitterTests
{
    [Fact]
    public void Sentences_come_out_as_they_end_and_the_rest_at_the_end()
    {
        var splitter = new SentenceSplitter();

        Assert.Empty(splitter.Append("Klockan är "));
        Assert.Equal(["Klockan är 3.15."], splitter.Append("3.15. Vill du "));
        Assert.Equal(["Vill du veta mer?"], splitter.Append("veta mer? Säg"));
        Assert.Equal("Säg till", splitter.Append(" till").Count == 0 ? splitter.Flush() : null);
    }

    [Fact]
    public void Markdown_is_not_read_out()
    {
        var splitter = new SentenceSplitter();

        var sentences = splitter.Append("## Bokningar\n- **Tisdag** kl. 18\n---\n");

        Assert.Equal(["Bokningar", "Tisdag kl. 18"], sentences);
    }
}

public sealed class PipelineTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
    private readonly PipelineServerHandler _server = new();
    private readonly SpineVoiceOptions _options = new();
    private readonly List<ChatMessage> _conversation = [];
    private readonly List<(VoiceSessionEnd Reason, Exception? Error)> _ended = [];
    private ScriptedChat _chat = new("Klockan är tre. Ha", " en fin dag!");
    private FakePlatform _audio = null!;
    private RoutingVoiceSession _session = null!;

    public Task InitializeAsync()
    {
        _options.SessionEndpoint = new Uri("https://api.example.com/ai/");
        _options.HttpHandler = _server;
        _audio = new FakePlatform(_time);
        Declare(p => p.AddServerSpeechToText("Transcribe").AddChat(_chat).AddServerTextToSpeech("Read"));
        _session = new RoutingVoiceSession(
            new VoiceSession(_audio, _options, new VoiceTokenClient(_options, _ => Task.FromResult<string?>("phone-1")), () => new ScriptedSocket(), action => action(), _time),
            new PipelineVoiceSession(_audio, _options, new PipelineServer(_options, _ => Task.FromResult<string?>("phone-1")), action => { lock (_ui) action(); }, _time),
            _options);
        _session.Ended += (reason, error) => _ended.Add((reason, error));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    private void Declare(Action<VoicePipeline> parts) => _options.Pipeline("Claude", p =>
    {
        // Short turns keep the tests quick; the defaults are 200 and 800 ms.
        p.StartOfTurn = TimeSpan.FromMilliseconds(100);
        p.EndOfTurn = TimeSpan.FromMilliseconds(300);
        parts(p);
    });

    private async Task ConnectAsync()
    {
        await _session.ConnectAsync("Claude", _conversation);
        _time.Advance(TimeSpan.FromSeconds(1));    // past the echo canceller's convergence
    }

    private void Say(int milliseconds, float amplitude = 0.3f)
    {
        // In 20 ms buffers, as the device delivers them.
        for (var i = 0; i < milliseconds / 20; i++)
            _audio.Capture(Signals.Sine(220, 24_000, 480, amplitude), 24_000);
    }

    // The main thread of a test: the session changes the conversation from its own tasks, and the test reads it, under one lock.
    private readonly Lock _ui = new();

    private async Task Until(Func<bool> condition, string what)
    {
        bool Met()
        {
            lock (_ui)
                return condition();
        }

        for (var i = 0; i < 300 && !Met(); i++)
            await Task.Delay(10);
        Assert.True(Met(), what);
    }

    private ChatMessage? Answer => _conversation.LastOrDefault(m => m.Role == ChatRole.Assistant && m.Text.Length > 0);

    [Fact]
    public async Task A_spoken_turn_is_written_down_answered_and_read_aloud_sentence_by_sentence()
    {
        _conversation.Add(new ChatMessage(ChatRole.User, "Hej"));
        _conversation.Add(new ChatMessage(ChatRole.Assistant, "Hej! Vad kan jag hjälpa till med?"));
        await ConnectAsync();

        Say(400);
        Say(400, 0f);

        await Until(() => _server.Spoken.Count() == 2, "both sentences read");
        var wav = _server.Requests.First(r => r.Path == "/ai/transcribe/Transcribe").Body;
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.True(wav.Length > 44 + 24_000 * 2 * 4 / 10, "the turn's audio, with the pre-roll");

        Assert.Equal(["Hej", "Hej! Vad kan jag hjälpa till med?", "Vad är klockan?"], _chat.Asked.Single().Select(m => m.Text));
        Assert.Equal(["Klockan är tre.", "Ha en fin dag!"], _server.Spoken);
        await Until(() => Answer?.Text == "Klockan är tre. Ha en fin dag!", "the answer in the conversation");
        Assert.Equal("Vad är klockan?", _conversation[2].Text);
        Assert.True(_audio.IsPlaying);
        Assert.Equal(VoiceState.Speaking, _session.UpdateFrame().State);
    }

    [Fact]
    public async Task The_users_turn_is_pending_while_they_talk_and_until_its_words_arrive()
    {
        await ConnectAsync();

        Say(400);

        var pending = Assert.Single(_conversation);
        Assert.True(pending.AdditionalProperties?.ContainsKey(VoiceSession.PendingKey));
        Say(400, 0f);
        await Until(() => _conversation[0].Text == "Vad är klockan?", "the words");
        Assert.Null(_conversation[0].AdditionalProperties);
    }

    [Fact]
    public async Task Talking_over_the_answer_cuts_it_off()
    {
        _chat.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await ConnectAsync();
        Say(400);
        Say(400, 0f);
        await Until(() => _audio.IsPlaying, "the first sentence plays");

        Say(400, 0.6f);

        Assert.False(_audio.IsPlaying);
        Assert.Equal(VoiceState.Interrupted, _session.UpdateFrame().State);
        await Until(() => Answer?.AdditionalProperties?.ContainsKey(VoiceSession.InterruptedKey) == true, "the answer marked as cut off");
        _chat.Hold.SetResult();
        Assert.Equal(["Klockan är tre."], _server.Spoken);
    }

    [Fact]
    public async Task Typed_text_is_answered_aloud()
    {
        await ConnectAsync();

        await _session.SendTextAsync("Vad är klockan?");

        await Until(() => _server.Spoken.Count() == 2, "read aloud");
        Assert.Equal("Vad är klockan?", _chat.Asked.Single().Last().Text);
    }

    [Fact]
    public async Task When_a_speech_to_text_part_fails_the_next_writes_the_turn_down()
    {
        _server.Transcriptions["Broken"] = (HttpStatusCode.InternalServerError, "{}");
        Declare(p => p.AddServerSpeechToText("Broken").AddServerSpeechToText("Transcribe").AddChat(_chat).AddServerTextToSpeech("Read"));
        await ConnectAsync();

        Say(400);
        Say(400, 0f);

        await Until(() => _chat.Asked.Count == 1, "answered");
        Assert.Equal(["/ai/transcribe/Broken", "/ai/transcribe/Transcribe"], _server.Requests.Select(r => r.Path).Where(p => p.Contains("transcribe")));
        Assert.Equal("Vad är klockan?", _chat.Asked[0].Last().Text);
    }

    [Fact]
    public async Task Silence_that_is_written_down_as_nothing_leaves_no_line_and_no_answer()
    {
        _server.Transcriptions["Transcribe"] = (HttpStatusCode.OK, """{ "text": "" }""");
        await ConnectAsync();

        Say(400);
        Say(400, 0f);

        await Until(() => _conversation.Count == 0, "the pending turn removed");
        Assert.Empty(_chat.Asked);
    }

    [Fact]
    public async Task When_no_chat_can_answer_the_call_ends_and_says_why()
    {
        _chat.Throws = new HttpRequestException("503 from the server");
        await ConnectAsync();

        await _session.SendTextAsync("Hallå?");

        await Until(() => _ended.Count == 1, "ended");
        Assert.Equal(VoiceSessionEnd.Failed, _ended[0].Reason);
        Assert.Equal("503 from the server", _ended[0].Error?.Message);
    }

    [Fact]
    public async Task The_apps_own_chat_client_runs_the_apps_functions()
    {
        SpineAIFunctionContext? seen = null;
        _options.Functions.Add("show_time", "Says the time.", (SpineAIFunctionContext context) => { seen = context; return "14:05"; });
        Declare(p => p.AddServerSpeechToText("Transcribe").AddChat(new ClockChat()).AddServerTextToSpeech("Read"));
        await ConnectAsync();

        await _session.SendTextAsync("Vad är klockan?");

        await Until(() => _server.Spoken.Any(), "read aloud");
        Assert.Equal(["Klockan är 14:05."], _server.Spoken);
        Assert.Equal(SpineAIFunctionSide.Device, seen?.RunsOn);
        Assert.Equal("call_1", seen?.CallId);
        Assert.Matches("^[0-9a-f]{32}$", seen?.ConversationId);
    }

    [Fact]
    public async Task A_goodbye_hangs_up_once_the_farewell_has_played()
    {
        _chat = new ScriptedChat("Hej då!") { HangsUp = true };
        Declare(p => p.AddServerSpeechToText("Transcribe").AddChat(_chat).AddServerTextToSpeech("Read"));
        _session.EndsOnGoodbye = true;
        await ConnectAsync();

        await _session.SendTextAsync("Tack, hej då");
        await Until(() => _server.Spoken.Any() && _audio.IsPlaying, "the farewell plays");
        Assert.Empty(_ended);

        // The user says goodbye back: it is not a turn, and it neither cuts the farewell nor keeps the call.
        Say(400);
        Say(400, 0f);
        Assert.True(_audio.IsPlaying);
        Assert.DoesNotContain(_server.Requests, r => r.Path.Contains("/transcribe/"));

        _audio.Render(4800, 48_000);                  // the farewell's 100 ms, played
        for (var i = 0; i < 100 && _ended.Count == 0; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(10);
        }
        Assert.Equal([(VoiceSessionEnd.Goodbye, (Exception?)null)], _ended);
        Assert.Equal(["Hej då!"], _server.Spoken);
    }

    [Fact]
    public async Task Turning_goodbyes_off_cancels_a_farewell_under_way()
    {
        _chat = new ScriptedChat("Hej då!") { HangsUp = true };
        Declare(p => p.AddServerSpeechToText("Transcribe").AddChat(_chat).AddServerTextToSpeech("Read"));
        _session.EndsOnGoodbye = true;
        await ConnectAsync();

        await _session.SendTextAsync("Tack, hej då");
        await Until(() => _server.Spoken.Any() && _audio.IsPlaying, "the farewell plays");
        _session.EndsOnGoodbye = false;

        _audio.Render(4800, 48_000);
        for (var i = 0; i < 30; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(10);
        }
        Assert.Empty(_ended);

        // The microphone is listened to again: the call goes on.
        Say(400);
        Say(900, 0f);
        await Until(() => _server.Requests.Any(r => r.Path.Contains("/transcribe/")), "the next turn is written down");
    }

    [Fact]
    public async Task Without_goodbyes_the_chat_is_offered_no_hang_up()
    {
        await ConnectAsync();

        await _session.SendTextAsync("Vad är klockan?");

        await Until(() => _chat.Asked.Count == 1, "answered");
        Assert.Empty(_chat.Offered);
    }

    [Fact]
    public async Task A_pipeline_without_a_part_of_each_kind_is_refused_when_declared()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => _options.Pipeline("Half", p => p.AddChat(_chat)));

        Assert.Contains("speech-to-text", refused.Message);
    }

    [Fact]
    public async Task The_pipeline_session_has_the_engine_an_avatar_reads()
    {
        await ConnectAsync();

        Assert.True(_session.IsConnected);
        Assert.Same(_audio, ((IVoiceAudioOwner)_session).Audio);
        Assert.Equal("the server's 'Read'", _session.SpokenVoice);
        await _session.EndAsync();
        Assert.Equal([(VoiceSessionEnd.User, (Exception?)null)], _ended);
    }
}
