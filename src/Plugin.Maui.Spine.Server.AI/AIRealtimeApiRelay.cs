using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// One OpenAI Realtime session through the server (<c>Realtime:Relay</c>): the app's WebSocket on one side, the
/// provider's on the other, both speaking the Realtime API's own events. The server opens the provider's socket with its
/// key, writes the session from the feature's configuration, passes on only what an app may send (its own functions as
/// far as the feature lists them, audio, the user's items and function results, <c>response.create</c> and
/// <c>response.cancel</c>), runs the feature's server functions itself, converts the audio to the rate and form the app
/// asked for, counts every response's usage and ends the session at its limit.
/// </summary>
/// <remarks>
/// Spine's own events: the app may send <c>spine.audio { rate, binary }</c> before any audio and gets the same event back
/// with what the relay applies (<c>session.created</c> carries it as <c>spine_audio</c> too); <c>spine.function.output</c>
/// reports a server function's result; <c>session.close</c> from the app ends the session. Everything else is the Realtime
/// API as the provider speaks it, so a client that speaks it directly speaks it through the relay unchanged.
/// </remarks>
internal sealed class AIRealtimeApiRelay
{
    private const int MaxAppMessage = 256 * 1024;
    private const int MaxProviderMessage = 16 * 1024 * 1024;
    private const int Rate = 24_000;
    private const int MinAppRate = 8_000;
    private const int MaxAppRate = 48_000;
    private static readonly TimeSpan LimitGrace = TimeSpan.FromSeconds(5);

    private readonly HttpContext _context;
    private readonly WebSocket _app;
    private readonly AIFeature _feature;
    private readonly AICaller _caller;
    private readonly AIRealtimeTicket _ticket;
    private readonly IAIRealtimeRelayProvider _provider;
    private readonly AIFeatures _features;
    private readonly SpineAIOptions _options;
    private readonly AIBudgetGate _gate;
    private readonly AIUsageMeter _meter;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    private readonly SemaphoreSlim _toApp = new(1, 1);
    private readonly SemaphoreSlim _toProvider = new(1, 1);
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SpineAIFunction> _serverRuns = new(StringComparer.Ordinal);
    private readonly List<string> _unlisted = new();
    private readonly HashSet<string> _countedResponses = new(StringComparer.Ordinal);
    private readonly UsageDetails _usage = new() { AdditionalCounts = new() };
    private readonly LiveAudioStats _audio;

    private WebSocket? _upstream;
    private string? _sessionId;
    private long _startedAt;
    private int _responses;
    private volatile bool _closing;
    private volatile bool _timeLimit;
    private int _appRate = Rate;
    private bool _binary;
    private PcmResampler? _resampleToProvider;
    private PcmResampler? _resampleToApp;

    public AIRealtimeApiRelay(
        HttpContext context, WebSocket app, AIFeature feature, AICaller caller, AIRealtimeTicket ticket, IAIRealtimeRelayProvider provider,
        AIFeatures features, SpineAIOptions options, AIBudgetGate gate, AIUsageMeter meter, ILogger logger)
    {
        _context = context;
        _app = app;
        _feature = feature;
        _caller = caller;
        _ticket = ticket;
        _provider = provider;
        _features = features;
        _options = options;
        _gate = gate;
        _meter = meter;
        _logger = logger;
        _time = meter.Time;
        _audio = new LiveAudioStats(_time);
    }

    private AIRealtimeOptions Options => _feature.Options.Realtime ?? new AIRealtimeOptions();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var opened = _time.GetTimestamp();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ITimer? limit = null;

