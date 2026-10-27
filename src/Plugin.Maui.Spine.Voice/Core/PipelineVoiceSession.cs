using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Spine.AI;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// A voice session over a <see cref="VoicePipeline"/>: the microphone through the engine, turns found by their level,
/// each written down, answered by a chat model and read aloud sentence by sentence through the same engine.
/// </summary>
internal sealed class PipelineVoiceSession : IVoiceSession, IVoiceAudioOwner
{
    private const int Rate = 24_000;
    private const int MaxHistory = 40;

    // Audio from before the detector was sure a turn had started, so its first syllable is not lost.
    private const int PreRollBytes = Rate * 2 * 300 / 1000;
    private static readonly TimeSpan InterruptedFor = TimeSpan.FromMilliseconds(600);

    private readonly IVoiceAudio _audio;
    private readonly SpineVoiceOptions _options;
    private readonly PipelineServer _server;
    private readonly IServiceProvider? _services;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ObservableCollection<ChatMessage> _own = [];
    private IList<ChatMessage> _transcript;
    private readonly Dictionary<string, ChatMessage> _byItem = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpineAIFunctionSide> _preferred = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly Lock _turnGate = new();
    private readonly Lock _pendingGate = new();
    private readonly Dictionary<string, string> _pendingText = new(StringComparer.Ordinal);

    private VoicePipeline? _pipeline;
    private List<SpeechToTextPart> _ears = [];
    private List<(string Name, IChatClient Client, bool Own)> _chats = [];
    private List<TextToSpeechPart> _mouths = [];
    private int _mouth;
    private TurnDetector? _detector;
    private readonly byte[] _preRoll = new byte[PreRollBytes];
    private int _preRollLength;
    private Turn? _turn;
    private int _turns;
    private int _generation;
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _answer;
    private ITimer? _ticker;
    private ITimer? _limit;
    private long _endsAt;
    private volatile bool _connected;
    private volatile bool _connecting;
    private volatile int _transcribing;
    private volatile bool _answering;
    // The chat hung up: the farewell plays to its end and the microphone is not listened to.
    private volatile bool _farewell;
    private volatile bool _answerAudio;
    private long _interruptedUntil;
    private VoiceState _state = VoiceState.Idle;
    private int _ended;

    public PipelineVoiceSession(
        IVoiceAudio audio, SpineVoiceOptions options, PipelineServer server, Action<Action> dispatch,
        TimeProvider? time = null, ILogger? logger = null, IServiceProvider? services = null)
    {
        _audio = audio;
        _options = options;
        _server = server;
        _services = services;
        _dispatch = dispatch;
        _transcript = _own;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _audio.StateChanged += _ => Evaluate();
    }

    public IVoiceAudio Audio => _audio;

    public VoiceState State { get { lock (_gate) return _state; } }
    public bool IsConnected => _connected;
    public IList<ChatMessage> Transcript => _transcript;
    public double? MaxMinutes { get; set; }

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
    public string? Voice { get; set; }
    public string? SpokenVoice { get; private set; }

    /// <summary>
    /// Each answer offers the chat <see cref="VoiceSession.EndConversationTool"/> as the app's own function; a server feature
    /// must declare it (<c>ai.Functions.AddDevice</c>) and list it, or its model never sees it.
    /// </summary>
    public bool EndsOnGoodbye { get; set; }

    public bool IsMuted
    {
        get => _audio.IsMuted;
        set
        {
            _audio.IsMuted = value;
            // A muted microphone hears no quiet either, so the turn under way ends now.
            if (value)
                EndTurn();
            Evaluate();
        }
    }

    public VoiceDuplex Duplex
    {
        get => _audio.Duplex;
        set => _audio.Duplex = value;
    }

    public event Action<VoiceState>? StateChanged;
    public event Action<VoiceSessionEnd, Exception?>? Ended;

    public Task ConnectAsync(string feature, CancellationToken cancellationToken = default) => ConnectCoreAsync(feature, null, cancellationToken);

