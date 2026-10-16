namespace Plugin.Maui.Spine.Voice;

/// <summary>Whether the microphone is uploaded while an answer plays.</summary>
public enum VoiceDuplex
{
    /// <summary>Always: the user can talk over the answer. Needs echo cancellation that works.</summary>
    Full,

    /// <summary>Not while an answer plays: the user interrupts with a tap. For devices whose echo cancellation is too weak.</summary>
    HalfWhileSpeaking,
}

/// <summary>Why the audio engine could not run.</summary>
public enum VoiceAudioProblem
{
    /// <summary>The app is missing something it must declare, such as <c>NSMicrophoneUsageDescription</c>.</summary>
    NotConfigured,

    /// <summary>The user did not allow the microphone.</summary>
    PermissionDenied,

    /// <summary>The device has no microphone, or the platform has no engine yet.</summary>
    Unavailable,

    /// <summary>The platform's audio failed; the message says how.</summary>
    Failed,
}

/// <summary>The audio engine could not start or stopped working.</summary>
/// <param name="problem">Why.</param>
/// <param name="message">What to tell a developer.</param>
/// <param name="inner">The platform's own error.</param>
public sealed class VoiceAudioException(VoiceAudioProblem problem, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Why.</summary>
    public VoiceAudioProblem Problem { get; } = problem;
}

/// <summary>How the engine runs one conversation.</summary>
public sealed class VoiceAudioOptions
{
    /// <summary>The microphone's rate, as the provider wants it. 24 kHz by default (OpenAI); Gemini takes 16 kHz.</summary>
    public int InputRate { get; init; } = 24_000;

    /// <summary>The rate of the provider's audio. 24 kHz by default.</summary>
    public int OutputRate { get; init; } = 24_000;

    /// <summary>
    /// The platform's echo cancellation and noise suppression on the microphone. On by default: without it the provider
    /// hears its own answer and interrupts itself. Off is for measuring.
    /// </summary>
    public bool VoiceProcessing { get; init; } = true;

    /// <summary>The loudspeaker rather than the receiver at the ear, when no headset is connected. On by default.</summary>
    public bool PreferSpeaker { get; init; } = true;

    /// <summary>Bluetooth headsets with a microphone (HFP). On by default.</summary>
    public bool AllowBluetooth { get; init; } = true;

    /// <summary>Whether the microphone is uploaded while an answer plays.</summary>
    public VoiceDuplex Duplex { get; init; } = VoiceDuplex.Full;

    /// <summary>How long <see cref="VoiceState.Connecting"/> lasts after the start, while the echo canceller settles. One second by default.</summary>
    public TimeSpan Convergence { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How much of an answer is queued before it starts to play. 100 ms by default.</summary>
    public TimeSpan Prebuffer { get; init; } = TimeSpan.FromMilliseconds(100);

    internal void Validate()
    {
        if (InputRate is < 8_000 or > 48_000 || OutputRate is < 8_000 or > 48_000)
            throw new ArgumentException("InputRate and OutputRate must be between 8 and 48 kHz.");
        if (Convergence < TimeSpan.Zero || Prebuffer < TimeSpan.Zero)
            throw new ArgumentException("Convergence and Prebuffer may not be negative.");
    }
}

/// <summary>PCM16 from the microphone, little-endian mono at <see cref="VoiceAudioOptions.InputRate"/>, on the audio thread.</summary>
/// <param name="pcm16">The samples. Valid during the call only; copy what you keep.</param>
public delegate void VoiceInputHandler(ReadOnlySpan<byte> pcm16);

/// <summary>
/// The full-duplex audio of one voice conversation: the microphone with the platform's echo cancellation, and a player
/// for the provider's answer that can be cut off at once. Provider adapters build on it; an app usually uses a voice
/// session instead.
/// </summary>
/// <remarks>
/// One engine runs one conversation at a time. <see cref="InputAvailable"/> is raised on the audio thread and the other
/// events on any thread; everything may be called from any thread, and <see cref="UpdateFrame"/> belongs to the UI's tick.
/// </remarks>
public interface IVoiceAudio : IAsyncDisposable
{
    /// <summary>Whether this platform has an engine.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the engine runs.</summary>
    bool IsRunning { get; }

    /// <summary><see cref="VoiceState.Idle"/>, <see cref="VoiceState.Connecting"/>, <see cref="VoiceState.Listening"/> or <see cref="VoiceState.Muted"/>.</summary>
    VoiceState State { get; }

    /// <summary>Whether the platform gave echo cancellation, which can differ from what was asked.</summary>
    bool IsVoiceProcessingOn { get; }

    /// <summary>Where the sound goes, such as <c>Speaker</c>, <c>Receiver</c> or a headset's name.</summary>
    string? Route { get; }

    /// <summary>Stops uploading the microphone; the input level shows silence.</summary>
    bool IsMuted { get; set; }

    /// <summary>The loudspeaker rather than the receiver, while no headset is connected.</summary>
    bool UseSpeaker { get; set; }

    /// <summary>Whether the microphone is uploaded while an answer plays; can change mid-conversation.</summary>
    VoiceDuplex Duplex { get; set; }

    /// <summary>Whether answer audio is queued or playing.</summary>
    bool IsPlaying { get; }

    /// <summary>The item playing now and how much of it has played; what a viseme source lines its mouth shapes up with.</summary>
    PlaybackPosition? Playing { get; }

    /// <summary>The frame <see cref="UpdateFrame"/> writes.</summary>
    VoiceFrame Frame { get; }

    /// <summary>The microphone's audio, on the audio thread. Not raised while muted, or while an answer plays in <see cref="VoiceDuplex.HalfWhileSpeaking"/>.</summary>
    event VoiceInputHandler? InputAvailable;

    /// <summary><see cref="State"/> changed. Raised on any thread: from <see cref="UpdateFrame"/>, from calls that change it, and when the convergence ends.</summary>
    event Action<VoiceState>? StateChanged;

    /// <summary>The sound moved, such as to a headset that was connected.</summary>
    event Action<string>? RouteChanged;

    /// <summary>The engine stopped working after it started.</summary>
    event Action<VoiceAudioException>? Failed;

    /// <summary>Opens the microphone and the player.</summary>
    /// <exception cref="VoiceAudioException">The app is not configured, the user said no, or the platform failed.</exception>
    Task StartAsync(VoiceAudioOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Closes the microphone and the player, and drops queued audio.</summary>
    Task StopAsync();

    /// <summary>Queues the provider's audio (PCM16 little-endian mono at <see cref="VoiceAudioOptions.OutputRate"/>) for <paramref name="itemId"/>.</summary>
    void Enqueue(string itemId, ReadOnlySpan<byte> pcm16);

    /// <summary>Says no more audio comes for <paramref name="itemId"/>, so its tail plays without waiting for a prebuffer.</summary>
    void Complete(string itemId);

    /// <summary>Cuts the answer off at once: drops everything queued and says how far the playing item got.</summary>
    PlaybackPosition? Flush();

    /// <summary>How much of <paramref name="itemId"/> has played.</summary>
    TimeSpan Played(string itemId);

    /// <summary>
    /// Writes the latest levels, bands and state into <see cref="Frame"/>. Call it on the UI's tick. The frame says
    /// <see cref="VoiceState.Speaking"/> while an answer plays and the engine is listening, so a view drawing a bare engine
    /// knows who speaks; <see cref="State"/> stays <see cref="VoiceState.Listening"/>.
    /// </summary>
    VoiceFrame UpdateFrame();
}
