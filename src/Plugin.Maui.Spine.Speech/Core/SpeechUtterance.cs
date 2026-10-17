namespace Plugin.Maui.Spine.Speech;

/// <summary>One text being spoken: when it starts, each word as the voice reaches it, where it is, and how it ended.</summary>
public sealed class SpeechUtterance
{
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private readonly TaskCompletionSource<SpeechEndReason> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<SpeechUtterance>? _stop;
    private long _startedAt;
    private TimeSpan _endedAt;

    internal SpeechUtterance(string text, SpeakOptions options, Action<Action> dispatch, TimeProvider? time = null)
    {
        Text = text;
        Options = options;
        _dispatch = dispatch;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The text being spoken.</summary>
    public string Text { get; }

    /// <summary>How it is spoken.</summary>
    public SpeakOptions Options { get; }

    /// <summary>Where it is.</summary>
    public SpeechUtteranceState State { get; private set; }

    /// <summary>
    /// How long it has been speaking: zero while queued, frozen once it ends. The platform's playback position where it
    /// has one (Windows), the time since the audio started elsewhere.
    /// </summary>
    public TimeSpan Position => State switch
    {
        SpeechUtteranceState.Queued => TimeSpan.Zero,
        SpeechUtteranceState.Speaking => PositionSource?.Invoke() ?? _time.GetElapsedTime(_startedAt),
        _ => _endedAt,
    };

    /// <summary>Whether it is speaking now.</summary>
    public bool IsSpeaking => State == SpeechUtteranceState.Speaking;

    /// <summary>Completes with the reason when it ends.</summary>
    public Task<SpeechEndReason> Completed => _completed.Task;

    /// <summary>Raised when the audio starts.</summary>
    public event EventHandler? Started;

    /// <summary>Raised as the voice reaches each word.</summary>
    public event EventHandler<SpeechWord>? Word;

    /// <summary>Raised for each mouth shape, by voices that report them.</summary>
    public event EventHandler<SpeechViseme>? Viseme;

    /// <summary>Raised once when it ends, however it ends.</summary>
    public event EventHandler<SpeechEndReason>? Ended;

    /// <summary>Stops it, or drops it from the queue; it ends as <see cref="SpeechEndReason.Stopped"/>.</summary>
    public void Stop()
    {
        if (State == SpeechUtteranceState.Ended)
            return;
        if (_stop is { } stop)
            stop(this);
        else
            OnEnded(SpeechEndReason.Stopped);
    }

    /// <summary>The platform's playback position, where it has one.</summary>
    internal Func<TimeSpan>? PositionSource { get; set; }

    /// <summary>What <see cref="Stop"/> asks the engine to do; the engine then reports the end.</summary>
    internal void OnStop(Action<SpeechUtterance> stop) => _stop = stop;

    internal void OnStarted()
    {
        if (State != SpeechUtteranceState.Queued)
            return;
        _startedAt = _time.GetTimestamp();
        State = SpeechUtteranceState.Speaking;
        _dispatch(() => Started?.Invoke(this, EventArgs.Empty));
    }

    internal void OnWord(int start, int length)
    {
        if (State != SpeechUtteranceState.Speaking || start < 0 || length <= 0 || start + length > Text.Length)
            return;
        var word = new SpeechWord(start, length, Position);
        _dispatch(() => Word?.Invoke(this, word));
    }

    internal void OnViseme(int id, TimeSpan at, TimeSpan duration)
    {
        if (State == SpeechUtteranceState.Ended)
            return;
        var viseme = new SpeechViseme(id, at, duration);
        _dispatch(() => Viseme?.Invoke(this, viseme));
    }

    internal void OnEnded(SpeechEndReason reason)
    {
        if (State == SpeechUtteranceState.Ended)
            return;
        _endedAt = Position;
        State = SpeechUtteranceState.Ended;
        _dispatch(() =>
        {
            Ended?.Invoke(this, reason);
            _completed.TrySetResult(reason);
        });
    }

    /// <inheritdoc />
    public override string ToString() => $"{State}: {(Text.Length > 40 ? Text[..40] + "…" : Text)}";
}

/// <summary>Where an utterance is.</summary>
public enum SpeechUtteranceState
{
    /// <summary>Waiting for those before it.</summary>
    Queued,

    /// <summary>The audio is playing.</summary>
    Speaking,

    /// <summary>Done; <see cref="SpeechUtterance.Completed"/> says how.</summary>
    Ended,
}
