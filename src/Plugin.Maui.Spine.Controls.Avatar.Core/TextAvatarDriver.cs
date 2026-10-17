using System.Diagnostics;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// Mouths text with no audio: a streamed chat answer, a caption. The mouth is guessed from the letters
/// (<see cref="AvatarTextVisemes"/>) and laid down on the driver's own clock a second ahead of it; the
/// level and bands are guessed from the viseme under the playhead. Quality EstimatedText.
/// </summary>
/// <remarks>
/// States: Thinking after <see cref="Begin"/> or while more text is awaited with nothing left to mouth,
/// Speaking while mouthing, Idle once the text is complete and mouthed, or after <see cref="Clear"/>.
/// </remarks>
public sealed class TextAvatarDriver : AvatarDriver, IAvatarPlaybackClock
{
    private readonly EstimatedMouth _mouth = new();
    private double _position;
    private long _generation;
    private bool _busy, _complete;

    /// <summary>A driver with quality EstimatedText.</summary>
    public TextAvatarDriver() => LipSyncQuality = AvatarLipSyncQuality.EstimatedText;

    /// <summary>How fast text is mouthed; 14 is a calm speaking pace.</summary>
    public double CharactersPerSecond { get; set; } = 14;

    /// <summary>Whether there is text being mouthed or awaited.</summary>
    public bool IsBusy => _busy;

    /// <summary>Seconds of text mouthed so far in this utterance.</summary>
    public double Position => _position;

    /// <summary>Seconds the text queued so far takes to mouth.</summary>
    public double Length => _mouth.End;

    /// <summary>A reply is coming: Thinking until its first text arrives.</summary>
    public void Begin()
    {
        if (!_busy)
            Start();
    }

    /// <summary>Adds text to the utterance; it is mouthed after what is already queued.</summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_busy || _complete)
            Start();

        _mouth.AddText(text, Math.Max(_mouth.End, _position), CharactersPerSecond);
    }

    /// <summary>No more text: once the queued text is mouthed the avatar goes back to Idle.</summary>
    public void Complete()
    {
        if (_busy)
            _complete = true;
    }

    /// <summary>Stops at once: the queued text is dropped, the mouth closes, the avatar is Idle.</summary>
    public void Clear()
    {
        if (!_busy)
            return;
        _busy = false;
        _mouth.Clear();
        _generation++;
        if (IsAttached)
        {
            Input.ResetSpeech(_generation);
            Input.SetOutputLevel(0);
            Input.SetState(AvatarState.Idle);
        }
    }

    /// <summary>Mouths <paramref name="text"/> on its own, replacing anything queued.</summary>
    public void Mouth(string text)
    {
        Clear();
        Append(text);
        Complete();
    }

    AvatarPlaybackSnapshot IAvatarPlaybackClock.GetSnapshot() =>
        new(_generation, TimeSpan.FromSeconds(_position), _busy && _position < _mouth.End, PositionIsPresentationTime: true,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(250), Stopwatch.GetTimestamp());

    /// <inheritdoc />
    protected internal override void OnAttached()
    {
        Input.PlaybackClock = this;
        // A driver attached mid-utterance starts its cues over: those it had laid down went to another avatar.
        _mouth.Rewind(_position);
        _generation++;
        Input.ResetSpeech(_generation);
    }

    /// <inheritdoc />
    protected internal override void OnDetached()
    {
        Input.ResetSpeech(++_generation);
        Input.SetOutputLevel(0);
        if (ReferenceEquals(Input.PlaybackClock, this))
            Input.PlaybackClock = null;
    }

    /// <inheritdoc />
    protected internal override void OnInterrupted() => Clear();

    /// <inheritdoc />
    protected internal override void OnTick(TimeSpan delta)
    {
        if (!_busy)
            return;

        if (_position < _mouth.End)
            _position = Math.Min(_position + Math.Max(delta.TotalSeconds, 0), _mouth.End);

        _mouth.Push(Input, _generation, _position);
        if (_mouth.Sound(Input, _position) > 0 || _position < _mouth.End)
        {
            if (Input.State != AvatarState.Speaking)
                Input.SetState(AvatarState.Speaking);
            return;
        }

        if (_complete)
        {
            _busy = false;
            Input.SetState(AvatarState.Idle);
        }
        else if (Input.State != AvatarState.Thinking)
            Input.SetState(AvatarState.Thinking);
    }

    private void Start()
    {
        _busy = true;
        _complete = false;
        _mouth.Clear();
        _position = 0;
        _generation++;
        if (IsAttached)
        {
            Input.ResetSpeech(_generation);
            Input.SetState(AvatarState.Thinking);
        }
    }
}
