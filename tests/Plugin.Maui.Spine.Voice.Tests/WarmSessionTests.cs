using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>A link kept open and silent between conversations, so the next one starts at once.</summary>
public sealed class WarmSessionTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero));
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

    private static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), what);
    }

    [Fact]
    public async Task A_conversation_kept_warm_parks_the_link_and_the_next_one_reuses_it()
    {
        _options.KeepWarm = true;
        await _session.ConnectAsync("Voice");
        var requests = _tokens.Requests;

        await _session.EndAsync();
        Assert.Equal([(VoiceSessionEnd.User, null)], _ended);
        Assert.False(_session.IsConnected);
        Assert.True(_session.IsWarm);
        Assert.False(_socket.Closed);
        Assert.Equal(1, _audio.Stops);

        await _session.ConnectAsync("Voice");
        Assert.True(_session.IsConnected);
        Assert.False(_session.IsWarm);
        Assert.Equal(requests, _tokens.Requests);
        Assert.Single(_socket.OfType("input_audio_buffer.clear"));
        Assert.Equal(2, _audio.Starts);

        // The link ends with the session.
        await _session.DisposeAsync();
        Assert.True(_socket.Closed);
    }

    [Fact]
    public async Task Warming_opens_the_link_without_the_microphone_and_the_connect_takes_it()
    {
        await _session.WarmAsync("Voice");
        Assert.True(_session.IsWarm);
        Assert.False(_session.IsConnected);
        Assert.Equal(0, _audio.Starts);
        Assert.Equal("wss://api.openai.com/v1/realtime?model=gpt-realtime", _socket.Url?.ToString());
        Assert.Equal(1, _tokens.Requests);
        Assert.Empty(_ended);

        await _session.WarmAsync("Voice");
        Assert.Equal(1, _tokens.Requests);

        await _session.ConnectAsync("Voice");
        Assert.True(_session.IsConnected);
        Assert.Equal(1, _tokens.Requests);
        Assert.Equal(1, _audio.Starts);
    }

    [Fact]
    public async Task A_parked_link_the_provider_closes_is_opened_again_by_the_next_connect()
    {
        _options.KeepWarm = true;
        await _session.ConnectAsync("Voice");
        await _session.EndAsync();
        Assert.True(_session.IsWarm);

        _socket.Hangup();
        await Until(() => !_session.IsWarm, "the lost link is let go");
        Assert.Single(_ended);

        await _session.ConnectAsync("Voice");
        Assert.True(_session.IsConnected);
        Assert.Equal(2, _tokens.Requests);
    }

    [Fact]
    public async Task A_live_feature_is_never_kept_warm()
    {
        _tokens.Body = """
            { "feature": "Voice", "protocol": "openai-live", "model": "gpt-live-1", "url": "realtime/Voice/relay?ticket=t1",
              "token": "t1", "expiresAt": "2026-10-29T10:01:00+00:00", "maxMinutes": 2 }
            """;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _session.WarmAsync("Voice"));
        Assert.False(_session.IsWarm);

        _options.KeepWarm = true;
        await _session.ConnectAsync("Voice");
        await _session.EndAsync();
        Assert.False(_session.IsWarm);
        Assert.True(_socket.Closed);
    }

    [Fact]
    public async Task A_connect_for_another_feature_closes_the_parked_link_first()
    {
        await _session.WarmAsync("Voice");
        await _session.ConnectAsync("Other");
        Assert.True(_session.IsConnected);
        Assert.Equal(2, _tokens.Requests);
        Assert.True(_socket.Closed);
    }
}
