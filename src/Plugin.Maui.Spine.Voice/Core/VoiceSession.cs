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
/// too), or GPT-Live's through the server's relay (<c>openai-live</c>), with the audio through <see cref="IVoiceAudio"/>.
/// </summary>
/// <remarks>
/// GPT-Live is full duplex and says less than Realtime: no event when the user starts to speak, no item ids, no end of an
/// answer, and no truncate or cancel. It streams its audio at real time, silence included, so the model yields simply by
/// sending silence. The session plays what is not silence, ends an answer's audio when none has come for a moment, cuts
/// it off itself if the model yielded with more than a moment still queued, and groups the transcript by speaker and pauses.
/// </remarks>
internal sealed class VoiceSession : IVoiceSession, IVoiceAudioOwner
{
    public const string InterruptedKey = "spine.interrupted";

    /// <summary>
    /// Marks the user's turn while it is being transcribed: ChatView (<c>ChatView.PendingKey</c>, the same string) shows
    /// it as a shimmer until the words arrive.
    /// </summary>
    public const string PendingKey = "spine.pending";

    /// <summary>The tool <see cref="EndsOnGoodbye"/> gives the model: Spine's built-in <see cref="SpineAIFunctions.EndConversation"/>.</summary>
    public const string EndConversationTool = SpineAIFunctions.EndConversationName;

    /// <summary>What the model is told <see cref="EndConversationTool"/> is for: <see cref="SpineAIFunctions.EndConversationDescription"/>.</summary>
    public const string EndConversationDescription = SpineAIFunctions.EndConversationDescription;

    private readonly FarewellTracker _farewell;

    /// <summary>The provider's rate both ways.</summary>
    // Microsoft.Extensions.AI's options indent, which the model pays for in tokens.
    private static readonly JsonSerializerOptions Compact = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private const int Rate = 24_000;

    /// <summary>Microphone audio is sent in frames of about 100 ms.</summary>
    private const int FrameBytes = Rate / 10 * 2;

    private static readonly TimeSpan InterruptedFor = TimeSpan.FromMilliseconds(600);

    private readonly IVoiceAudio _audio;

    /// <summary>The engine the session plays through; an avatar's driver reads its playback position for the mouth's clock.</summary>
    public IVoiceAudio Audio => _audio;
    private readonly SpineVoiceOptions _options;
    private readonly VoiceTokenClient _tokens;
    private readonly IServiceProvider? _services;
    private readonly Dictionary<string, SpineAIFunctionSide> _preferred = new(StringComparer.Ordinal);
    private VoiceSessionToken? _token;
    private readonly Func<IVoiceSocket> _socketFactory;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ObservableCollection<ChatMessage> _own = [];
    private IList<ChatMessage> _transcript;

    // The model's context is limited; the latest turns of a long typed conversation are what it needs.
    private const int MaxHistory = 40;
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

    // GPT-Live through the relay.
    private const string LiveProtocol = "openai-live";
    // OpenAI's Realtime events through the server's relay (Realtime:Relay): the same session as 'openai', reached like Live.
    private const string RelayProtocol = "openai-relay";
    // A pause longer than this between two pieces of one speaker's words starts a new message.
    private const double LiveTurnGapMs = 1500;
    // No answer audio for this long ends its item, so it plays out and the next audio starts a new one.
    private static readonly TimeSpan LiveAudioGap = TimeSpan.FromMilliseconds(400);
    // GPT-Live's silence is zeros or a few steps of noise (measured 2026-10-20); speech, fades included, peaks above this.
    private const int LiveSilencePeak = 32;
    // The model stopped sending while the user speaks and this much is still queued: it yielded, so the rest is dropped.
    private static readonly TimeSpan LiveYieldAhead = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LiveUserSpeaking = TimeSpan.FromSeconds(1);
    // How long a farewell is waited for when the voice may already have said it: its answer to a result comes within this.
    private static readonly TimeSpan LiveMaybeSaid = TimeSpan.FromSeconds(3);
    private volatile bool _live;
    // A parked link (SpineVoiceOptions.KeepWarm, WarmAsync): the provider's socket stays open and silent between conversations,
    // so the next one starts at once. An open Realtime session that sends no audio costs no tokens; Live bills by the second.
    private volatile bool _warm;
    private string? _warmFeature;
    // What the provider reported the session used (response.done), for the server to settle a direct session's reservation.
    private VoiceSessionUsage _usage = VoiceSessionUsage.None;
    private long _connectedAt;
    private readonly object _liveGate = new();
    private int _liveCount;
    private string? _liveItem;
    private string? _liveLastItem;
    private TimeSpan _liveQueued;
    private long _liveAudioAt;
    private bool _liveDropping;
    private string? _liveAnswer;
    private double _liveAnswerEnd = double.NegativeInfinity;
    private string? _liveUser;
    private double _liveUserEnd = double.NegativeInfinity;
    private long _liveUserAt;
    private long _liveMicAt;

