using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>A session against a GPT-Live feature, which the server relays: the socket speaks Live's events.</summary>
public sealed class LiveSessionTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));
    private readonly ScriptedSocket _socket = new();
    private readonly TokenHandler _tokens = new()
    {
        Body = """
            { "feature": "Voice", "protocol": "openai-live", "model": "gpt-live-1", "url": "realtime/Voice/relay?ticket=t1",
              "token": "t1", "expiresAt": "2026-10-20T10:01:00+00:00", "maxMinutes": 2, "voice": "marin",
              "tools": [ { "name": "get_my_bookings", "description": "Bookings.", "parameters": { "type": "object" }, "runsOn": [ "server" ], "prefer": "server" } ] }
            """,
    };
    private readonly SpineVoiceOptions _options = new();
    private FakePlatform _audio = null!;
    private VoiceSession _session = null!;
    private readonly List<(VoiceSessionEnd Reason, Exception? Error)> _ended = [];

    // The main thread of a test: the session changes the transcript on its receive loop, and the test reads it, under one lock.
    private readonly Lock _ui = new();

    public Task InitializeAsync()
    {
        _options.SessionEndpoint = new Uri("https://api.example.com/ai");
        _options.AuthorizationHeader = _ => Task.FromResult<string?>("Bearer app-token");
        _options.HttpHandler = _tokens;
        _audio = new FakePlatform(_time);
        _session = new VoiceSession(_audio, _options, new VoiceTokenClient(_options, _ => Task.FromResult<string?>("phone-1")),
            () => _socket, action => { lock (_ui) action(); }, _time);
        _session.Ended += (reason, error) => _ended.Add((reason, error));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    private async Task ConnectAsync(IList<ChatMessage>? conversation = null)
    {
        if (conversation is null) await _session.ConnectAsync("Voice");
        else await _session.ConnectAsync("Voice", conversation);
        _time.Advance(TimeSpan.FromSeconds(1));
    }

    private async Task Until(Func<bool> condition, string what)
    {
        bool Met()
        {
            lock (_ui)
                return condition();
        }

        for (var i = 0; i < 200 && !Met(); i++)
            await Task.Delay(10);
        Assert.True(Met(), what);
    }

    private static string Audio(int samples, short value = 1000)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task It_connects_to_the_relay_with_the_apps_own_sign_in()
    {
        await ConnectAsync();

        Assert.Equal("wss://api.example.com/ai/realtime/Voice/relay?ticket=t1", _socket.Url?.ToString());
        Assert.Equal("Bearer app-token", _socket.Headers["Authorization"]);
        Assert.Equal("phone-1", _socket.Headers["Spine-Installation"]);
        Assert.Equal("marin", _session.SpokenVoice);
    }

    [Fact]
    public async Task The_first_message_gives_the_relay_the_conversation_and_the_apps_own_functions_only()
    {
        _options.Functions.Add(AIFunctionFactory.Create(() => "12:00", "show_time", "Says what time it is."));
        _session.EndsOnGoodbye = true;
        List<ChatMessage> chat = [new(ChatRole.User, "Hej"), new(ChatRole.Assistant, "Hej hej")];

        await ConnectAsync(chat);

        await Until(() => _socket.Sent.Any(), "the start");
        var start = _socket.Sent.First();
        Assert.Equal("session.start", start.GetProperty("type").GetString());
        var input = start.GetProperty("session").GetProperty("input");
        Assert.Equal(["user", "assistant"], input.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Hej hej", input[1].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(["end_conversation", "show_time"],
            start.GetProperty("session").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Empty(_socket.OfType("session.update"));
    }

    [Fact]
    public async Task A_shorter_limit_is_asked_for_and_kept()
    {
        _session.MaxMinutes = 1.5;
        _tokens.Body = _tokens.Body.Replace("\"maxMinutes\": 2", "\"maxMinutes\": 1.5");

        await ConnectAsync();

        Assert.Equal("https://api.example.com/ai/realtime/Voice?maxMinutes=1.5", _tokens.Last?.RequestUri?.ToString());
        Assert.Equal(TimeSpan.FromSeconds(89), _session.TimeLeft);
    }

    [Fact]
    public async Task The_microphone_goes_out_as_lives_appends_and_silence_fills_its_gaps()
    {
        await ConnectAsync();

        await Until(() => _socket.OfType("session.input_audio.append").Count() >= 5, "silence while the microphone is quiet");
        Assert.All(_socket.OfType("session.input_audio.append"), a => Assert.Equal(4800, Convert.FromBase64String(a.GetProperty("audio").GetString()!).Length));
        Assert.Empty(_socket.OfType("input_audio_buffer.append"));
    }

    [Fact]
    public async Task The_answer_plays_and_ends_when_no_more_audio_comes()
    {
        await ConnectAsync();

        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
        await Until(() => _audio.IsPlaying, "the answer queued");
        Assert.Equal(VoiceState.Speaking, _session.UpdateFrame().State);

        _time.Advance(TimeSpan.FromMilliseconds(400));
        _audio.Render(4800, 48_000);
        Assert.False(_audio.IsPlaying);
        Assert.Equal(VoiceState.Listening, _session.UpdateFrame().State);
    }

    [Fact]
    public async Task The_silence_live_streams_between_answers_is_not_played()
    {
        await ConnectAsync();

        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400, value: 0) });
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400, value: 8) });
        await Task.Delay(50);
        Assert.False(_audio.IsPlaying);
        Assert.Equal(VoiceState.Listening, _session.UpdateFrame().State);

        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400, value: 200) });
        await Until(() => _audio.IsPlaying, "a fade-in plays");
    }

    [Fact]
    public async Task Each_side_s_words_join_its_latest_message_until_a_pause()
    {
        await ConnectAsync();

        _socket.Push(new { type = "session.input_transcript.delta", delta = "Jag vill", start_ms = 1000, end_ms = 1400 });
        _socket.Push(new { type = "session.output_transcript.delta", delta = "Mm.", start_ms = 1300, end_ms = 1500 });
        _socket.Push(new { type = "session.input_transcript.delta", delta = " boka", start_ms = 1500, end_ms = 1900 });
        _socket.Push(new { type = "session.input_transcript.delta", delta = "Tack", start_ms = 6000, end_ms = 6300 });
        await Until(() => _session.Transcript.Count(m => m.Role == ChatRole.User) == 2, "two user messages");
        _time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(["Jag vill boka", "Mm.", "Tack"], _session.Transcript.Select(m => m.Text));
    }

    [Fact]
    public async Task When_the_model_yields_to_the_user_the_rest_of_its_answer_is_dropped()
    {
        await ConnectAsync();
        _socket.Push(new { type = "session.output_transcript.delta", delta = "Det var en gång", start_ms = 0, end_ms = 900 });
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(48_000) });    // two seconds queued
        await Until(() => _audio.IsPlaying, "the answer queued");
        _audio.Render(9600, 48_000);

        _socket.Push(new { type = "session.input_transcript.delta", delta = "Vänta", start_ms = 1200, end_ms = 1500 });
        await Until(() => _session.Transcript.Any(m => m.Role == ChatRole.User), "the user's words");
        _time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.False(_audio.IsPlaying);
        Assert.Equal(true, _session.Transcript.First(m => m.Role == ChatRole.Assistant).AdditionalProperties?[VoiceSession.InterruptedKey]);
        Assert.Empty(_socket.OfType("conversation.item.truncate"));
        Assert.Empty(_socket.OfType("response.cancel"));
    }

    [Fact]
    public async Task A_backchannel_while_the_model_goes_on_talking_cuts_nothing()
    {
        await ConnectAsync();
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(48_000) });
        await Until(() => _audio.IsPlaying, "the answer queued");

        _socket.Push(new { type = "session.input_transcript.delta", delta = "Mm", start_ms = 500, end_ms = 700 });
        for (var i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
            await Task.Delay(20);
        }

        Assert.True(_audio.IsPlaying);
    }

    [Fact]
    public async Task Stop_drops_the_answer_and_what_still_comes_of_it()
    {
        await ConnectAsync();
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
        await Until(() => _audio.IsPlaying, "the answer queued");

        _session.Interrupt();
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
        await Task.Delay(50);
        Assert.False(_audio.IsPlaying);

        _time.Advance(TimeSpan.FromMilliseconds(400));
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
        await Until(() => _audio.IsPlaying, "the next answer plays");
    }

    private static object Envelope(object inner) => new { type = "response.event", delegation_id = "d1", @event = inner };

    [Fact]
    public async Task A_device_function_runs_and_its_result_goes_to_the_relay()
    {
        _options.Functions.Add(AIFunctionFactory.Create(() => "12:00", "show_time", "Says what time it is."));
        await ConnectAsync();

        _socket.Push(new { type = "session.delegation.created", delegation = new { id = "d1", target = "responses" } });
        await Until(() => _session.State == VoiceState.Thinking, "thinking");
        _socket.Push(Envelope(new { type = "response.output_item.done", item = new { type = "function_call", call_id = "call_1", name = "show_time", arguments = "{}" } }));

        await Until(() => _socket.OfType("response.item.create").Any(), "the result");
        var result = _socket.OfType("response.item.create").Single().GetProperty("item");
        Assert.Equal("call_1", result.GetProperty("call_id").GetString());
        Assert.Equal("12:00", result.GetProperty("output").GetString());
        Assert.Empty(_socket.OfType("response.create"));

        _socket.Push(Envelope(new { type = "response.completed", response = new { id = "r1" } }));
        await Until(() => _session.State == VoiceState.Listening, "listening");
    }

    [Fact]
    public async Task A_server_function_is_left_to_the_relay_and_shown_with_its_result()
    {
        await ConnectAsync();

        _socket.Push(Envelope(new { type = "response.output_item.done", item = new { type = "function_call", call_id = "call_1", name = "get_my_bookings", arguments = "{}" } }));
        _socket.Push(new { type = "spine.function.output", call_id = "call_1", name = "get_my_bookings", output = "två bokningar" });

        await Until(() => _session.Transcript.Count == 2, "the call and its result");
        Assert.IsType<FunctionCallContent>(_session.Transcript[0].Contents.Single());
        Assert.Equal("två bokningar", ((FunctionResultContent)_session.Transcript[1].Contents.Single()).Result);
        Assert.Empty(_socket.OfType("response.item.create"));
        Assert.Empty(_tokens.FunctionCalls);
    }

    [Fact]
    public async Task Typed_text_goes_to_the_relay_as_the_users_words()
    {
        await ConnectAsync();

        await _session.SendTextAsync("Vad är klockan?");

        await Until(() => _socket.OfType("spine.user_text").Any(), "the text");
        Assert.Equal("Vad är klockan?", _socket.OfType("spine.user_text").Single().GetProperty("text").GetString());
        Assert.Equal("Vad är klockan?", _session.Transcript.Single().Text);
    }

    [Fact]
    public async Task Muting_tells_the_model()
    {
        await ConnectAsync();

        _session.IsMuted = true;
        _session.IsMuted = false;

        await Until(() => _socket.OfType("session.input_audio.unmute").Any(), "unmuted");
        Assert.Single(_socket.OfType("session.input_audio.mute"));
    }

    [Fact]
    public async Task The_servers_time_limit_ends_the_session_as_a_time_limit()
    {
        await ConnectAsync();

        _socket.Push(new { type = "session.closed", reason = "close_requested", spine_reason = "time_limit", usage = new { seconds = 125 } });

        await Until(() => _ended.Count == 1, "ended");
        Assert.Equal(VoiceSessionEnd.TimeLimit, _ended[0].Reason);
    }

    [Fact]
    public async Task A_goodbye_ends_the_call_once_the_voice_has_said_it()
    {
        _session.EndsOnGoodbye = true;
        await ConnectAsync();

        _socket.Push(Envelope(new { type = "response.output_item.done", item = new { type = "function_call", call_id = "call_1", name = "end_conversation", arguments = "{}" } }));
        await Until(() => _socket.OfType("response.item.create").Any(), "the hang-up's result");
        _socket.Push(new { type = "session.output_audio.delta", delta = Audio(2400) });
        await Until(() => _audio.IsPlaying, "the farewell plays");
        _audio.Render(4800, 48_000);

        // The device keeps asking for audio; once none has come for a moment the farewell's item is done.
        for (var i = 0; i < 20 && _ended.Count == 0; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            _audio.Render(4800, 48_000);
            await Task.Delay(10);
        }

        Assert.Equal(VoiceSessionEnd.Goodbye, Assert.Single(_ended).Reason);
    }
}
