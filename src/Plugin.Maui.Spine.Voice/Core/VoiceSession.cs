using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Server.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// A voice session speaking OpenAI's realtime events over a WebSocket (protocol <c>openai</c>, which Azure OpenAI speaks
/// too), with the audio through <see cref="IVoiceAudio"/>.
/// </summary>
internal sealed class VoiceSession : IVoiceSession
{
    public const string InterruptedKey = "spine.interrupted";

    /// <summary>The tool <see cref="EndsOnGoodbye"/> gives the model.</summary>
    public const string EndConversationTool = "end_conversation";

    // EndsOnGoodbye's way out: asked for (the tool ran), the farewell under way, the farewell done (end once it has played).
    private const int NoGoodbye = 0, FarewellAsked = 1, FarewellUnderWay = 2, FarewellDone = 3;
    private int _goodbye;
    // How long the farewell must have been quiet before the call ends: the queue says it has played when its last sample
    // went to the device, which still has the output buffer and the speaker's latency to go. Ending at once cut the last word.
    private static readonly TimeSpan FarewellTail = TimeSpan.FromMilliseconds(700);
    private long _quietSince;

    /// <summary>The provider's rate both ways.</summary>
    // Microsoft.Extensions.AI's options indent, which the model pays for in tokens.
    private static readonly JsonSerializerOptions Compact = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private const int Rate = 24_000;

    /// <summary>Microphone audio is sent in frames of about 100 ms.</summary>
    private const int FrameBytes = Rate / 10 * 2;

    private static readonly TimeSpan InterruptedFor = TimeSpan.FromMilliseconds(600);

    private readonly IVoiceAudio _audio;

    /// <summary>The engine the session plays through; an avatar's driver reads its playback position for the mouth's clock.</summary>
    internal IVoiceAudio Audio => _audio;
    private readonly SpineVoiceOptions _options;
    private readonly VoiceTokenClient _tokens;
    private readonly IServiceProvider? _services;
    private readonly Dictionary<string, SpineAIFunctionSide> _preferred = new(StringComparer.Ordinal);
    private VoiceSessionToken? _token;
    private readonly Func<IVoiceSocket> _socketFactory;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ObservableCollection<ChatMessage> _transcript = [];
    private readonly Dictionary<string, ChatMessage> _byItem = new(StringComparer.Ordinal);
    private readonly object _micGate = new();
    private readonly MemoryStream _mic = new();
    private readonly object _gate = new();
    private readonly object _pendingGate = new();
    private readonly Dictionary<string, string> _pendingText = new(StringComparer.Ordinal);

    private IVoiceSocket? _socket;
    private Channel<object>? _outbox;
    private CancellationTokenSource? _run;
    private ITimer? _ticker;
    private ITimer? _limit;
    private long _endsAt;
    private volatile bool _connected;
    private volatile bool _connecting;
    private volatile bool _responseActive;
    private volatile bool _answerAudio;
    private long _interruptedUntil;
    private VoiceState _state = VoiceState.Idle;
    private int _ended;

    public VoiceSession(
        IVoiceAudio audio, SpineVoiceOptions options, VoiceTokenClient tokens, Func<IVoiceSocket> socketFactory,
        Action<Action> dispatch, TimeProvider? time = null, ILogger? logger = null, IServiceProvider? services = null)
    {
        _audio = audio;
        _options = options;
        _tokens = tokens;
        _services = services;
        _socketFactory = socketFactory;
        _dispatch = dispatch;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _audio.StateChanged += _ => Evaluate();
    }

    public VoiceState State { get { lock (_gate) return _state; } }
    public bool IsConnected => _connected;
    public IList<ChatMessage> Transcript => _transcript;

    public bool IsMuted
    {
        get => _audio.IsMuted;
        set
        {
            _audio.IsMuted = value;
            Evaluate();
        }
    }

    public VoiceDuplex Duplex
    {
        get => _audio.Duplex;
        set => _audio.Duplex = value;
    }