        try
        {
            var request = new AIRealtimeRequest(_feature.Name, _feature.Model, _feature.Options.SystemPrompt, _feature.Options.MaxOutputTokens, Options, _caller)
            {
                Tools = _features.FunctionsOf(_feature),
                MaxMinutes = _ticket.Minutes,
            };

            try
            {
                _upstream = await _provider.ConnectRealtimeAsync(request, run.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "AI feature '{Feature}': the Realtime provider could not be reached for {Caller}.", _feature.Name, _caller.Key);
                await SendAppAsync(new JsonObject
                {
                    ["type"] = "error",
                    ["error"] = new JsonObject { ["code"] = "provider_unreachable", ["message"] = "The voice service could not be reached." },
                }.ToJsonString(), run.Token);
                await CloseAppAsync(WebSocketCloseStatus.InternalServerError, "The voice service could not be reached.");
                return;
            }

            _startedAt = _time.GetTimestamp();
            // The feature's session, and the server's own functions; the app's functions come with its session.update.
            var session = _provider.SessionOf(request);
            session["tools"] = Tools(null);
            session["tool_choice"] = "auto";
            await SendProviderAsync(new JsonObject { ["type"] = "session.update", ["session"] = session }.ToJsonString(), run.Token);
            limit = _time.CreateTimer(_ => _ = EndAtLimitAsync(), null, TimeSpan.FromMinutes(_ticket.Minutes) + LimitGrace, Timeout.InfiniteTimeSpan);

            var fromProvider = FromProviderAsync(_upstream, run.Token);
            var fromApp = FromAppAsync(run.Token);

            if (await Task.WhenAny(fromProvider, fromApp) == fromApp)
            {
                // The app hung up: the provider's session ends with its socket, and so does the billing.
                _closing = true;
                await CloseUpstreamAsync();
            }
            else
            {
                await CloseAppAsync(WebSocketCloseStatus.NormalClosure, _timeLimit ? "The session reached its time limit." : "The session ended.");
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
        }
        catch (WebSocketException e)
        {
            _logger.LogInformation(e, "AI feature '{Feature}': a Realtime connection for {Caller} dropped.", _feature.Name, _caller.Key);
        }
        finally
        {
            limit?.Dispose();
            await run.CancelAsync();
            _upstream?.Abort();
            _upstream?.Dispose();
            await SettleAsync(_time.GetElapsedTime(opened));
        }
    }

    private async Task EndAtLimitAsync()
    {
        if (_closing) return;
        _timeLimit = true;
        _closing = true;
        _logger.LogInformation("AI feature '{Feature}': the Realtime session {Session} for {Caller} reached its limit of {Minutes} min.",
            _feature.Name, _sessionId, _caller.Key, _ticket.Minutes);
        await CloseUpstreamAsync();
        await CloseAppAsync(WebSocketCloseStatus.NormalClosure, "The session reached its time limit.");
    }