    public VoiceSession(
        IVoiceAudio audio, SpineVoiceOptions options, VoiceTokenClient tokens, Func<IVoiceSocket> socketFactory,
        Action<Action> dispatch, TimeProvider? time = null, ILogger? logger = null, IServiceProvider? services = null)
    {
        _audio = audio;
        _options = options;
        _tokens = tokens;
        _services = services;
        _socketFactory = socketFactory;
        _transcript = _own;
        _dispatch = dispatch;
        _time = time ?? TimeProvider.System;
        _farewell = new FarewellTracker(_time);
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
            // GPT-Live listens to a continuous stream: it is told, rather than left to guess from silence.
            if (_live && _connected)
                Send(new JsonObject { ["type"] = value ? "session.input_audio.mute" : "session.input_audio.unmute" });
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

    public double? MaxMinutes { get; set; }

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
                _farewell.Cancel();
        }
    }

    public Task ConnectAsync(string feature, CancellationToken cancellationToken = default) =>
        ConnectCoreAsync(feature, null, cancellationToken);

    public Task ConnectAsync(string feature, IList<ChatMessage> conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return ConnectCoreAsync(feature, conversation, cancellationToken);
    }

    private async Task ConnectCoreAsync(string feature, IList<ChatMessage>? conversation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (_connected || _connecting)
            throw new InvalidOperationException("The voice session is already connected; end it before connecting again.");

        _connecting = true;
        _ended = 0;
        // One conversation id per call, for the app's functions and, in the requests' headers, the server's.
        _tokens.ConversationId = Guid.NewGuid().ToString("N");
        // Taken now, on the caller's thread: the conversation is the app's list, and it changes on the main thread.
        var history = conversation is null ? [] : History(conversation).ToList();
        _dispatch(() =>
        {
            _byItem.Clear();
            // A new conversation starts with an empty transcript; one in the middle of a chat goes on in the chat's list.
            _transcript = conversation ?? _own;
            if (conversation is null)
                _own.Clear();
        });
        lock (_pendingGate)
            _pendingText.Clear();
        Evaluate();
        try
        {
            // A link parked for this feature goes on; one parked for another is closed first.
            var reuse = _warm && _warmFeature == feature && _socket is not null && _run is { IsCancellationRequested: false };
            if (_warm && !reuse)
                await CloseLinkAsync();
            if (!reuse)
                await OpenLinkAsync(feature, history, forWarm: false, cancellationToken);
            var token = _token!;

            await _audio.StartAsync(new VoiceAudioOptions { InputRate = Rate, OutputRate = Rate, Duplex = _audio.Duplex }, cancellationToken);
            _audio.InputAvailable += OnMicrophone;
            _farewell.Cancel();
            if (reuse)
            {
                _warm = false;
                // Nothing was sent while parked, but the provider's buffer starts clean; a conversation given now joins the one the model remembers.
                Send(new JsonObject { ["type"] = "input_audio_buffer.clear" });
                foreach (var item in history)
                    Send(item);
            }

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
            if (!reuse)
                StartLoops();
            _logger.LogInformation("Spine.Voice: connected to '{Feature}' over {Protocol}{Reused}.", token.Feature, token.Protocol, reuse ? " (the warm link)" : "");
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

    public async Task WarmAsync(string feature, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (_connected || _connecting) return;
        if (_warm && _warmFeature == feature && _socket is not null && _run is { IsCancellationRequested: false }) return;

        _connecting = true;
        try
        {
            if (_warm)
                await CloseLinkAsync();
            await OpenLinkAsync(feature, [], forWarm: true, cancellationToken);
            _warm = true;
            _warmFeature = feature;
            StartLoops();
            _logger.LogInformation("Spine.Voice: '{Feature}' is warm over {Protocol}.", feature, _token!.Protocol);
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

    public bool IsWarm => _warm;

    /// <summary>
    /// The link a conversation and a parked session share: the token, the provider's socket, the outbox and, for the
    /// provider's protocol, the conversation so far and the functions the app runs. The loops start separately.
    /// </summary>
    private async Task OpenLinkAsync(string feature, List<JsonObject> history, bool forWarm, CancellationToken cancellationToken)
    {
        var token = await _tokens.RequestAsync(feature, cancellationToken, Voice, MaxMinutes);
        if (token.Protocol is not ("openai" or LiveProtocol or RelayProtocol))
            throw new NotSupportedException(
                $"The voice feature '{feature}' speaks '{token.Protocol}', which Spine.Voice cannot speak yet; only 'openai' (OpenAI and Azure OpenAI, directly or through the server's relay as '{RelayProtocol}') and '{LiveProtocol}' (GPT-Live).");
        if (forWarm && token.Protocol == LiveProtocol)
            throw new InvalidOperationException($"The voice feature '{feature}' is GPT-Live, which bills by the second: it cannot be kept warm.");
        _token = token;
        SpokenVoice = token.Voice;
        _live = token.Protocol == LiveProtocol;

        var socket = _socketFactory();
        _socket = socket;
        await socket.ConnectAsync(token.Url, await _tokens.SocketHeadersAsync(token, cancellationToken), cancellationToken);

        _run = new CancellationTokenSource();
        _outbox = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
            _usage = VoiceSessionUsage.None;
        _connectedAt = _time.GetTimestamp();
        if (_live)
        {
            // The relay writes the session; the app gives it the conversation so far and the functions it runs itself.
            Send(new JsonObject
            {
                ["type"] = "session.start",
                ["session"] = new JsonObject { ["input"] = LiveHistory(history), ["tools"] = LiveTools() },
            });
        }
        else
        {
            foreach (var item in history)
                Send(item);
            if (_options.Functions.Count > 0 || token.Tools.Count > 0 || EndsOnGoodbye)
                Send(ToolsUpdate());
        }
    }

    private void StartLoops()
    {
        if (_socket is not { } socket || _outbox is not { } outbox || _run is not { } run) return;
        _ = SendLoopAsync(socket, outbox.Reader, run.Token);
        _ = ReceiveLoopAsync(socket, run.Token);
    }

    /// <summary>Closes a parked link: the socket, its loops and the usage report.</summary>
    private async Task CloseLinkAsync()
    {
        _warm = false;
        _warmFeature = null;
        await TearDownAsync();
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
        Send(new JsonObject { ["type"] = _live ? "session.input_audio.unmute" : "input_audio_buffer.clear" });
    }

    public void EndTurn()
    {
        if (!_connected)
            return;
        FlushMicrophone();
        if (_live)
        {
            // GPT-Live decides itself when to answer; muted, it knows the turn is over.
            Send(new JsonObject { ["type"] = "session.input_audio.mute" });
            return;
        }
        Send(new JsonObject { ["type"] = "input_audio_buffer.commit" });
        Send(new JsonObject { ["type"] = "response.create" });
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        NewTurn();
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!_connected)
            throw new InvalidOperationException("Connect the voice session before sending text.");

        BargeIn();
        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.User, text)));
        if (_live)
        {
            // The relay frames it for the voice as the user's own words.
            Send(new JsonObject { ["type"] = "spine.user_text", ["text"] = text });
            return Task.CompletedTask;
        }
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
        if (_warm)
            await CloseLinkAsync();
        _mic.Dispose();
    }

    private void OnMicrophone(ReadOnlySpan<byte> pcm16)
    {
        // The farewell is not interrupted: once the model has hung up, the user saying goodbye back is not sent, so it
        // neither cuts the farewell off nor makes the model answer it.
        if (_farewell.IsActive)
            return;
        Interlocked.Exchange(ref _liveMicAt, _time.GetUtcNow().UtcTicks);
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
                    byte[] audio => _live
                        ? $$"""{"type":"session.input_audio.append","audio":"{{Convert.ToBase64String(audio)}}"}"""
                        : $$"""{"type":"input_audio_buffer.append","audio":"{{Convert.ToBase64String(audio)}}"}""",
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
            if (_connected)
                await EndCoreAsync(VoiceSessionEnd.Failed, e);
            else
                await LinkLostAsync(e);
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
            if (_connected)
                await EndCoreAsync(VoiceSessionEnd.Provider, null);
            else
                await LinkLostAsync(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            if (_connected)
                await EndCoreAsync(VoiceSessionEnd.Failed, e);
            else
                await LinkLostAsync(e);
        }
    }

    private void HandleEvent(JsonElement e, CancellationToken cancellationToken)
    {
        if (_live)
        {
            HandleLiveEvent(e, cancellationToken);
            Evaluate();
            return;
        }

        var type = Text(e, "type");
        switch (type)
        {
            case "input_audio_buffer.speech_started":
                // The user talks over the answer: cut it off where it was heard. Not the farewell, whose speech was
                // already on its way when the microphone stopped.
                if ((_audio.IsPlaying || _responseActive) && !_farewell.IsActive)
                    BargeIn();
                break;

            case "input_audio_buffer.committed" when Text(e, "item_id") is { } userItem:
                NewTurn();
                _dispatch(() => AddMessage(userItem, new ChatMessage(ChatRole.User, "") { AdditionalProperties = new() { [PendingKey] = true } }));
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
                _farewell.AnswerStarted();
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
                var (callId, name, arguments, round) = (Text(e, "call_id"), Text(e, "name"), Text(e, "arguments"), Text(e, "response_id"));
                _ = Task.Run(() => CallToolAsync(callId, name, arguments, cancellationToken, round), cancellationToken);
                break;

            case "response.done":
                _responseActive = false;
                _farewell.AnswerDone();
                if (e.TryGetProperty("response", out var done) && done.TryGetProperty("usage", out var usage))
                {
                    lock (_gate)
                        _usage = _usage.Add(Count(usage, "input_tokens"), Count(usage, "input_token_details", "cached_tokens"), Count(usage, "output_tokens"),
                            Count(usage, "input_token_details", "audio_tokens"), Count(usage, "output_token_details", "audio_tokens"));
                }
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
        // The Stop button keeps the conversation going; talking over the farewell cannot reach here (see OnMicrophone).
        _farewell.Cancel();
        Tick();
        if (_live)
        {
            LiveBargeIn();
            return;
        }
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

    // The rounds of calls in the user's current turn: a response (Realtime) or a delegation (GPT-Live) is one round.
    private readonly HashSet<string> _rounds = new(StringComparer.Ordinal);

    private void NewTurn()
    {
        lock (_rounds)
            _rounds.Clear();
    }

    /// <summary>Whether <paramref name="round"/> is past the feature's <c>MaxToolRounds</c> for this turn.</summary>
    private bool TooManyRounds(string? round)
    {
        lock (_rounds)
            return round is not null && _rounds.Add(round) && _rounds.Count > (_token?.MaxToolRounds ?? 4);
    }

    private async Task CallToolAsync(string? callId, string? name, string? arguments, CancellationToken cancellationToken, string? round = null)
    {
        if (callId is null || name is null)
            return;

        var readable = SpineAIFunctionResult.TryParseArguments(arguments, out var parsed, out var invalid);
        var call = new FunctionCallContent(callId, name, parsed);
        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Assistant, [call])));

        object? result;
        var goodbye = name == EndConversationTool && EndsOnGoodbye;
        // A model often says goodbye in the same response that hangs up. That was the farewell: asking for another one
        // made it say goodbye twice.
        // GPT-Live has no responses, and its voice talks while the backend works: what it said may be the goodbye or only
        // "one moment", so the voice decides.
        var saidGoodbye = goodbye && !_live && _answerAudio;
        if (saidGoodbye)
            result = SpineAIFunctions.GoodbyeSaid;
        else if (goodbye)
            result = _live ? SpineAIFunctions.GoodbyeUnlessSaid : SpineAIFunctions.SayGoodbye;
        else if (!readable)
            result = invalid;
        else if (TooManyRounds(round))
            result = SpineAIFunctionResult.Error($"This turn has used its {_token?.MaxToolRounds} rounds of function calls. Answer with what you have.", "refused");
        else
        {
            try
            {
                result = SideOf(name) switch
                {
                    SpineAIFunctionSide.Device => await _options.Functions[name]!.InvokeAsync(call.Arguments, await DeviceContextAsync(callId, cancellationToken)),
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
        // A string is the model's text as it is; a function's JSON string result is unwrapped rather than sent in quotes.
        var output = result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            _ => JsonSerializer.Serialize(result, Compact.GetTypeInfo(result?.GetType() ?? typeof(object))),
        };
        if (_live)
        {
            // The backend goes on once the relay has every result of its response; the voice then says the farewell.
            Send(new JsonObject
            {
                ["type"] = "response.item.create",
                ["item"] = new JsonObject { ["type"] = "function_call_output", ["call_id"] = callId, ["output"] = output },
            });
            // A voice that already spoke since the user's last words may have said its goodbye and say nothing more.
            if (goodbye)
                _farewell.Start(Interlocked.Read(ref _liveAudioAt) > Interlocked.Read(ref _liveUserAt) ? LiveMaybeSaid : null);
            return;
        }
        Send(new JsonObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = new JsonObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = callId,
                ["output"] = output,
            },
        });
        // What the microphone already sent of a goodbye back is dropped, so the provider does not answer it.
        if (goodbye)
            Send(new JsonObject { ["type"] = "input_audio_buffer.clear" });
        if (saidGoodbye)
        {
            // The farewell is this response: done when it reports done, or now when it already has.
            _farewell.Start();
            if (!_responseActive)
                _farewell.AnswerDone();
            return;
        }

        // The response that called the tool may still report done after this; only the next one is the farewell.
        if (goodbye)
            _farewell.Ask();
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
        // Only what the feature lists: a function of the app's that the catalogue does not list for the device is no one's.
        var device = _token?.Tools.Any(t => t.Name == name && t.OnDevice) == true ? _options.Functions[name] : null;
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

    private Task<SpineAIFunctionContext> DeviceContextAsync(string? callId, CancellationToken cancellationToken) =>
        SpineAIFunctionContext.ForDeviceAsync(_services, _tokens.InstallationIdAsync, _token?.Feature, _tokens.ConversationId, callId, cancellationToken,
            _token?.MaxResultCharacters);


    private JsonObject ToolsUpdate()
    {
        if (_live)
            return new JsonObject { ["type"] = "session.update", ["session"] = new JsonObject { ["tools"] = LiveTools() } };

        var list = new JsonArray();
        if (EndsOnGoodbye)
        {
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = EndConversationTool,
                ["description"] = EndConversationDescription,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            });
        }
        // The feature's catalogue from the token, and nothing else: a server function, or a device function the app has.
        // One declaration per name, from the side that runs it.
        var names = (_token?.Tools.Where(t => t.OnServer || t.OnDevice && _options.Functions[t.Name] is not null).Select(t => t.Name) ?? [])
            .Distinct(StringComparer.Ordinal);
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
        LiveTick();
        Evaluate();

        // The farewell has been heard to its end, its last word out of the speaker too: hang up.
        if (_farewell.ShouldHangUp(_audio.IsPlaying || _responseActive))
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

        // The conversation ends; the link stays when the app keeps it warm and nothing is wrong with it.
        var park = _options.KeepWarm && wasConnected && !_live && reason is not (VoiceSessionEnd.Failed or VoiceSessionEnd.Provider)
            && _token is not null && _socket is not null && _run is { IsCancellationRequested: false };
        if (park)
            await ParkAsync();
        else
            await TearDownAsync();
        Evaluate();
        // A turn still being transcribed will never get its words now.
        _dispatch(() =>
        {
            foreach (var (item, message) in _byItem.ToList())
            {
                if (message.AdditionalProperties?.ContainsKey(PendingKey) == true)
                    RemoveMessage(item);
            }

            // The app's conversation is the app's again: the session neither shows nor holds on to it after the call.
            _byItem.Clear();
            _transcript = _own;
        });
        if (wasConnected || error is not null)
            _dispatch(() => Ended?.Invoke(reason, error));
    }

    /// <summary>The session's usage goes to the server when the token asked for it (a direct session): the server settles the reservation with it.</summary>
    private void ReportUsage()
    {
        VoiceSessionUsage usage;
        lock (_gate)
        {
            usage = _usage;
            _usage = VoiceSessionUsage.None;
        }
        if (_token is not { UsageTicket: { Length: > 0 } } token || usage.Responses == 0) return;
        var seconds = _connectedAt == 0 ? 0 : _time.GetElapsedTime(_connectedAt).TotalSeconds;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                if (!await _tokens.ReportUsageAsync(token, usage, seconds, timeout.Token))
                    _logger.LogWarning("Spine.Voice: the server did not take the session's usage report for '{Feature}'.", token.Feature);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Spine.Voice: the session's usage report for '{Feature}' failed.", token.Feature);
            }
        });
    }

    private static long Count(JsonElement usage, string name, string? inner = null)
    {
        if (!usage.TryGetProperty(name, out var value)) return 0;
        if (inner is not null)
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(inner, out var nested) && nested.TryGetInt64(out var n) ? n : 0;
        return value.TryGetInt64(out var count) ? count : 0;
    }

    /// <summary>The conversation's part of the session goes; the socket and its loops stay open and silent for the next one.</summary>
    private async Task ParkAsync()
    {
        _audio.InputAvailable -= OnMicrophone;
        _ticker?.Dispose();
        _ticker = null;
        _limit?.Dispose();
        _limit = null;
        Interlocked.Exchange(ref _endsAt, 0);
        await _audio.StopAsync();
        _responseActive = false;
        _answerAudio = false;
        lock (_micGate)
            _mic.SetLength(0);
        _warm = true;
        _warmFeature = _token!.Feature;
        _logger.LogInformation("Spine.Voice: '{Feature}' stays warm.", _warmFeature);
    }

    /// <summary>A parked link the provider closed or that failed: gone quietly, the next connect opens a new one.</summary>
    private async Task LinkLostAsync(Exception? error)
    {
        if (!_warm) return;
        if (error is not null)
            _logger.LogWarning(error, "Spine.Voice: the warm link to '{Feature}' failed.", _warmFeature);
        else
            _logger.LogInformation("Spine.Voice: the provider closed the warm link to '{Feature}'.", _warmFeature);
        await CloseLinkAsync();
    }

    private async Task TearDownAsync()
    {
        ReportUsage();
        _warm = false;
        _warmFeature = null;
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
        lock (_liveGate)
        {
            _liveItem = _liveAnswer = _liveUser = null;
            _liveLastItem = null;
            _liveQueued = TimeSpan.Zero;
            _liveDropping = false;
            _liveAnswerEnd = _liveUserEnd = double.NegativeInfinity;
            _liveAudioAt = _liveUserAt = 0;
        }
        lock (_micGate)
            _mic.SetLength(0);
    }

    private void HandleLiveEvent(JsonElement e, CancellationToken cancellationToken)
    {
        switch (Text(e, "type"))
        {
            case "session.output_audio.delta" when Text(e, "delta") is { } delta:
                var pcm = Convert.FromBase64String(delta);
                if (IsSilence(pcm))
                    break;
                string? item = null;
                lock (_liveGate)
                {
                    _liveAudioAt = _time.GetUtcNow().UtcTicks;
                    // What is left of an answer the user cut off is not played.
                    if (!_liveDropping)
                    {
                        if (_liveItem is null)
                        {
                            _liveItem = _liveLastItem = $"live_{++_liveCount}";
                            _liveQueued = TimeSpan.Zero;
                        }
                        item = _liveItem;
                        _liveQueued += TimeSpan.FromSeconds(pcm.Length / 2.0 / Rate);
                    }
                }
                if (item is not null)
                    _audio.Enqueue(item, pcm);
                break;

            case "session.output_transcript.delta":
                LiveWords(e, user: false);
                break;

            case "session.input_transcript.delta":
                NewTurn();
                LiveWords(e, user: true);
                break;

            case "session.delegation.created":
                // The backend works on something: the conversation shows it thinking until the work is done.
                _responseActive = true;
                break;

            case "response.event" when e.TryGetProperty("event", out var inner):
                switch (Text(inner, "type"))
                {
                    case "response.output_item.done" when inner.TryGetProperty("item", out var call) && Text(call, "type") == "function_call":
                        var (callId, name, arguments) = (Text(call, "call_id"), Text(call, "name"), Text(call, "arguments"));
                        if (name is null || callId is null)
                            break;
                        if (name != EndConversationTool && SideOf(name) == SpineAIFunctionSide.Server)
                        {
                            // The relay runs the server's own functions; the transcript shows the call, and its result when it comes.
                            _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, ParseArguments(arguments))])));
                            break;
                        }
                        var delegation = Text(e, "delegation_id");
                        _ = Task.Run(() => CallToolAsync(callId, name, arguments, cancellationToken, delegation), cancellationToken);
                        break;

                    case "response.completed" or "response.failed" or "response.incomplete":
                        _responseActive = false;
                        break;
                }
                break;

            case "spine.function.output" when Text(e, "call_id") is { } resultId:
                var result = Text(e, "output");
                _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(resultId, result)])));
                break;

            case "session.closed":
                _ = EndCoreAsync(Text(e, "spine_reason") == "time_limit" ? VoiceSessionEnd.TimeLimit : VoiceSessionEnd.Provider, null);
                break;

            case "error":
                var error = e.TryGetProperty("error", out var details) ? details : e;
                _logger.LogWarning("Spine.Voice: GPT-Live reported {Code}: {Message}", Text(error, "code") ?? Text(error, "type"), Text(error, "message"));
                break;
        }
    }

    private static bool IsSilence(ReadOnlySpan<byte> pcm16)
    {
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm16[..(pcm16.Length & ~1)]);
        foreach (var sample in samples)
        {
            if (sample > LiveSilencePeak || sample < -LiveSilencePeak)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A piece of what one side said. GPT-Live marks no turns, so a piece joins that speaker's latest message unless a
    /// pause came between; both sides can grow at once.
    /// </summary>
    private void LiveWords(JsonElement e, bool user)
    {
        if (Text(e, "delta") is not { Length: > 0 } words)
            return;
        var start = e.TryGetProperty("start_ms", out var s) && s.TryGetDouble(out var startMs) ? startMs : double.PositiveInfinity;
        var end = e.TryGetProperty("end_ms", out var f) && f.TryGetDouble(out var endMs) ? endMs : start;

        string id;
        var first = false;
        lock (_liveGate)
        {
            if (user)
            {
                if (_liveUser is null || start - _liveUserEnd > LiveTurnGapMs)
                    _liveUser = $"user_{++_liveCount}";
                id = _liveUser;
                _liveUserEnd = end;
                _liveUserAt = _time.GetUtcNow().UtcTicks;
            }
            else
            {
                if (_liveAnswer is null || start - _liveAnswerEnd > LiveTurnGapMs)
                {
                    _liveAnswer = $"answer_{++_liveCount}";
                    first = true;
                }
                id = _liveAnswer;
                _liveAnswerEnd = end;
            }
        }

        // A new message goes in at once, so the transcript keeps the order things were said in.
        if (user || first)
        {
            _dispatch(() => ReplaceText(id, user ? ChatRole.User : ChatRole.Assistant, words, append: true));
            return;
        }
        // Collected and shown on the next tick, as Realtime's answer is.
        lock (_pendingGate)
            _pendingText[id] = _pendingText.GetValueOrDefault(id) + words;
    }

    /// <summary>Cuts GPT-Live's answer off: there is nothing to tell the model, so the session drops what is queued and what still comes of it.</summary>
    private void LiveBargeIn()
    {
        var position = _audio.Flush();
        string? answer;
        lock (_liveGate)
        {
            if (_liveItem is not null)
                _liveDropping = true;
            _liveItem = null;
            _liveQueued = TimeSpan.Zero;
            answer = _liveAnswer;
            _liveAnswer = null;
        }

        if (position is not null)
        {
            if (answer is not null)
                _dispatch(() => MarkInterrupted(answer));
            Interlocked.Exchange(ref _interruptedUntil, (_time.GetUtcNow() + InterruptedFor).UtcTicks);
        }
        Evaluate();
    }

    /// <summary>
    /// GPT-Live's clock: ends an answer's audio once none has come for a moment, cuts it off when the model yielded to the
    /// user with much of it still queued, keeps the microphone's stream continuous, and lets a goodbye end the call.
    /// </summary>
    private void LiveTick()
    {
        if (!_live || !_connected)
            return;

        var now = _time.GetUtcNow().UtcTicks;
        string? ended = null;
        bool yielded;
        lock (_liveGate)
        {
            var quiet = now - _liveAudioAt >= LiveAudioGap.Ticks;
            if (quiet)
            {
                ended = _liveItem;
                _liveItem = null;
                _liveDropping = false;
            }

            yielded = quiet && _liveLastItem is { } last && _audio.IsPlaying && now - _liveUserAt < LiveUserSpeaking.Ticks
                && _liveQueued - _audio.Played(last) > LiveYieldAhead;
        }

        if (ended is not null)
            _audio.Complete(ended);
        if (yielded)
        {
            _logger.LogDebug("Spine.Voice: GPT-Live yielded to the user; the rest of its answer is dropped.");
            LiveBargeIn();
        }

        // GPT-Live listens to a continuous stream: silence goes up while the engine sends nothing (muted, or half duplex).
        if (now - Interlocked.Read(ref _liveMicAt) > TimeSpan.FromMilliseconds(150).Ticks)
            _outbox?.Writer.TryWrite(new byte[FrameBytes]);

        // The voice spoke after the backend's goodbye and has played it out (a farewell that never comes ends in the tracker).
        if (_farewell.IsUnderWay && Interlocked.Read(ref _liveAudioAt) >= _farewell.UnderWaySince && !_audio.IsPlaying)
            _farewell.AnswerDone();
    }

    /// <summary>The functions the app runs itself, which GPT-Live's backend gets beside the server's.</summary>
    private JsonArray LiveTools()
    {
        var list = new JsonArray();
        if (EndsOnGoodbye)
        {
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = EndConversationTool,
                ["description"] = EndConversationDescription,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            });
        }
        foreach (var function in _options.Functions)
        {
            if (SideOf(function.Name) != SpineAIFunctionSide.Device)
                continue;
            list.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = function.Name,
                ["description"] = function.Description,
                ["parameters"] = function.JsonSchema.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(function.JsonSchema.GetRawText())
                    : new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            });
        }
        return list;
    }

    /// <summary>The conversation so far as GPT-Live's startup history: the same messages Realtime gets as items.</summary>
    private static JsonArray LiveHistory(IEnumerable<JsonObject> history) =>
        new([.. history.Select(item => item["item"]!.DeepClone())]);

    /// <summary>The text of a conversation's user and assistant turns, as items the provider adds before the first turn.</summary>
    private static IEnumerable<JsonObject> History(IEnumerable<ChatMessage> conversation)
    {
        var turns = conversation
            .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && m.AdditionalProperties?.ContainsKey(PendingKey) != true)
            .Select(m => (m.Role, Text: string.Concat(m.Contents.OfType<TextContent>().Select(t => t.Text)).Trim()))
            .Where(m => m.Text.Length > 0)
            .TakeLast(MaxHistory);

        foreach (var (role, text) in turns)
        {
            var user = role == ChatRole.User;
            yield return new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "message",
                    ["role"] = user ? "user" : "assistant",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = user ? "input_text" : "output_text", ["text"] = text }),
                },
            };
        }
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

        // The words have arrived, so a pending turn is pending no longer.
        var properties = old.AdditionalProperties;
        if (properties?.ContainsKey(PendingKey) == true)
        {
            properties = new AdditionalPropertiesDictionary(properties);
            properties.Remove(PendingKey);
            if (properties.Count == 0)
                properties = null;
        }

        var replacement = new ChatMessage(role, append ? old.Text + text : text) { AdditionalProperties = properties };
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
