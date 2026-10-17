namespace Plugin.Maui.Spine.Voice;

/// <summary>Where a voice conversation stands.</summary>
public enum VoiceState
{
    /// <summary>Not started, or ended.</summary>
    Idle,

    /// <summary>Starting: the microphone opens and the echo canceller settles.</summary>
    Connecting,

    /// <summary>The microphone is open and the user can talk.</summary>
    Listening,

    /// <summary>The model works on an answer.</summary>
    Thinking,

    /// <summary>The model's answer plays.</summary>
    Speaking,

    /// <summary>The user talked over the answer, which was cut off.</summary>
    Interrupted,

    /// <summary>The microphone is muted, by the user or by the system (a phone call).</summary>
    Muted,
}

/// <summary>
/// One tick of a voice conversation: its state, a level and 24 bands for each direction. It is one object, overwritten
/// on every tick without allocating. Read it during the call that hands it to you, and call <see cref="Snapshot"/> to keep
/// a copy.
/// </summary>
public sealed class VoiceFrame
{
    /// <summary>The bands per direction, log-spaced from about 80 Hz to 8 kHz.</summary>
    public const int BandCount = 24;

    private readonly float[] _inputBands = new float[BandCount];
    private readonly float[] _outputBands = new float[BandCount];

    /// <summary>Where the conversation stands.</summary>
    public VoiceState State { get; internal set; }

    /// <summary>The microphone's level, 0 to 1 on a decibel scale, after echo cancellation.</summary>
    public float InputLevel { get; internal set; }

    /// <summary>The answer's level, 0 to 1, as it plays.</summary>
    public float OutputLevel { get; internal set; }

    /// <summary>The microphone's bands, 0 to 1 each.</summary>
    public ReadOnlySpan<float> InputBands => _inputBands;

    /// <summary>The answer's bands, 0 to 1 each.</summary>
    public ReadOnlySpan<float> OutputBands => _outputBands;

    /// <summary>The mouth shape for an avatar, or -1 when nothing gives visemes.</summary>
    public int Viseme { get; internal set; } = -1;

    /// <summary>Time since the conversation started.</summary>
    public TimeSpan Time { get; internal set; }

    /// <summary>Time since <see cref="State"/> last changed.</summary>
    public TimeSpan TimeInState { get; internal set; }

    internal Span<float> WritableInputBands => _inputBands;
    internal Span<float> WritableOutputBands => _outputBands;

    /// <summary>A copy that later ticks do not change.</summary>
    public VoiceFrame Snapshot()
    {
        var copy = new VoiceFrame
        {
            State = State,
            InputLevel = InputLevel,
            OutputLevel = OutputLevel,
            Viseme = Viseme,
            Time = Time,
            TimeInState = TimeInState,
        };
        _inputBands.CopyTo(copy._inputBands, 0);
        _outputBands.CopyTo(copy._outputBands, 0);
        return copy;
    }
}