    public Task ConnectAsync(string feature, IList<ChatMessage> conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return ConnectCoreAsync(feature, conversation, cancellationToken);
    }

    private async Task ConnectCoreAsync(string name, IList<ChatMessage>? conversation, CancellationToken cancellationToken)
    {
        if (_connected || _connecting)
            throw new InvalidOperationException("The voice session is already connected; end it before connecting again.");
        var pipeline = _options.Pipelines.GetValueOrDefault(name)
            ?? throw new InvalidOperationException($"There is no voice pipeline named '{name}'.");
        pipeline.Validate();

        _connecting = true;
        _ended = 0;
        _farewell = false;
        _server.ConversationId = Guid.NewGuid().ToString("N");
        _dispatch(() =>
        {
            _byItem.Clear();
            _transcript = conversation ?? _own;
            if (conversation is null)
                _own.Clear();
        });
        lock (_pendingGate)
            _pendingText.Clear();
        Evaluate();
        try
        {
            _pipeline = pipeline;
            _chats = [.. pipeline.Chats.Select(c => (c.Name, c.Own ? new FunctionInvokingChatClient(c.Create(_services), null, _services) : c.Create(_services), c.Own))];
            _mouths = [.. pipeline.Mouths.Select(m => m(_services, _server))];
            _mouth = 0;
            _ears = [.. pipeline.Ears.Select(e => e(_services, _server))];
            var refused = new List<string>();
            foreach (var ear in _ears)
            {
                try
                {
                    await ear.PrepareAsync(cancellationToken);
                    if (!ear.IsAvailable)
                        refused.Add($"{ear.Name} is not available here or not allowed");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    refused.Add($"{ear.Name}: {e.Message}");
                    _logger.LogWarning(e, "Spine.Voice: {Part} cannot be used.", ear.Name);
                }
            }
            if (!_ears.Any(e => e.IsAvailable))
                throw new InvalidOperationException($"The voice pipeline '{name}' cannot write speech down: {string.Join("; ", refused)}.");
            foreach (var reason in refused)
                _logger.LogInformation("Spine.Voice: '{Pipeline}' skips {Reason}.", name, reason);

            _detector = new TurnDetector(Rate, pipeline.SpeechThreshold, pipeline.StartOfTurn, pipeline.EndOfTurn, pipeline.MaxTurn);
            _preRollLength = 0;
            await _audio.StartAsync(new VoiceAudioOptions { InputRate = Rate, OutputRate = Rate, Duplex = _audio.Duplex }, cancellationToken);
            _audio.InputAvailable += OnMicrophone;

            _run = new CancellationTokenSource();
            _ticker = _time.CreateTimer(_ => Tick(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            // No server reserves a pipeline's minutes, so the app's own limit is the only one.
            if (MaxMinutes is { } minutes and > 0)
            {
                var limit = TimeSpan.FromMinutes(minutes);
                Interlocked.Exchange(ref _endsAt, (_time.GetUtcNow() + limit).UtcTicks);
                _limit = _time.CreateTimer(_ => _ = EndCoreAsync(VoiceSessionEnd.TimeLimit, null), null, limit, Timeout.InfiniteTimeSpan);
            }
            SpokenVoice = _mouths.FirstOrDefault(m => m.IsAvailable)?.Name;
            _connected = true;
            _logger.LogInformation("Spine.Voice: connected to the pipeline '{Pipeline}'.", name);
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
        lock (_turnGate)
        {
            _detector?.Begin();
            StartTurn(manual: true);
        }
    }

    public void EndTurn()
    {
        lock (_turnGate)
        {
            _detector?.Reset();
            FinishTurn();
        }
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!_connected)
            throw new InvalidOperationException("Connect the voice session before sending text.");

        BargeIn();
        var generation = Interlocked.Increment(ref _generation);
        _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.User, text)));
        _ = Task.Run(() => AnswerAsync(generation));
        return Task.CompletedTask;
    }

    public void Prefer(string function, SpineAIFunctionSide side)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        lock (_preferred)
            _preferred[function] = side;
    }

    public VoiceFrame UpdateFrame()
    {
        var frame = _audio.UpdateFrame();
        Evaluate();
        frame.State = State;
        frame.Viseme = _options.VisemeSource is { } visemes && _audio.Playing is { } playing ? visemes.VisemeAt(playing) : -1;
        return frame;
    }

    public async ValueTask DisposeAsync() => await EndCoreAsync(VoiceSessionEnd.User, null);

    private sealed class Turn(string id, IHeardTurn? heard, int ear, long started)
    {
        public string Id { get; } = id;
        public long Started { get; } = started;
        public long Ended { get; set; }
        public MemoryStream Audio { get; } = new();
        public IHeardTurn? Heard { get; } = heard;
        public int Ear { get; } = ear;
    }

    private void OnMicrophone(ReadOnlySpan<byte> pcm16)
    {
        if (_farewell)
            return;
        lock (_turnGate)
        {
            if (_detector is not { } detector)
                return;
            var wasInTurn = detector.InTurn;
            var edge = detector.Feed(pcm16, _audio.IsPlaying || _answering);

            if (wasInTurn || edge == TurnEdge.Started)
                Append(pcm16);
            else
                KeepForPreRoll(pcm16);

            if (edge == TurnEdge.Started && _turn is null)
            {
                StartTurn(manual: false);
                Append(pcm16);
            }
            else if (edge == TurnEdge.Ended)
                FinishTurn();
        }
    }

    private void Append(ReadOnlySpan<byte> pcm16)
    {
        if (_turn is not { } turn)
            return;
        turn.Audio.Write(pcm16);
        try
        {
            turn.Heard?.Append(pcm16);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Spine.Voice: the recognizer would not take the audio.");
        }
    }

    private void KeepForPreRoll(ReadOnlySpan<byte> pcm16)
    {
        if (pcm16.Length >= PreRollBytes)
        {
            pcm16[^PreRollBytes..].CopyTo(_preRoll);
            _preRollLength = PreRollBytes;
            return;
        }
        var keep = Math.Min(_preRollLength, PreRollBytes - pcm16.Length);
        _preRoll.AsSpan(_preRollLength - keep, keep).CopyTo(_preRoll);
        pcm16.CopyTo(_preRoll.AsSpan(keep));
        _preRollLength = keep + pcm16.Length;
    }

    /// <summary>The user started talking: an answer is cut off, the turn shows as pending, and its words start to be written down.</summary>
    private void StartTurn(bool manual)
    {
        if (_turn is not null)
            return;
        if (_audio.IsPlaying || _answering || manual)
            BargeIn();
        Interlocked.Increment(ref _generation);

        var (heard, ear) = BeginHearing(0);
        var turn = new Turn($"turn-{Interlocked.Increment(ref _turns)}", heard, ear, _time.GetTimestamp());
        _logger.LogDebug("Spine.Voice: {Turn} started (threshold {Threshold:0.000}, {Part}{Barged}).", turn.Id, _detector?.Threshold,
            ear < _ears.Count ? _ears[ear].Name : "no part", manual ? ", pushed" : "");
        _turn = turn;
        if (!manual && _preRollLength > 0)
        {
            var preRoll = _preRoll.AsSpan(0, _preRollLength);
            turn.Audio.Write(preRoll);
            turn.Heard?.Append(preRoll);
        }
        _preRollLength = 0;
        _dispatch(() => AddMessage(turn.Id, new ChatMessage(ChatRole.User, "") { AdditionalProperties = new() { [VoiceSession.PendingKey] = true } }));
    }

    private void FinishTurn()
    {
        if (Interlocked.Exchange(ref _turn, null) is not { } turn)
            return;
        turn.Ended = _time.GetTimestamp();
        _logger.LogDebug("Spine.Voice: {Turn} ended after {Length:0.0} s.", turn.Id, _time.GetElapsedTime(turn.Started, turn.Ended).TotalSeconds);
        Interlocked.Increment(ref _transcribing);
        var generation = Volatile.Read(ref _generation);
        _ = Task.Run(() => HandleTurnAsync(turn, generation));
        Evaluate();
    }

    /// <summary>The first speech-to-text part from <paramref name="from"/> on that starts; null when none does.</summary>
    private (IHeardTurn? Heard, int Ear) BeginHearing(int from)
    {
        for (var i = from; i < _ears.Count; i++)
        {
            if (!_ears[i].IsAvailable)
                continue;
            try
            {
                return (_ears[i].Begin(), i);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Spine.Voice: {Part} would not start; the next part writes the turn down.", _ears[i].Name);
            }
        }
        return (null, _ears.Count);
    }

    private async Task HandleTurnAsync(Turn turn, int generation)
    {
        var cancellationToken = _run?.Token ?? CancellationToken.None;
        string? text = null;
        try
        {
            text = await TranscribeAsync(turn, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            turn.Heard?.Dispose();
            Interlocked.Decrement(ref _transcribing);
            Evaluate();
        }

        _logger.LogDebug("Spine.Voice: {Turn} written down {Ms:0} ms after it ended: {Length} characters.", turn.Id, _time.GetElapsedTime(turn.Ended).TotalMilliseconds, text?.Length ?? -1);
        if (string.IsNullOrWhiteSpace(text))
        {
            _dispatch(() => RemoveMessage(turn.Id));
            return;
        }
        _dispatch(() => ReplaceText(turn.Id, ChatRole.User, text, append: false));

        // The user went on talking after a pause: that turn is answered, with this one in the conversation before it.
        if (Volatile.Read(ref _generation) == generation)
            await AnswerAsync(generation);
        else
            _logger.LogDebug("Spine.Voice: {Turn} is not answered; a later turn is.", turn.Id);
    }

    /// <summary>The turn's words: from the part that heard it live, else each next part given the whole turn's audio.</summary>
    private async Task<string?> TranscribeAsync(Turn turn, CancellationToken cancellationToken)
    {
        var audio = turn.Audio.ToArray();
        Exception? last = null;
        for (var (heard, ear) = (turn.Heard, turn.Ear); ear < _ears.Count;)
        {
            if (heard is not null)
            {
                try
                {
                    if (heard != turn.Heard)
                        heard.Append(audio);
                    return await heard.FinishAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    last = e;
                    _logger.LogWarning(e, "Spine.Voice: {Part} could not write the turn down; trying the next part.", _ears[ear].Name);
                }
                finally
                {
                    if (heard != turn.Heard)
                        heard.Dispose();
                }
            }
            (heard, ear) = BeginHearing(ear + 1);
        }

        if (last is not null)
            _logger.LogError(last, "Spine.Voice: no part could write the turn down.");
        return null;
    }

    private async Task AnswerAsync(int generation)
    {
        if (_run is not { } run || _pipeline is null)
            return;
        // Not disposed: a barge-in may cancel it from another thread just as it ends.
        var answer = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        Interlocked.Exchange(ref _answer, answer)?.Cancel();
        if (Volatile.Read(ref _generation) != generation)
            return;

        var cancellationToken = answer.Token;
        var item = $"answer-{Guid.NewGuid():N}";
        var asked = _time.GetTimestamp();
        var firstWords = true;
        ChatFinishReason? finish = null;
        _answering = true;
        _answerAudio = false;
        Evaluate();

        var history = await HistoryAsync();
        var sentences = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var speaking = SpeakAsync(item, sentences.Reader, cancellationToken);
        var splitter = new SentenceSplitter();
        var said = new StringBuilder();
        var goodbye = false;
        var options = ChatOptions(EndsOnGoodbye ? Goodbye(said, () => goodbye = _farewell = true) : null);
        Exception? failed = null;
        try
        {
            for (var i = 0; i < _chats.Count; i++)
            {
                try
                {
                    var chatOptions = _chats[i].Own ? await WithAppFunctionsAsync(options, cancellationToken) : options;
                    await foreach (var update in _chats[i].Client.GetStreamingResponseAsync(history, chatOptions, cancellationToken))
                    {
                        finish = update.FinishReason ?? finish;
                        foreach (var content in update.Contents)
                        {
                            switch (content)
                            {
                                case TextContent { Text: { Length: > 0 } words }:
                                    if (firstWords)
                                        _logger.LogDebug("Spine.Voice: the chat's first words after {Ms:0} ms.", _time.GetElapsedTime(asked).TotalMilliseconds);
                                    firstWords = false;
                                    said.Append(words);
                                    lock (_pendingGate)
                                        _pendingText[item] = _pendingText.GetValueOrDefault(item) + words;
                                    foreach (var sentence in splitter.Append(words))
                                        sentences.Writer.TryWrite(sentence);
                                    break;
                                case FunctionCallContent call:
                                    _logger.LogDebug("Spine.Voice: the chat called {Function} after {Ms:0} ms.", call.Name, _time.GetElapsedTime(asked).TotalMilliseconds);
                                    _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Assistant, [call])));
                                    break;
                                case FunctionResultContent result:
                                    _dispatch(() => _transcript.Add(new ChatMessage(ChatRole.Tool, [result])));
                                    break;
                            }
                        }
                    }
                    failed = null;
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException && said.Length == 0)
                {
                    // Another chat answers only while nothing has been said: half an answer from one and the rest from another would not hang together.
                    failed = e;
                    _logger.LogWarning(e, "Spine.Voice: the chat '{Chat}' failed.", _chats[i].Name);
                }
            }
            if (splitter.Flush() is { } rest)
                sentences.Writer.TryWrite(rest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            failed = e;
        }
        finally
        {
            sentences.Writer.TryComplete();
        }

        try
        {
            await speaking;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        // Cut off before a tick had shown it, the answer is marked here.
        var cut = answer.IsCancellationRequested && !run.IsCancellationRequested;
        _logger.LogDebug("Spine.Voice: the answer ended after {Ms:0} ms{Cut}: {Length} characters.", _time.GetElapsedTime(asked).TotalMilliseconds, cut ? ", cut off" : "", said.Length);
        // A model that spends its output limit on reasoning answers nothing, and the user hears only silence.
        if (!cut && failed is null && said.Length == 0)
            _logger.LogWarning("Spine.Voice: the chat answered no text (finish reason {Finish}); a reasoning model may have used its output limit.", finish?.Value ?? "none");
        lock (_pendingGate)
        {
            _pendingText.Remove(item);
            if (said.Length > 0)
            {
                _dispatch(() =>
                {
                    ReplaceText(item, ChatRole.Assistant, said.ToString().Trim(), append: false);
                    if (cut)
                        MarkInterrupted(item);
                });
            }
        }
        if (Interlocked.CompareExchange(ref _answer, null, answer) == answer)
        {
            _answering = false;
            _answerAudio = false;
        }
        Evaluate();

        // No chat could answer: the call ends and says why, as a realtime session does when its provider fails.
        if (failed is not null)
            await EndCoreAsync(VoiceSessionEnd.Failed, failed);
        else if (goodbye && !cut)
            await HangUpAfterFarewellAsync();
        else
            _farewell = false;
    }

    /// <summary>
    /// Ends the call once the farewell has played, its last word out of the speaker too. The user saying goodbye back is
    /// not heard (see <see cref="OnMicrophone"/>); only the Stop button or typed text cuts the farewell and goes on.
    /// </summary>
    private async Task HangUpAfterFarewellAsync()
    {
        _logger.LogDebug("Spine.Voice: the chat hung up; the call ends after the farewell.");
        while (_connected && _farewell && _audio.IsPlaying)
            await Task.Delay(TimeSpan.FromMilliseconds(50), _time);
        await Task.Delay(VoiceSession.FarewellTail, _time);
        if (_connected && _farewell)
            await EndCoreAsync(VoiceSessionEnd.Goodbye, null);
    }

    /// <summary>The options for the app's own chat client: the app's functions too, run with the device's context.</summary>
    private async Task<ChatOptions> WithAppFunctionsAsync(ChatOptions options, CancellationToken cancellationToken)
    {
        if (_options.Functions.Count == 0)
            return options;
        var context = await SpineAIFunctionContext.ForDeviceAsync(_services, _server.InstallationIdAsync, _pipeline?.Name, _server.ConversationId, null, cancellationToken);
        var own = options.Clone();
        own.Tools = [.. options.Tools ?? [], .. _options.Functions.Select(f => (AITool)f.Bind(context, static () => FunctionInvokingChatClient.CurrentContext?.CallContent.CallId))];
        return own;
    }

    /// <summary>The hang-up tool, run here: a farewell already said in this answer is the farewell.</summary>
    private static AIFunction Goodbye(StringBuilder said, Action hangUp) =>
        AIFunctionFactory.Create(() =>
        {
            hangUp();
            return said.Length > 0 ? VoiceSession.GoodbyeSaid : VoiceSession.SayGoodbye;
        }, VoiceSession.EndConversationTool, VoiceSession.EndConversationDescription);

    /// <summary>Reads the sentences aloud as they come, through the engine as one item, so a cut-off says how far it got.</summary>
    private async Task SpeakAsync(string item, ChannelReader<string> sentences, CancellationToken cancellationToken)
    {
        await foreach (var sentence in sentences.ReadAllAsync(cancellationToken))
        {
            for (; _mouth < _mouths.Count; _mouth++)
            {
                var mouth = _mouths[_mouth];
                if (!mouth.IsAvailable)
                    continue;
                var played = false;
                var started = _time.GetTimestamp();
                var seconds = 0.0;
                try
                {
                    PcmResampler? resampler = null;
                    await foreach (var piece in mouth.SayAsync(sentence, cancellationToken))
                    {
                        if (!played)
                            _logger.LogDebug("Spine.Voice: {Part} gave its first audio {Ms:0} ms after the sentence came, at {Rate} Hz.", mouth.Name, _time.GetElapsedTime(started).TotalMilliseconds, piece.SampleRate);
                        seconds += piece.Pcm16.Length / 2.0 / piece.SampleRate;
                        var pcm = piece.SampleRate == Rate ? piece.Pcm16.Span
                            : Resample(piece.Pcm16.Span, resampler ??= new PcmResampler(piece.SampleRate, Rate));
                        _audio.Enqueue(item, pcm);
                        played = true;
                        _answerAudio = true;
                    }
                    SpokenVoice = mouth.Name;
                    _logger.LogDebug("Spine.Voice: {Part} read {Characters} characters as {Seconds:0.0} s of audio in {Ms:0} ms{Empty}.", mouth.Name, sentence.Length,
                        seconds, _time.GetElapsedTime(started).TotalMilliseconds, played ? "" : ", but gave no audio");
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning(e, "Spine.Voice: {Part} could not read the answer.", mouth.Name);
                    // A sentence cut off half-way is not said again by the next voice.
                    if (played)
                        break;
                }
            }
            if (_mouth >= _mouths.Count)
                _logger.LogError("Spine.Voice: no voice could read the answer; it is shown, not said.");
        }
        _audio.Complete(item);
    }

    private static byte[] Resample(ReadOnlySpan<byte> pcm16, PcmResampler resampler)
    {
        var samples = MemoryMarshal.Cast<byte, short>(pcm16);
        var input = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            input[i] = samples[i] / 32768f;
        var output = new float[resampler.MaxOutput(input.Length)];
        var written = resampler.Process(input, output);
        var bytes = new byte[written * 2];
        var shorts = MemoryMarshal.Cast<byte, short>(bytes.AsSpan());
        for (var i = 0; i < written; i++)
            shorts[i] = (short)(Math.Clamp(output[i], -1f, 1f) * short.MaxValue);
        return bytes;
    }

    private ChatOptions ChatOptions(AIFunction? goodbye)
    {
        var options = new ChatOptions
        {
            Tools = goodbye is null ? null : [goodbye],
            AdditionalProperties = new() { [SpineClientHeaders.ConversationKey] = _server.ConversationId },
        };
        lock (_preferred)
        {
            foreach (var (function, side) in _preferred)
                options.Prefer(function, side);
        }
        return options;
    }

    /// <summary>The conversation's text turns, read on the main thread where the app changes the list.</summary>
    private Task<List<ChatMessage>> HistoryAsync()
    {
        var taken = new TaskCompletionSource<List<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatch(() =>
        {
            try
            {
                taken.SetResult([.. _transcript
                    .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && m.AdditionalProperties?.ContainsKey(VoiceSession.PendingKey) != true)
                    .Select(m => (m.Role, Text: string.Concat(m.Contents.OfType<TextContent>().Select(t => t.Text)).Trim()))
                    .Where(m => m.Text.Length > 0)
                    .TakeLast(MaxHistory)
                    .Select(m => new ChatMessage(m.Role, m.Text))]);
            }
            catch (Exception e)
            {
                taken.SetException(e);
            }
        });
        return taken.Task;
    }

    /// <summary>Cuts the answer off: the chat stops, the engine flushes, and the answer is marked as cut off where it was heard.</summary>
    private void BargeIn()
    {
        // Only the Stop button and typed text reach here during the farewell: the conversation goes on.
        _farewell = false;
        var wasAnswering = _answering;
        if (wasAnswering || _audio.IsPlaying)
            _logger.LogDebug("Spine.Voice: the answer is cut off ({What}).", _audio.IsPlaying ? "playing" : "not playing yet");
        Interlocked.Exchange(ref _answer, null)?.Cancel();
        var position = _audio.Flush();
        if (position is { } played)
            _dispatch(() => MarkInterrupted(played.ItemId));
        if (position is not null || wasAnswering)
            Interlocked.Exchange(ref _interruptedUntil, (_time.GetUtcNow() + InterruptedFor).UtcTicks);
        _answering = false;
        _answerAudio = false;
        Evaluate();
    }

    private void Tick()
    {
        // Dispatched under the lock, so the words shown on a tick are never shown again after the answer's final text.
        lock (_pendingGate)
        {
            KeyValuePair<string, string>[] pending = [.. _pendingText];
            _pendingText.Clear();
            if (pending.Length > 0)
            {
                _dispatch(() =>
                {
                    foreach (var (item, words) in pending)
                        ReplaceText(item, ChatRole.Assistant, words, append: true);
                });
            }
        }
        Evaluate();
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
        else if (_audio.IsPlaying || _answering && _answerAudio)
            next = VoiceState.Speaking;
        else if (_answering || _transcribing > 0)
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
            _logger.LogError(error, "Spine.Voice: the pipeline session failed.");

        await TearDownAsync();
        Evaluate();
        _dispatch(() =>
        {
            foreach (var (item, message) in _byItem.ToList())
            {
                if (message.AdditionalProperties?.ContainsKey(VoiceSession.PendingKey) == true)
                    RemoveMessage(item);
            }
            _byItem.Clear();
            _transcript = _own;
        });
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
        _run?.Cancel();
        Interlocked.Exchange(ref _answer, null)?.Cancel();
        lock (_turnGate)
        {
            if (Interlocked.Exchange(ref _turn, null) is { } turn)
                turn.Heard?.Dispose();
            _detector = null;
        }
        await _audio.StopAsync();
        SpokenVoice = null;
        _answering = false;
        _answerAudio = false;
        _transcribing = 0;
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

        var properties = old.AdditionalProperties;
        if (properties?.ContainsKey(VoiceSession.PendingKey) == true)
        {
            properties = new AdditionalPropertiesDictionary(properties);
            properties.Remove(VoiceSession.PendingKey);
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
            message.AdditionalProperties[VoiceSession.InterruptedKey] = true;
            ReplaceText(itemId, message.Role, message.Text, append: false);
        }
    }
}
