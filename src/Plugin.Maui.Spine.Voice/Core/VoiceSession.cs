using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// A voice session speaking OpenAI's realtime events over a WebSocket (protocol <c>openai</c>, which Azure OpenAI speaks
/// too), with the audio through <see cref="IVoiceAudio"/>.
/// </summary>
internal sealed class VoiceSession : IVoiceSession
{
    public const string InterruptedKey = "spine.interrupted";

    /// <summary>The provider's rate both ways.</summary>
    private const int Rate = 24_000;

    /// <summary>Microphone audio is sent in frames of about 100 ms.</summary>
    private const int FrameBytes = Rate / 10 * 2;

    private static readonly TimeSpan InterruptedFor = TimeSpan.FromMilliseconds(600);

    private readonly IVoiceAudio _audio;
    private readonly SpineVoiceOptions _options;
    private readonly VoiceTokenClient _tokens;
    private readonly Func<IVoiceSocket> _socketFactory;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ObservableCollection<ChatMessage> _transcript = [];
    private readonly Dictionary<string, ChatMessage> _byItem = new(StringComparer.Ordinal);
    private readonly object _micGate = new();
    private readonly MemoryStream _mic = new();
    private readonly object _gate = new();

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
        Action<Action> dispatch, TimeProvider? time = null, ILogger? logger = null)
    {
        _audio = audio;
        _options = options;
        _tokens = tokens;
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

    public async Task ConnectAsync(string feature, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (_connected || _connecting)
            throw new InvalidOperationException("The voice session is already connected; end it before connecting again.");

        _connecting = true;
        _ended = 0;
        Evaluate();
        try
        {
            var token = await _tokens.RequestAsync(feature, cancellationToken);
            if (token.Protocol != "openai")
                throw new NotSupportedException($"The voice feature '{feature}' speaks '{token.Protocol}', which Spine.Voice cannot speak yet; only 'openai' (OpenAI and Azure OpenAI).");

            var socket = _socketFactory();
            _socket = socket;
            await socket.ConnectAsync(token.Url, token.Token, cancellationToken);

            await _audio.StartAsync(new VoiceAudioOptions { InputRate = Rate, OutputRate = Rate, Duplex = _audio.Duplex }, cancellationToken);
            _audio.InputAvailable += OnMicrophone;

            _run = new CancellationTokenSource();
            _outbox = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
            if (_options.ClientTools.Count > 0)
                Send(ToolsUpdate(_options.ClientTools));

            _ = SendLoopAsync(socket, _outbox.Reader, _run.Token);
            _ = ReceiveLoopAsync(socket, _run.Token);

            if (token.MaxMinutes is { } minutes and > 0)
            {
                var limit = TimeSpan.FromMinutes(minutes);
                Interlocked.Exchange(ref _endsAt, (_time.GetUtcNow() + limit).UtcTicks);
                _limit = _time.CreateTimer(_ => _ = EndCoreAsync(VoiceSessionEnd.TimeLimit, null), null, limit, Timeout.InfiniteTimeSpan);
            }
            _ticker = _time.CreateTimer(_ => Evaluate(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

            _connected = true;
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
                using var json = JsonDocument.Parse(message);
                await HandleAsync(json.RootElement, cancellationToken);
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

    private async Task HandleAsync(JsonElement e, CancellationToken cancellationToken)
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
                _dispatch(() => ReplaceText(textItem, ChatRole.Assistant, words, append: true));
                break;

            case "response.output_audio_transcript.done" or "response.audio_transcript.done"
                when Text(e, "item_id") is { } spokenItem && Text(e, "transcript") is { } spoken:
                _dispatch(() => ReplaceText(spokenItem, ChatRole.Assistant, spoken, append: false));
                break;

            case "response.function_call_arguments.done":
                await CallToolAsync(Text(e, "call_id"), Text(e, "name"), Text(e, "arguments"), cancellationToken);
                break;

            case "response.done":
                _responseActive = false;
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
        if (_options.ClientTools.FirstOrDefault(t => t.Name == name) is not { } tool)
        {
            result = new { error = $"The app has no tool named '{name}'." };
            _logger.LogWarning("Spine.Voice: the model called '{Tool}', which the app does not have.", name);
        }
        else
        {
            try
            {
                result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The model hears that the tool failed, so it can say so, rather than waiting for an answer that never comes.
                _logger.LogWarning(e, "Spine.Voice: the tool '{Tool}' failed.", name);
                result = new { error = $"The tool '{name}' failed." };
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
                ["output"] = result as string ?? JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions),
            },
        });
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

    private static JsonObject ToolsUpdate(IEnumerable<AIFunction> tools)
    {
        var list = new JsonArray();
        foreach (var tool in tools)
        {
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText()),
            });
        }
        return new JsonObject
        {
            ["type"] = "session.update",
            ["session"] = new JsonObject { ["type"] = "realtime", ["tools"] = list, ["tool_choice"] = "auto" },
        };
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