    public TimeSpan? TimeLeft
    {
        get
        {
            var endsAt = Interlocked.Read(ref _endsAt);
            if (!_connected || endsAt == 0)
                return null;
            var left = TimeSpan.FromTicks(endsAt - _time.GetUtcNow().UtcTicks);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public event Action<VoiceState>? StateChanged;
    public event Action<VoiceSessionEnd, Exception?>? Ended;

    public string? Voice { get; set; }

    public string? SpokenVoice { get; private set; }

    public bool EndsOnGoodbye
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            if (_connected)
                Send(ToolsUpdate());
            if (!value)
                Interlocked.Exchange(ref _goodbye, NoGoodbye);
        }
    }

    public async Task ConnectAsync(string feature, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (_connected || _connecting)
            throw new InvalidOperationException("The voice session is already connected; end it before connecting again.");

        _connecting = true;
        _ended = 0;
        // A new conversation starts with an empty transcript.
        _dispatch(() =>
        {
            _transcript.Clear();
            _byItem.Clear();
        });
        lock (_pendingGate)
            _pendingText.Clear();
        Evaluate();
        try
        {
            var token = await _tokens.RequestAsync(feature, cancellationToken, Voice);
            _token = token;
            SpokenVoice = token.Voice;
            if (token.Protocol != "openai")
                throw new NotSupportedException($"The voice feature '{feature}' speaks '{token.Protocol}', which Spine.Voice cannot speak yet; only 'openai' (OpenAI and Azure OpenAI).");

            var socket = _socketFactory();
            _socket = socket;
            await socket.ConnectAsync(token.Url, token.Token, cancellationToken);

            await _audio.StartAsync(new VoiceAudioOptions { InputRate = Rate, OutputRate = Rate, Duplex = _audio.Duplex }, cancellationToken);
            _audio.InputAvailable += OnMicrophone;

            _run = new CancellationTokenSource();
            _outbox = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
            Interlocked.Exchange(ref _goodbye, NoGoodbye);
            if (_options.Functions.Count > 0 || token.Tools.Count > 0 || EndsOnGoodbye)
                Send(ToolsUpdate());

            if (token.MaxMinutes is { } minutes and > 0)
            {
                var limit = TimeSpan.FromMinutes(minutes);
                Interlocked.Exchange(ref _endsAt, (_time.GetUtcNow() + limit).UtcTicks);
                _limit = _time.CreateTimer(_ => _ = EndCoreAsync(VoiceSessionEnd.TimeLimit, null), null, limit, Timeout.InfiniteTimeSpan);
            }
            _ticker = _time.CreateTimer(_ => Tick(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

            // Connected before the loops start: a provider that hangs up at once then ends a connected session, with
            // Ended raised, rather than leaving one that looks connected and hears nothing.
            _connected = true;
            _ = SendLoopAsync(socket, _outbox.Reader, _run.Token);
            _ = ReceiveLoopAsync(socket, _run.Token);
            _logger.LogInformation("Spine.Voice: connected to '{Feature}' over {Protocol}.", token.Feature, token.Protocol);
        }
        catch
        {
            await TearDownAsync();
            throw;
        }
        finally
        {
            _connecting = false;
            Evaluate();
        }
    }

    public Task EndAsync() => EndCoreAsync(VoiceSessionEnd.User, null);

    public void Interrupt()
    {
        if (_connected)
            BargeIn();
    }

    public void BeginTurn()
    {
        if (!_connected)
            return;
        BargeIn();
        Send(new JsonObject { ["type"] = "input_audio_buffer.clear" });
    }

    public void EndTurn()
    {
        if (!_connected)
            return;
        FlushMicrophone();
        Send(new JsonObject { ["type"] = "input_audio_buffer.commit" });
        Send(new JsonObject { ["type"] = "response.create" });
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!_connected)
            throw new InvalidOperationException("Connect the voice session before sending text.");

        BargeIn();
        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.User, text)));
        Send(new JsonObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = new JsonObject
            {
                ["type"] = "message",
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = text }),
            },
        });
        Send(new JsonObject { ["type"] = "response.create" });
        return Task.CompletedTask;
    }

    public VoiceFrame UpdateFrame()
    {
        var frame = _audio.UpdateFrame();
        Evaluate();
        frame.State = State;
        frame.Viseme = _options.VisemeSource is { } visemes && _audio.Playing is { } playing ? visemes.VisemeAt(playing) : -1;
        return frame;
    }

    public async ValueTask DisposeAsync()
    {
        await EndCoreAsync(VoiceSessionEnd.User, null);
        _mic.Dispose();
    }

    private void OnMicrophone(ReadOnlySpan<byte> pcm16)
    {
        lock (_micGate)
        {
            _mic.Write(pcm16);
            if (_mic.Length < FrameBytes)
                return;

            // Whole frames out, the rest kept for the next buffer.
            var buffered = _mic.GetBuffer().AsSpan(0, (int)_mic.Length);
            var whole = buffered.Length / FrameBytes * FrameBytes;
            for (var offset = 0; offset < whole; offset += FrameBytes)
                _outbox?.Writer.TryWrite(buffered.Slice(offset, FrameBytes).ToArray());
            var rest = buffered[whole..].ToArray();
            _mic.SetLength(0);
            _mic.Write(rest);
        }
    }

    private void FlushMicrophone()
    {
        byte[]? rest = null;
        lock (_micGate)
        {
            if (_mic.Length > 0)
            {
                rest = _mic.ToArray();
                _mic.SetLength(0);
            }
        }
        if (rest is not null)
            _outbox?.Writer.TryWrite(rest);
    }

    private void Send(JsonObject message) => _outbox?.Writer.TryWrite(message.ToJsonString());

    private async Task SendLoopAsync(IVoiceSocket socket, ChannelReader<object> outbox, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in outbox.ReadAllAsync(cancellationToken))
            {
                var message = item switch
                {
                    byte[] audio => $$"""{"type":"input_audio_buffer.append","audio":"{{Convert.ToBase64String(audio)}}"}""",
                    string json => json,
                    _ => null,
                };
                if (message is not null)
                    await socket.SendAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            await EndCoreAsync(VoiceSessionEnd.Failed, e);
        }
    }

    private async Task ReceiveLoopAsync(IVoiceSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (await socket.ReceiveAsync(cancellationToken) is { } message)
            {
                try
                {
                    using var json = JsonDocument.Parse(message);
                    HandleEvent(json.RootElement, cancellationToken);
                }
                catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
                {
                    // One event the session cannot read is skipped, not the whole conversation.
                    _logger.LogWarning(e, "Spine.Voice: skipped an event it could not read.");
                }
            }

            _logger.LogInformation("Spine.Voice: the provider closed the connection ({Reason}).", socket.CloseReason ?? "no reason");
            await EndCoreAsync(VoiceSessionEnd.Provider, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            await EndCoreAsync(VoiceSessionEnd.Failed, e);
        }
    }

    private void HandleEvent(JsonElement e, CancellationToken cancellationToken)
    {
        var type = Text(e, "type");
        switch (type)
        {
            case "input_audio_buffer.speech_started":
                // The user talks over the answer: cut it off where it was heard.
                if (_audio.IsPlaying || _responseActive)
                    BargeIn();
                break;

            case "input_audio_buffer.committed" when Text(e, "item_id") is { } userItem:
                _dispatch(() => AddMessage(userItem, new ChatMessage(ChatRole.User, "…")));
                break;

            case "conversation.item.input_audio_transcription.completed" when Text(e, "item_id") is { } heardItem:
                var heard = Text(e, "transcript")?.Trim();
                _dispatch(() =>
                {
                    if (string.IsNullOrEmpty(heard))
                        RemoveMessage(heardItem);
                    else
                        ReplaceText(heardItem, ChatRole.User, heard, append: false);
                });
                break;

            case "conversation.item.input_audio_transcription.failed" when Text(e, "item_id") is { } lostItem:
                _dispatch(() => RemoveMessage(lostItem));
                break;

            case "response.created":
                Interlocked.CompareExchange(ref _goodbye, FarewellUnderWay, FarewellAsked);
                _responseActive = true;
                _answerAudio = false;
                break;

            case "response.output_audio.delta" or "response.audio.delta"
                when Text(e, "item_id") is { } item && Text(e, "delta") is { } delta:
                _audio.Enqueue(item, Convert.FromBase64String(delta));
                _answerAudio = true;
                break;

            case "response.output_audio.done" or "response.audio.done" when Text(e, "item_id") is { } doneItem:
                _audio.Complete(doneItem);
                break;

            case "response.output_audio_transcript.delta" or "response.audio_transcript.delta"
                when Text(e, "item_id") is { } textItem && Text(e, "delta") is { } words:
                // Collected and shown on the next tick: a bound list redraws a row ten times a second, not per word.
                lock (_pendingGate)
                    _pendingText[textItem] = _pendingText.GetValueOrDefault(textItem) + words;
                break;

            case "response.output_audio_transcript.done" or "response.audio_transcript.done"
                when Text(e, "item_id") is { } spokenItem && Text(e, "transcript") is { } spoken:
                lock (_pendingGate)
                    _pendingText.Remove(spokenItem);
                _dispatch(() => ReplaceText(spokenItem, ChatRole.Assistant, spoken, append: false));
                break;

            case "response.function_call_arguments.done":
                // Off the receive loop: a slow tool must not hold up the answer's audio or the user's interruption.
                var (callId, name, arguments) = (Text(e, "call_id"), Text(e, "name"), Text(e, "arguments"));
                _ = Task.Run(() => CallToolAsync(callId, name, arguments, cancellationToken), cancellationToken);
                break;

            case "response.done":
                _responseActive = false;
                Interlocked.CompareExchange(ref _goodbye, FarewellDone, FarewellUnderWay);
                break;

            case "error":
                // Not fatal: a truncate of an answer that already ended, or a cancel with no response, is reported here.
                var error = e.TryGetProperty("error", out var details) ? details : e;
                _logger.LogWarning("Spine.Voice: the provider reported {Code}: {Message}", Text(error, "code") ?? Text(error, "type"), Text(error, "message"));
                break;
        }

        Evaluate();
    }

    /// <summary>Cuts the answer off: the engine flushes, the model hears how much was played, and a running response stops.</summary>
    private void BargeIn()
    {
        // Talking over the farewell (or the Stop button) keeps the conversation going.
        Interlocked.Exchange(ref _goodbye, NoGoodbye);
        Tick();
        var position = _audio.Flush();
        if (position is { } played)
        {
            Send(new JsonObject
            {
                ["type"] = "conversation.item.truncate",
                ["item_id"] = played.ItemId,
                ["content_index"] = 0,
                ["audio_end_ms"] = (int)played.Played.TotalMilliseconds,
            });
            _dispatch(() => MarkInterrupted(played.ItemId));
        }
        if (_responseActive)
            Send(new JsonObject { ["type"] = "response.cancel" });

        if (position is not null || _responseActive)
            Interlocked.Exchange(ref _interruptedUntil, (_time.GetUtcNow() + InterruptedFor).UtcTicks);
        _responseActive = false;
        _answerAudio = false;
        Evaluate();
    }

    private async Task CallToolAsync(string? callId, string? name, string? arguments, CancellationToken cancellationToken)
    {
        if (callId is null || name is null)
            return;

        var call = new FunctionCallContent(callId, name, ParseArguments(arguments));
        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Assistant, [call])));

        object? result;
        var goodbye = name == EndConversationTool && EndsOnGoodbye;
        // A model often says goodbye in the same response that hangs up. That was the farewell: asking for another one
        // made it say goodbye twice.
        var saidGoodbye = goodbye && _answerAudio;
        if (saidGoodbye)
            result = "The call ends once your goodbye has played. Say nothing more.";
        else if (goodbye)
            result = "The call ends after your goodbye. Say a short goodbye now.";
        else
        {
            try
            {
                result = SideOf(name) switch
                {
                    SpineAIFunctionSide.Device => await _options.Functions[name]!.InvokeAsync(call.Arguments, await DeviceContextAsync(cancellationToken)),
                    SpineAIFunctionSide.Server => await _tokens.CallFunctionAsync(_token!.Feature, name, callId, arguments, cancellationToken),
                    _ => Unknown(name),
                };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The model hears that the function failed, so it can say so, rather than waiting for an answer that never comes.
                _logger.LogWarning(e, "Spine.Voice: the function '{Function}' failed.", name);
                result = SpineAIFunctionResult.Error($"The function '{name}' failed.");
            }
        }

        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, result)])));
        Send(new JsonObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = new JsonObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = callId,
                ["output"] = result as string ?? JsonSerializer.Serialize(result, Compact.GetTypeInfo(result?.GetType() ?? typeof(object))),
            },
        });
        if (saidGoodbye)
        {
            // The farewell is this response: done when it reports done, or now when it already has.
            Interlocked.Exchange(ref _goodbye, FarewellUnderWay);
            if (!_responseActive)
                Interlocked.CompareExchange(ref _goodbye, FarewellDone, FarewellUnderWay);
            return;
        }

        // The response that called the tool may still report done after this; only the next one is the farewell.
        if (goodbye)
            Interlocked.Exchange(ref _goodbye, FarewellAsked);
        Send(new JsonObject { ["type"] = "response.create" });
    }

    private static Dictionary<string, object?> ParseArguments(string? arguments)
    {
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(arguments))
            return parsed;
        try
        {
            using var json = JsonDocument.Parse(arguments);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in json.RootElement.EnumerateObject())
                    parsed[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
        }
        return parsed;
    }

    public void Prefer(string function, SpineAIFunctionSide side)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        lock (_preferred)
            _preferred[function] = side;
        if (_connected)
            Send(ToolsUpdate());
    }

    /// <summary>
    /// Where a call to <paramref name="name"/> runs: the app's own function, or the server's from the token. A function both
    /// sides have runs where the session's <see cref="Prefer"/> says, else where the app's declaration prefers.
    /// </summary>
    private SpineAIFunctionSide? SideOf(string name)
    {
        var device = _options.Functions[name];
        var server = _token?.Tools.FirstOrDefault(t => t.Name == name && t.OnServer);

        if (device is null) return server is null ? null : SpineAIFunctionSide.Server;
        if (server is null) return SpineAIFunctionSide.Device;

        lock (_preferred)
            return SpineAIFunctionOrder.SideOf(device, _preferred);
    }

    private object Unknown(string name)
    {
        _logger.LogWarning("Spine.Voice: the model called '{Function}', which neither the app nor the server has.", name);
        return SpineAIFunctionResult.Error($"There is no function named '{name}'.", "notFound");
    }

    private async Task<SpineAIFunctionContext> DeviceContextAsync(CancellationToken cancellationToken)
    {
        var caller = _services?.GetService(typeof(Plugin.Maui.Spine.Authentication.ISignedInSession)) is Plugin.Maui.Spine.Authentication.ISignedInSession { UserId: { Length: > 0 } user }
            ? AICaller.SignedIn(user)
            : await _tokens.InstallationIdAsync(cancellationToken) is { Length: > 0 } installation ? AICaller.Installation(installation) : AICaller.Anonymous(null);

        return new SpineAIFunctionContext(caller, SpineAIFunctionSide.Device)
        {
            Feature = _token?.Feature,
            Services = _services ?? EmptyServices.Instance,
            CancellationToken = cancellationToken,
        };
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }

    private JsonObject ToolsUpdate()
    {
        var list = new JsonArray();
        if (EndsOnGoodbye)
        {
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = EndConversationTool,
                ["description"] = "Hangs up. Call it when the user says goodbye, thanks you and is done, or otherwise wants to end the conversation; then say a short goodbye in their language. Do not call it for a pause or a thank-you in the middle of a conversation.",
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            });
        }
        // The server's functions from the token, then the app's own: one declaration per name, from the side that runs it.
        var names = (_token?.Tools.Where(t => t.OnServer || _options.Functions[t.Name] is not null).Select(t => t.Name) ?? [])
            .Concat(_options.Functions.Select(f => f.Name)).Distinct(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var (description, schema) = SideOf(name) == SpineAIFunctionSide.Device && _options.Functions[name] is { } device
                ? (device.Description, device.JsonSchema)
                : _token!.Tools.First(t => t.Name == name) is var server ? (server.Description, server.Parameters) : default;

            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = schema.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(schema.GetRawText())
                    : new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            });
        }
        return new JsonObject
        {
            ["type"] = "session.update",
            ["session"] = new JsonObject { ["type"] = "realtime", ["tools"] = list, ["tool_choice"] = "auto" },
        };
    }

    private void Tick()
    {
        KeyValuePair<string, string>[] pending;
        lock (_pendingGate)
        {
            pending = [.. _pendingText];
            _pendingText.Clear();
        }
        if (pending.Length > 0)
        {
            _dispatch(() =>
            {
                foreach (var (item, words) in pending)
                    ReplaceText(item, ChatRole.Assistant, words, append: true);
            });
        }
        Evaluate();

        // The farewell has been heard to its end, its last word out of the speaker too: hang up.
        if (_audio.IsPlaying || _responseActive)
            Interlocked.Exchange(ref _quietSince, 0);
        else if (Interlocked.Read(ref _quietSince) == 0)
            Interlocked.Exchange(ref _quietSince, _time.GetUtcNow().UtcTicks);
        if (Volatile.Read(ref _goodbye) == FarewellDone && Interlocked.Read(ref _quietSince) is var quiet and > 0
            && _time.GetUtcNow().UtcTicks - quiet >= FarewellTail.Ticks
            && Interlocked.CompareExchange(ref _goodbye, NoGoodbye, FarewellDone) == FarewellDone)
            _ = EndCoreAsync(VoiceSessionEnd.Goodbye, null);
    }

    private void Evaluate()
    {
        VoiceState next;
        if (_connecting)
            next = VoiceState.Connecting;
        else if (!_connected)
            next = VoiceState.Idle;
        else if (_audio.IsMuted || _audio.State == VoiceState.Muted)
            next = VoiceState.Muted;
        else if (_time.GetUtcNow().UtcTicks < Interlocked.Read(ref _interruptedUntil))
            next = VoiceState.Interrupted;
        else if (_audio.IsPlaying || _responseActive && _answerAudio)
            next = VoiceState.Speaking;
        else if (_responseActive)
            next = VoiceState.Thinking;
        else if (_audio.State == VoiceState.Connecting)
            next = VoiceState.Connecting;
        else
            next = VoiceState.Listening;

        lock (_gate)
        {
            if (next == _state)
                return;
            _state = next;
        }
        _dispatch(() => StateChanged?.Invoke(next));
    }

    private async Task EndCoreAsync(VoiceSessionEnd reason, Exception? error)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1)
            return;

        var wasConnected = _connected;
        _connected = false;
        if (error is not null)
            _logger.LogError(error, "Spine.Voice: the session failed.");

        await TearDownAsync();
        Evaluate();
        if (wasConnected || error is not null)
            _dispatch(() => Ended?.Invoke(reason, error));
    }

    private async Task TearDownAsync()
    {
        _audio.InputAvailable -= OnMicrophone;
        _ticker?.Dispose();
        _ticker = null;
        _limit?.Dispose();
        _limit = null;
        Interlocked.Exchange(ref _endsAt, 0);
        _outbox?.Writer.TryComplete();
        _run?.Cancel();

        if (_socket is { } socket)
        {
            try
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(close.Token);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Spine.Voice: closing the socket failed.");
            }
            await socket.DisposeAsync();
            _socket = null;
        }

        await _audio.StopAsync();
        SpokenVoice = null;
        _responseActive = false;
        _answerAudio = false;
        lock (_micGate)
            _mic.SetLength(0);
    }

    private void AddMessage(string itemId, ChatMessage message)
    {
        _byItem[itemId] = message;
        _transcript.Add(message);
    }

    private void RemoveMessage(string itemId)
    {
        if (_byItem.Remove(itemId, out var message))
            _transcript.Remove(message);
    }

    /// <summary>Replaces the item's message with one carrying the new text, so a bound list sees the change.</summary>
    private void ReplaceText(string itemId, ChatRole role, string text, bool append)
    {
        if (!_byItem.TryGetValue(itemId, out var old))
        {
            AddMessage(itemId, new ChatMessage(role, text));
            return;
        }

        var replacement = new ChatMessage(role, append ? old.Text + text : text) { AdditionalProperties = old.AdditionalProperties };
        var index = _transcript.IndexOf(old);
        _byItem[itemId] = replacement;
        if (index >= 0)
            _transcript[index] = replacement;
        else
            _transcript.Add(replacement);
    }

    private void MarkInterrupted(string itemId)
    {
        if (_byItem.TryGetValue(itemId, out var message))
        {
            message.AdditionalProperties ??= [];
            message.AdditionalProperties[InterruptedKey] = true;
            // A replacement so a bound list redraws the row.
            ReplaceText(itemId, message.Role, message.Text, append: false);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
