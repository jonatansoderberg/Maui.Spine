using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>A provider's socket that the test speaks for: events go in with <see cref="Push"/>, and what the app sent is kept.</summary>
internal sealed class ScriptedSocket : IVoiceSocket
{
    private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();

    public Uri? Url { get; private set; }
    public string? Bearer { get; private set; }
    public ConcurrentQueue<JsonElement> Sent { get; } = new();
    public bool Closed { get; private set; }
    public string? CloseReason => "1000: done";

    public IEnumerable<JsonElement> OfType(string type) => Sent.Where(m => m.GetProperty("type").GetString() == type);

    public void Push(object message) => _incoming.Writer.TryWrite(JsonSerializer.Serialize(message));

    public void Hangup() => _incoming.Writer.TryWrite(null);

    public Task ConnectAsync(Uri url, string bearer, CancellationToken cancellationToken)
    {
        Url = url;
        Bearer = bearer;
        return Task.CompletedTask;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(JsonDocument.Parse(message).RootElement.Clone());
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) => await _incoming.Reader.ReadAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Closed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>The backend's token endpoint.</summary>
internal sealed class TokenHandler : HttpMessageHandler
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string Body { get; set; } = """
        { "feature": "Voice", "protocol": "openai", "model": "gpt-realtime", "url": "wss://api.openai.com/v1/realtime?model=gpt-realtime",
          "token": "ek_test", "expiresAt": "2026-10-15T10:01:00+00:00", "maxMinutes": 3 }
        """;
    public TimeSpan? RetryAfter { get; set; }
    public HttpRequestMessage? Last { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Last = request;
        var response = new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        if (RetryAfter is { } wait)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
        return Task.FromResult(response);
    }
}