    private async Task CloseUpstreamAsync()
    {
        if (_upstream is not { State: WebSocketState.Open or WebSocketState.CloseReceived } upstream) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await upstream.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "The session ended.", timeout.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task FromProviderAsync(WebSocket upstream, CancellationToken cancellationToken)
    {
        while (await AIRealtimeRelay.ReceiveAsync(upstream, MaxProviderMessage, cancellationToken) is { } received)
        {
            var message = received.Text;
            var type = AIRealtimeRelay.TypeOf(message);
            switch (type)
            {
                case "session.created":
                    var created = AIRealtimeRelay.Parse(message);
                    _sessionId = created?["session"]?["id"]?.GetValue<string>();
                    _logger.LogInformation("AI feature '{Feature}': Realtime session {Session} for {Caller} through the relay, limit {Minutes} min.",
                        _feature.Name, _sessionId, _caller.Key, _ticket.Minutes);
                    if (created is not null)
                    {
                        created["spine_audio"] = AudioApplied();
                        message = created.ToJsonString();
                    }
                    break;

                case "response.output_audio.delta" or "response.audio.delta":
                    if (AIRealtimeRelay.Parse(message) is not { } output || AIRealtimeRelay.Pcm(output["delta"]) is not { } pcm) break;
                    var ms = pcm.Length / 2.0 * 1000 / Rate;
                    var sending = _time.GetTimestamp();
                    if (_binary)
                        await SendAppAsync(_resampleToApp?.Resample(pcm) ?? pcm, WebSocketMessageType.Binary, cancellationToken);
                    else
                    {
                        if (_resampleToApp is not null)
                        {
                            output["delta"] = Convert.ToBase64String(_resampleToApp.Resample(pcm));
                            message = output.ToJsonString();
                        }
                        await SendAppAsync(message, cancellationToken);
                    }
                    _audio.Audible(sending, ms, _time.GetElapsedTime(sending));
                    continue;

                case "response.function_call_arguments.done":
                    if (AIRealtimeRelay.Parse(message) is { } call && AIRealtimeRelay.Text(call, "name") is { } name && AIRealtimeRelay.Text(call, "call_id") is { } callId)
                    {
                        SpineAIFunction? server;
                        lock (_lock)
                            server = _serverRuns.GetValueOrDefault(name);
                        if (server is not null)
                        {
                            // The server's own function: run here, never shown to the app as a call for it to make.
                            _ = Task.Run(() => ServerFunctionAsync(callId, server, AIRealtimeRelay.Text(call, "arguments"), cancellationToken), cancellationToken);
                            continue;
                        }
                    }
                    break;

                case "response.done":
                    if (AIRealtimeRelay.Parse(message)?["response"] is JsonObject response)
                        Count(response);
                    break;
            }

            await SendAppAsync(message, cancellationToken);
        }
    }

    private async Task FromAppAsync(CancellationToken cancellationToken)
    {
        while (await AIRealtimeRelay.ReceiveAsync(_app, MaxAppMessage, cancellationToken) is { } received)
        {
            if (received.Binary)
            {
                if (!_binary)
                    _logger.LogDebug("AI feature '{Feature}': dropped a binary message from {Caller}'s Realtime session.", _feature.Name, _caller.Key);
                else if (!_closing)
                    await SendProviderAsync(new JsonObject
                    {
                        ["type"] = "input_audio_buffer.append",
                        ["audio"] = Convert.ToBase64String(_resampleToProvider?.Resample(received.Bytes) ?? received.Bytes),
                    }.ToJsonString(), cancellationToken);
                continue;
            }

            var message = received.Text;
            var type = AIRealtimeRelay.TypeOf(message);
            switch (type)
            {
                case "spine.audio":
                    var asked = AIRealtimeRelay.Parse(message);
                    Audio(asked?["audio"] as JsonObject ?? asked);
                    await SendAppAsync(new JsonObject { ["type"] = "spine.audio", ["rate"] = _appRate, ["binary"] = _binary }.ToJsonString(), cancellationToken);
                    break;

                case "session.update":
                    // Only the app's functions: the instructions, voice and audio are the feature's.
                    var tools = AIRealtimeRelay.Parse(message)?["session"]?["tools"] as JsonArray;
                    await SendProviderAsync(new JsonObject
                    {
                        ["type"] = "session.update",
                        ["session"] = new JsonObject { ["type"] = "realtime", ["tools"] = Tools(tools), ["tool_choice"] = "auto" },
                    }.ToJsonString(), cancellationToken);
                    break;

                case "input_audio_buffer.append":
                    if (!_closing)
                        await SendProviderAsync(_resampleToProvider is null ? message : Resampled(message, _resampleToProvider), cancellationToken);
                    break;

                case "input_audio_buffer.commit" or "input_audio_buffer.clear" or "conversation.item.truncate" or "conversation.item.delete"
                    or "conversation.item.retrieve" or "response.cancel":
                    await SendProviderAsync(message, cancellationToken);
                    break;

                case "conversation.item.create":
                    // The user's own words and the results of the functions the app ran; no instructions in disguise.
                    if (AIRealtimeRelay.Parse(message)?["item"] is JsonObject item
                        && (AIRealtimeRelay.Text(item, "type") == "function_call_output"
                            || (AIRealtimeRelay.Text(item, "type") == "message" && AIRealtimeRelay.Text(item, "role") is "user" or "assistant")))
                        await SendProviderAsync(message, cancellationToken);
                    break;

                case "response.create":
                    await SendProviderAsync("""{"type":"response.create"}""", cancellationToken);
                    break;

                case "session.close":
                    _closing = true;
                    await CloseUpstreamAsync();
                    return;

                default:
                    _logger.LogDebug("AI feature '{Feature}': dropped '{Type}' from {Caller}'s Realtime session.", _feature.Name, type, _caller.Key);
                    break;
            }
        }
    }

    private async Task ServerFunctionAsync(string callId, SpineAIFunction function, string? arguments, CancellationToken cancellationToken)
    {
        object? result;
        try
        {
            using var lease = _gate.Acquire(_feature, _caller, "functions");
            await _gate.CheckAsync(_feature, _caller, cancellationToken);

            await using var scope = _context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var client = AIClientContext.From(_context.Request);
            var context = new SpineAIFunctionContext(_caller, SpineAIFunctionSide.Server)
            {
                Feature = _feature.Name,
                ConversationId = client.Conversation,
                CallId = callId,
                TimeZone = client.TimeZone,
                Locale = client.Locale,
                User = _caller.Kind == AICallerKind.SignedIn ? _context.User : new ClaimsPrincipal(new ClaimsIdentity()),
                Services = scope.ServiceProvider,
                CancellationToken = cancellationToken,
                MaxResultCharacters = _feature.Options.MaxToolResultCharacters ?? SpineAIFunction.DefaultMaxResultCharacters,
            };

            _logger.LogInformation("AI feature '{Feature}': the Realtime model called '{Function}' ({CallId}) for {Caller}.", _feature.Name, function.Name, callId, _caller.Key);
            result = SpineAIFunctionResult.TryParseArguments(arguments, out var parsed, out var invalid)
                ? await function.InvokeAsync(parsed, context)
                : invalid;
        }
        catch (AIFeatureException e)
        {
            result = SpineAIFunctionResult.Error(e.Message, "refused");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "AI feature '{Feature}': the server function '{Function}' failed.", _feature.Name, function.Name);
            result = SpineAIFunctionResult.Error($"The function '{function.Name}' failed.");
        }

        var output = result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            _ => JsonSerializer.Serialize(result, SpineAIEndpoints.Json),
        };

