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
    private const double Lookahead = 1;
    private const float Strength = 0.8f;

    private readonly List<AvatarTextVisemes.Estimate> _timeline = [];
    private readonly float[] _bands = new float[AvatarRenderFrame.BandCount];
    private double _position, _end;
    private int _next, _current;
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

        var start = Math.Max(_end, _position);
        foreach (var estimate in AvatarTextVisemes.FromText(text, CharactersPerSecond))
            _timeline.Add(estimate with { Start = start + estimate.Start });
        if (_timeline.Count > 0)
            _end = Math.Max(_end, _timeline[^1].Start + _timeline[^1].Duration);
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
        _timeline.Clear();
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
        new(_generation, TimeSpan.FromSeconds(_position), _busy && _position < _end, PositionIsPresentationTime: true,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(250), Stopwatch.GetTimestamp());

    /// <inheritdoc />
    protected internal override void OnAttached()
    {
        Input.PlaybackClock = this;
        // A driver attached mid-utterance starts its cues over: those it had laid down went to another avatar.
        _next = _current = 0;
        while (_next < _timeline.Count && _timeline[_next].Start + _timeline[_next].Duration <= _position)
            _next++;
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
    protected internal override void OnTick(TimeSpan delta)
    {
        if (!_busy)
            return;

        var mouthing = _position < _end;
        if (mouthing)
            _position = Math.Min(_position + Math.Max(delta.TotalSeconds, 0), _end);

        while (_next < _timeline.Count && _timeline[_next].Start <= _position + Lookahead)
        {
            var e = _timeline[_next++];
            Input.EnqueueViseme(_generation, TimeSpan.FromSeconds(e.Start), TimeSpan.FromSeconds(e.Duration), e.Viseme, Strength);
        }

        if (_position < _end)
        {
            while (_current < _timeline.Count - 1 && _timeline[_current].Start + _timeline[_current].Duration <= _position)
                _current++;
            var viseme = _timeline[_current].Viseme;
            var level = AvatarTextVisemes.Level(viseme);
            AvatarTextVisemes.Bands(viseme, level, _position, _bands);
            Input.SetOutputLevel(level, _bands);
            if (Input.State != AvatarState.Speaking)
                Input.SetState(AvatarState.Speaking);
            return;
        }

        Input.SetOutputLevel(0);
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
        _timeline.Clear();
        _position = _end = 0;
        _next = _current = 0;
        _generation++;
        if (IsAttached)
        {
            Input.ResetSpeech(_generation);
            Input.SetState(AvatarState.Thinking);
        }
    }
}