public sealed class SessionTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly ScriptedSocket _socket = new();
    private readonly TokenHandler _tokens = new();
    private readonly SpineVoiceOptions _options = new();
    private FakePlatform _audio = null!;
    private VoiceSession _session = null!;
    private readonly List<(VoiceSessionEnd Reason, Exception? Error)> _ended = [];

    public Task InitializeAsync()
    {
        _options.SessionEndpoint = new Uri("https://api.example.com/ai/");
        _options.AuthorizationHeader = _ => Task.FromResult<string?>("Bearer app-token");
        _options.HttpHandler = _tokens;
        _audio = new FakePlatform(_time);
        _session = new VoiceSession(_audio, _options, new VoiceTokenClient(_options, _ => Task.FromResult<string?>("phone-1")),
            () => _socket, action => action(), _time);
        _session.Ended += (reason, error) => _ended.Add((reason, error));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    private async Task ConnectAsync()
    {
        await _session.ConnectAsync("Voice");
        _time.Advance(TimeSpan.FromSeconds(1));    // past the echo canceller's convergence
    }

    /// <summary>Waits for the receive loop to handle what was pushed.</summary>
    private static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), what);
    }

    private static string Audio(int samples, short value = 1000)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task Connecting_asks_the_backend_for_a_token_and_opens_the_providers_socket_with_it()
    {
        await ConnectAsync();

        Assert.Equal("https://api.example.com/ai/realtime/Voice", _tokens.Last?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, _tokens.Last?.Method);
        Assert.Equal("Bearer app-token", _tokens.Last?.Headers.Authorization?.ToString());
        Assert.Equal("phone-1", _tokens.Last?.Headers.GetValues("Spine-Installation").Single());
        Assert.Equal("wss://api.openai.com/v1/realtime?model=gpt-realtime", _socket.Url?.ToString());
        Assert.Equal("ek_test", _socket.Bearer);
        Assert.True(_audio.IsRunning);
        Assert.True(_session.IsConnected);
        Assert.Equal(TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(1), _session.TimeLeft);
        Assert.Equal(VoiceState.Listening, _session.UpdateFrame().State);
    }

    [Fact]
    public async Task A_feature_that_needs_sign_in_is_off_and_nothing_is_left_open()
    {
        _tokens.Status = HttpStatusCode.Forbidden;
        _tokens.Body = """{ "off": "signInRequired" }""";

        var e = await Assert.ThrowsAsync<AIFeatureOffException>(() => _session.ConnectAsync("Voice"));

        Assert.Equal(AIOffReason.SignInRequired, e.Reason);
        Assert.False(_audio.IsRunning);
        Assert.Equal(VoiceState.Idle, _session.State);
    }

    [Fact]
    public async Task Too_many_sessions_say_when_to_try_again()
    {
        _tokens.Status = HttpStatusCode.TooManyRequests;
        _tokens.Body = "{}";
        _tokens.RetryAfter = TimeSpan.FromSeconds(20);

        var e = await Assert.ThrowsAsync<AIRateLimitedException>(() => _session.ConnectAsync("Voice"));

        Assert.Equal(TimeSpan.FromSeconds(20), e.RetryAfter);
    }

    [Fact]
    public async Task A_protocol_Spine_cannot_speak_is_refused_before_the_microphone_opens()
    {
        _tokens.Body = _tokens.Body.Replace("\"openai\"", "\"gemini\"");

        var e = await Assert.ThrowsAsync<NotSupportedException>(() => _session.ConnectAsync("Voice"));

        Assert.Contains("gemini", e.Message);
        Assert.False(_audio.IsRunning);
        Assert.Null(_socket.Url);
    }

    [Fact]
    public async Task The_microphone_goes_out_in_frames_of_about_100_ms()
    {
        await ConnectAsync();

        _audio.Capture(Signals.Sine(440, 48_000, 48_000, 0.3f), 48_000);    // one second at the device's rate

        await Until(() => _socket.OfType("input_audio_buffer.append").Count() >= 9, "nine or ten frames of audio");
        var frame = Convert.FromBase64String(_socket.OfType("input_audio_buffer.append").First().GetProperty("audio").GetString()!);
        Assert.InRange(frame.Length, 4800, 4800 + 2048);
    }

    [Fact]
    public async Task Client_tools_are_added_to_the_session_when_it_connects()
    {
        _options.ClientTools.Add(AIFunctionFactory.Create(() => "12:00", "show_time", "Says what time it is."));

        await ConnectAsync();

        await Until(() => _socket.OfType("session.update").Any(), "a session.update");
        var tool = _socket.OfType("session.update").Single().GetProperty("session").GetProperty("tools")[0];
        Assert.Equal("show_time", tool.GetProperty("name").GetString());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_answer_plays_per_item_and_the_states_follow_it()
    {
        await ConnectAsync();
        var states = new List<VoiceState>();
        _session.StateChanged += states.Add;

        _socket.Push(new { type = "response.created", response = new { id = "r1" } });
        await Until(() => _session.State == VoiceState.Thinking, "thinking");
        _socket.Push(new { type = "response.output_audio.delta", item_id = "a1", delta = Audio(2400) });
        _socket.Push(new { type = "response.output_audio.done", item_id = "a1" });
        _socket.Push(new { type = "response.done", response = new { id = "r1", status = "completed" } });
        await Until(() => _audio.IsPlaying, "the answer queued");
        Assert.Equal(VoiceState.Speaking, _session.UpdateFrame().State);

        _audio.Render(4800, 48_000);                  // 100 ms at the device's rate: all of it
        Assert.Equal(VoiceState.Listening, _session.UpdateFrame().State);
        Assert.Equal([VoiceState.Thinking, VoiceState.Speaking, VoiceState.Listening], states);
    }

    [Fact]
    public async Task Talking_over_the_answer_cuts_it_off_and_tells_the_model_how_much_was_heard()
    {
        await ConnectAsync();
        _socket.Push(new { type = "response.created", response = new { id = "r1" } });
        _socket.Push(new { type = "response.output_audio_transcript.delta", item_id = "a1", delta = "Det var en gång" });
        _socket.Push(new { type = "response.output_audio.delta", item_id = "a1", delta = Audio(24_000) });
        await Until(() => _audio.IsPlaying, "the answer queued");
        _audio.Render(9600, 48_000);                  // 200 ms played

        _socket.Push(new { type = "input_audio_buffer.speech_started", audio_start_ms = 1200, item_id = "u2" });

        await Until(() => _socket.OfType("conversation.item.truncate").Any(), "a truncate");
        var truncate = _socket.OfType("conversation.item.truncate").Single();
        Assert.Equal("a1", truncate.GetProperty("item_id").GetString());
        Assert.InRange(truncate.GetProperty("audio_end_ms").GetInt32(), 199, 201);
        Assert.Single(_socket.OfType("response.cancel"));
        Assert.False(_audio.IsPlaying);
        Assert.Equal(VoiceState.Interrupted, _session.UpdateFrame().State);
        Assert.Equal(true, _session.Transcript.Single().AdditionalProperties?[VoiceSession.InterruptedKey]);

        _time.Advance(TimeSpan.FromMilliseconds(700));
        Assert.Equal(VoiceState.Listening, _session.UpdateFrame().State);
    }

    [Fact]
    public async Task Speech_while_nothing_plays_is_not_an_interruption()
    {
        await ConnectAsync();

        _socket.Push(new { type = "input_audio_buffer.speech_started", audio_start_ms = 100, item_id = "u1" });
        _socket.Push(new { type = "input_audio_buffer.speech_stopped", audio_end_ms = 900, item_id = "u1" });
        await Task.Delay(50);

        Assert.Empty(_socket.OfType("conversation.item.truncate"));
        Assert.Empty(_socket.OfType("response.cancel"));
    }

    [Fact]
    public async Task The_transcript_has_both_sides_in_the_order_they_were_said()
    {
        await ConnectAsync();

        _socket.Push(new { type = "input_audio_buffer.committed", item_id = "u1", previous_item_id = (string?)null });
        _socket.Push(new { type = "response.created", response = new { id = "r1" } });
        _socket.Push(new { type = "response.output_audio_transcript.delta", item_id = "a1", delta = "Hej" });
        _socket.Push(new { type = "response.output_audio_transcript.delta", item_id = "a1", delta = " Jonatan!" });
        _socket.Push(new { type = "conversation.item.input_audio_transcription.completed", item_id = "u1", transcript = "Hej där. " });
        _socket.Push(new { type = "response.output_audio_transcript.done", item_id = "a1", transcript = "Hej Jonatan!" });

        await Until(() => _session.Transcript.Count == 2 && _session.Transcript[0].Text == "Hej där.", "both turns");
        Assert.Equal(ChatRole.User, _session.Transcript[0].Role);
        Assert.Equal(ChatRole.Assistant, _session.Transcript[1].Role);
        Assert.Equal("Hej Jonatan!", _session.Transcript[1].Text);
    }

    [Fact]
    public async Task A_turn_that_could_not_be_transcribed_leaves_no_empty_line()
    {
        await ConnectAsync();

        _socket.Push(new { type = "input_audio_buffer.committed", item_id = "u1" });
        _socket.Push(new { type = "conversation.item.input_audio_transcription.failed", item_id = "u1", error = new { message = "x" } });

        await Until(() => _session.Transcript.Count == 0 && _socket.Sent.Count >= 0, "no line");
        await Task.Delay(50);
        Assert.Empty(_session.Transcript);
    }

    [Fact]
    public async Task A_client_tool_runs_and_its_answer_goes_back_to_the_model()
    {
        _options.ClientTools.Add(AIFunctionFactory.Create((string city) => $"12:00 i {city}", "show_time", "Says what time it is."));
        await ConnectAsync();

        _socket.Push(new { type = "response.function_call_arguments.done", call_id = "c1", name = "show_time", arguments = """{"city":"Visby"}""" });

        await Until(() => _socket.OfType("response.create").Any(), "a new response");
        var output = _socket.OfType("conversation.item.create").Single().GetProperty("item");
        Assert.Equal("function_call_output", output.GetProperty("type").GetString());
        Assert.Equal("c1", output.GetProperty("call_id").GetString());
        Assert.Contains("12:00 i Visby", output.GetProperty("output").GetString());
        Assert.Contains(_session.Transcript, m => m.Contents.OfType<FunctionCallContent>().Any(c => c.Name == "show_time"));
        Assert.Contains(_session.Transcript, m => m.Contents.OfType<FunctionResultContent>().Any(r => r.CallId == "c1"));
    }

    [Fact]
    public async Task An_unknown_tool_answers_with_an_error_so_the_model_can_say_so()
    {
        await ConnectAsync();

        _socket.Push(new { type = "response.function_call_arguments.done", call_id = "c1", name = "launch_rocket", arguments = "{}" });

        await Until(() => _socket.OfType("conversation.item.create").Any(), "an output");
        Assert.Contains("launch_rocket", _socket.OfType("conversation.item.create").Single().GetProperty("item").GetProperty("output").GetString());
    }

    [Fact]
    public async Task Push_to_talk_clears_then_commits_and_asks_for_the_answer()
    {
        await ConnectAsync();

        _session.BeginTurn();
        _audio.Capture(new float[2400], 24_000);    // 100 ms
        _session.EndTurn();

        await Until(() => _socket.OfType("response.create").Any(), "a response");
        var types = _socket.Sent.Select(m => m.GetProperty("type").GetString()).ToList();
        Assert.True(types.IndexOf("input_audio_buffer.clear") < types.IndexOf("input_audio_buffer.commit"));
        Assert.True(types.IndexOf("input_audio_buffer.commit") < types.IndexOf("response.create"));
    }

    [Fact]
    public async Task Text_goes_in_the_transcript_and_to_the_model()
    {
        await ConnectAsync();

        await _session.SendTextAsync("Vad är klockan?");

        await Until(() => _socket.OfType("response.create").Any(), "a response");
        var item = _socket.OfType("conversation.item.create").Single().GetProperty("item");
        Assert.Equal("Vad är klockan?", item.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Vad är klockan?", _session.Transcript.Single().Text);
    }

    [Fact]
    public async Task The_session_ends_itself_at_the_servers_limit()
    {
        await ConnectAsync();

        _time.Advance(TimeSpan.FromMinutes(3));

        await Until(() => _ended.Count == 1, "ended");
        Assert.Equal(VoiceSessionEnd.TimeLimit, _ended[0].Reason);
        Assert.True(_socket.Closed);
        Assert.False(_audio.IsRunning);
        Assert.Equal(VoiceState.Idle, _session.State);
    }

    [Fact]
    public async Task The_provider_hanging_up_ends_the_session()
    {
        await ConnectAsync();

        _socket.Hangup();

        await Until(() => _ended.Count == 1, "ended");
        Assert.Equal(VoiceSessionEnd.Provider, _ended[0].Reason);
        Assert.False(_session.IsConnected);
    }

    [Fact]
    public async Task Ending_closes_everything_once()
    {
        await ConnectAsync();

        await _session.EndAsync();
        await _session.EndAsync();

        Assert.Equal([(VoiceSessionEnd.User, (Exception?)null)], _ended);
        Assert.True(_socket.Closed);
        Assert.False(_audio.IsRunning);
    }
}
