using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>A direct session sums what the provider reported it used and tells the server when it ends.</summary>
public sealed class UsageReportTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly ScriptedSocket _socket = new();
    private readonly TokenHandler _tokens = new()
    {
        Body = """
            { "feature": "Voice", "protocol": "openai", "model": "gpt-realtime-2.1-mini", "url": "wss://api.openai.com/v1/realtime?model=gpt-realtime-2.1-mini",
              "token": "ek_test", "expiresAt": "2026-10-29T10:01:00+00:00", "maxMinutes": 3, "usageTicket": "u1" }
            """,
    };
    private readonly SpineVoiceOptions _options = new();
    private FakePlatform _audio = null!;
    private VoiceSession _session = null!;
    private readonly Lock _ui = new();

    public Task InitializeAsync()
    {
        _options.SessionEndpoint = new Uri("https://api.example.com/ai");
        _options.AuthorizationHeader = _ => Task.FromResult<string?>("Bearer app-token");
        _options.HttpHandler = _tokens;
        _audio = new FakePlatform(_time);
        _session = new VoiceSession(_audio, _options, new VoiceTokenClient(_options, _ => Task.FromResult<string?>("phone-1")),
            () => _socket, action => { lock (_ui) action(); }, _time);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _session.DisposeAsync();

    [Fact]
    public async Task What_the_provider_reported_goes_to_the_server_when_the_session_ends()
    {
        await _session.ConnectAsync("Voice");
        _socket.Push(new { type = "response.done", response = new { id = "r1", status = "completed", usage = new { input_tokens = 600, output_tokens = 200,
            input_token_details = new { cached_tokens = 300, audio_tokens = 100 }, output_token_details = new { audio_tokens = 150 } } } });
        _socket.Push(new { type = "response.done", response = new { id = "r2", status = "completed", usage = new { input_tokens = 400, output_tokens = 100,
            input_token_details = new { cached_tokens = 200, audio_tokens = 100 }, output_token_details = new { audio_tokens = 50 } } } });
        for (var i = 0; i < 200 && _socket.Sent.Count < 1; i++)
            await Task.Delay(10);
        _time.Advance(TimeSpan.FromSeconds(42));
        await Task.Delay(100);

        await _session.EndAsync();
        for (var i = 0; i < 300 && _tokens.UsageReports.IsEmpty; i++)
            await Task.Delay(10);

        Assert.True(_tokens.UsageReports.TryDequeue(out var report), "no usage report was posted");
        Assert.Equal("https://api.example.com/ai/realtime/Voice/usage", report.Request.RequestUri!.ToString());
        Assert.Equal("Bearer app-token", report.Request.Headers.Authorization?.ToString());
        using var body = JsonDocument.Parse(report.Body);
        Assert.Equal("u1", body.RootElement.GetProperty("ticket").GetString());
        var usage = body.RootElement.GetProperty("usage");
        Assert.Equal(1000, usage.GetProperty("inputTokens").GetInt64());
        Assert.Equal(500, usage.GetProperty("cachedInputTokens").GetInt64());
        Assert.Equal(300, usage.GetProperty("outputTokens").GetInt64());
        Assert.Equal(200, usage.GetProperty("audioInputTokens").GetInt64());
        Assert.Equal(200, usage.GetProperty("audioOutputTokens").GetInt64());
        Assert.Equal(42, body.RootElement.GetProperty("seconds").GetDouble(), 1);
    }

    [Fact]
    public async Task A_session_with_no_usage_ticket_or_no_responses_reports_nothing()
    {
        await _session.ConnectAsync("Voice");
        await _session.EndAsync();
        await Task.Delay(100);
        Assert.True(_tokens.UsageReports.IsEmpty);
    }
}