        try
        {
            await SendProviderAsync(new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject { ["type"] = "function_call_output", ["call_id"] = callId, ["output"] = output },
            }.ToJsonString(), cancellationToken);
            // The Realtime API does not go on by itself after a function's result.
            await SendProviderAsync("""{"type":"response.create"}""", cancellationToken);
            await SendAppAsync(new JsonObject
            {
                ["type"] = "spine.function.output",
                ["call_id"] = callId,
                ["name"] = function.Name,
                ["output"] = output,
            }.ToJsonString(), cancellationToken);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Sums a response's tokens. Audio tokens are priced apart from text (ten and more times the price), so they go in
    /// <see cref="UsageDetails.AdditionalCounts"/> as <c>audio_input_tokens</c> and <c>audio_output_tokens</c> and are
    /// taken out of the plain counts; cached tokens stay one count, text and audio together.
    /// </summary>
    private void Count(JsonObject response)
    {
        if (response["usage"] is not JsonObject usage) return;
        var id = AIRealtimeRelay.Text(response, "id") ?? Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            if (!_countedResponses.Add(id)) return;
            _responses++;
            var input = AIRealtimeRelay.Number(usage["input_tokens"]);
            var output = AIRealtimeRelay.Number(usage["output_tokens"]);
            var audioIn = AIRealtimeRelay.Number(usage["input_token_details"]?["audio_tokens"]);
            var audioOut = AIRealtimeRelay.Number(usage["output_token_details"]?["audio_tokens"]);
            var cached = AIRealtimeRelay.Number(usage["input_token_details"]?["cached_tokens"]);
            _usage.InputTokenCount = (_usage.InputTokenCount ?? 0) + Math.Max(0, input - audioIn);
            _usage.CachedInputTokenCount = (_usage.CachedInputTokenCount ?? 0) + cached;
            _usage.OutputTokenCount = (_usage.OutputTokenCount ?? 0) + Math.Max(0, output - audioOut);
            var counts = _usage.AdditionalCounts!;
            counts["audio_input_tokens"] = counts.GetValueOrDefault("audio_input_tokens") + audioIn;
            counts["audio_output_tokens"] = counts.GetValueOrDefault("audio_output_tokens") + audioOut;
        }
    }

    /// <summary>
    /// Settles the reservation with what the session used: no minutes (the reservation is taken off), and the tokens of
    /// every response at the model's prices. A session with no response costs nothing.
    /// </summary>
    private async Task SettleAsync(TimeSpan duration)
    {
        UsageDetails? usage;
        int responses;
        lock (_lock)
        {
            responses = _responses;
            usage = responses > 0 ? _usage : null;
        }
        await _meter.SettleAsync(_feature, _caller, _feature.Model, usage, 0, (decimal)_ticket.Minutes, duration);

        _audio.End();
        if (_audio.AudibleDeltas > 0)
            _logger.LogInformation(
                "AI feature '{Feature}': Realtime audio for {Caller}: {Audible:0.#} s in {Deltas} deltas; delivery had {Gaps} gaps over 300 ms (longest {Gap:0} ms), slowest stretch {Rate:0.00}x over {Span:0.#} s; longest send to the app {Send:0} ms.",
                _feature.Name, _caller.Key, _audio.AudibleMs / 1000, _audio.AudibleDeltas, _audio.Gaps, _audio.LongestGapMs,
                double.IsPositiveInfinity(_audio.SlowestRate) ? 1 : _audio.SlowestRate, _audio.SlowestStretchSeconds, _audio.LongestSendMs);
        _logger.LogInformation(
            "AI feature '{Feature}': Realtime session {Session} for {Caller} ended ({Reason}) after {Seconds:0.#} s: {Responses} responses, {Input} text in ({Cached} cached), {AudioIn} audio in, {Output} text out, {AudioOut} audio out.",
            _feature.Name, _sessionId ?? "(not started)", _caller.Key, _timeLimit ? "time limit" : "closed", duration.TotalSeconds, responses,
            usage?.InputTokenCount ?? 0, usage?.CachedInputTokenCount ?? 0, usage?.AdditionalCounts?.GetValueOrDefault("audio_input_tokens") ?? 0,
            usage?.OutputTokenCount ?? 0, usage?.AdditionalCounts?.GetValueOrDefault("audio_output_tokens") ?? 0);
    }

    private JsonArray Tools(JsonArray? appTools)
    {
        var list = AIRelayTools.Build(_features, _feature, _options, appTools, [], out var serverRuns, out var unlisted);
        lock (_lock)
        {
            _serverRuns.Clear();
            foreach (var (name, function) in serverRuns)
                _serverRuns[name] = function;
            _unlisted.Clear();
            _unlisted.AddRange(unlisted);
        }
        if (unlisted.Count > 0)
            _logger.LogWarning(
                "AI feature '{Feature}': {Caller}'s Realtime session offered functions the feature does not list, so the model will not see them: {Names}. List them in the feature's Functions and declare them with AddDevice.",
                _feature.Name, _caller.Key, string.Join(", ", unlisted));
        return list;
    }

    private Task SendAppAsync(string message, CancellationToken cancellationToken) =>
        SendAppAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, cancellationToken);

    private async Task SendAppAsync(byte[] message, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        if (_app.State != WebSocketState.Open) return;
        await _toApp.WaitAsync(cancellationToken);
        try
        {
            await _app.SendAsync(message, type, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _toApp.Release();
        }
    }

    private async Task SendProviderAsync(string message, CancellationToken cancellationToken)
    {
        if (_upstream is not { State: WebSocketState.Open } upstream) return;
        await _toProvider.WaitAsync(cancellationToken);
        try
        {
            await upstream.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _toProvider.Release();
        }
    }

    private async Task CloseAppAsync(WebSocketCloseStatus status, string description)
    {
        if (_app.State is not (WebSocketState.Open or WebSocketState.CloseReceived)) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _app.CloseOutputAsync(status, description, timeout.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
        }
    }

    private JsonObject AudioApplied() => new() { ["rate"] = _appRate, ["binary"] = _binary };

    /// <summary>The audio the app asked for: its own rate (resampled both ways) and raw PCM16 in binary messages.</summary>
    private bool Audio(JsonObject? audio)
    {
        if (audio?["rate"] is JsonValue rate)
        {
            if (!rate.TryGetValue<int>(out var value) || value is < MinAppRate or > MaxAppRate) return false;
            _appRate = value;
        }
        _binary = audio?["binary"] is JsonValue binary && binary.TryGetValue<bool>(out var raw) && raw;
        if (_appRate != Rate)
        {
            _resampleToProvider = new PcmResampler(_appRate, Rate);
            _resampleToApp = new PcmResampler(Rate, _appRate);
        }
        else
        {
            _resampleToProvider = _resampleToApp = null;
        }
        return true;
    }

    private static string Resampled(string append, PcmResampler resampler)
    {
        if (AIRealtimeRelay.Parse(append) is not { } json || AIRealtimeRelay.Pcm(json["audio"]) is not { } pcm) return append;
        json["audio"] = Convert.ToBase64String(resampler.Resample(pcm));
        return json.ToJsonString();
    }
}
