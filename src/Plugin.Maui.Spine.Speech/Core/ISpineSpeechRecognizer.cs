namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// The platform's speech-to-text, fed with audio the app already has (Spine.Voice's microphone, with its echo
/// cancellation) rather than opening a microphone of its own.
/// </summary>
/// <remarks>
/// iOS and Mac Catalyst write down with <c>SFSpeechRecognizer</c>, on the device when the language allows it; the app
/// declares <c>NSSpeechRecognitionUsageDescription</c>. Android and Windows are not supported yet: <see cref="IsSupported"/>
/// is false and <see cref="Start"/> throws.
/// </remarks>
/// <example>
/// <code>
/// if (await recognizer.RequestPermissionAsync())
/// {
///     using var recognition = recognizer.Start(new RecognitionOptions { Language = "sv-SE" });
///     recognition.Partial += text => Caption = text;
///     recognition.Append(pcm16);
///     var said = await recognition.FinishAsync();
/// }
/// </code>
/// </example>
public interface ISpineSpeechRecognizer
{
    /// <summary>Whether this platform can write speech down.</summary>
    bool IsSupported { get; }

    /// <summary>Whether <paramref name="language"/> (BCP-47; the device's language when null) is written down without a network.</summary>
    bool IsOnDevice(string? language = null);

    /// <summary>Asks the user, once, to allow speech recognition; true when it is allowed.</summary>
    /// <exception cref="InvalidOperationException">The app does not declare <c>NSSpeechRecognitionUsageDescription</c>.</exception>
    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts writing down one utterance; append its audio, then finish it.</summary>
    /// <exception cref="InvalidOperationException">Not supported here, not allowed, or the language has no recognizer.</exception>
    ISpeechRecognition Start(RecognitionOptions? options = null);
}

/// <summary>How an utterance is written down.</summary>
public sealed class RecognitionOptions
{
    /// <summary>BCP-47, such as <c>sv-SE</c>. Unset, the device's language.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Only on the device: the audio never leaves it, and a language without an on-device model fails to start. Off by
    /// default, so the platform uses its server when it must (Apple's has limits per device and day).
    /// </summary>
    public bool OnDeviceOnly { get; init; }

    /// <summary>The rate of the PCM16 given to <see cref="ISpeechRecognition.Append"/>. 24 kHz by default, Spine.Voice's.</summary>
    public int SampleRate { get; init; } = 24_000;

    /// <summary>Words the speaker may use (names, terms), which the recognizer then prefers.</summary>
    public IReadOnlyList<string> Phrases { get; init; } = [];
}

/// <summary>One utterance being written down.</summary>
public interface ISpeechRecognition : IDisposable
{
    /// <summary>What has been heard so far, as the recognizer revises it. Raised on any thread.</summary>
    event Action<string>? Partial;

    /// <summary>Adds audio: PCM16, little-endian mono at <see cref="RecognitionOptions.SampleRate"/>. Any thread, the audio thread too.</summary>
    void Append(ReadOnlySpan<byte> pcm16);

    /// <summary>Says the audio has ended and waits for the words; empty when nothing was said.</summary>
    /// <exception cref="SpeechRecognitionException">The recognizer failed.</exception>
    Task<string> FinishAsync(CancellationToken cancellationToken = default);

    /// <summary>Gives the utterance up; <see cref="FinishAsync"/> then answers empty.</summary>
    void Cancel();
}

/// <summary>The platform's recognizer failed.</summary>
/// <param name="message">What happened, with the platform's code.</param>
public sealed class SpeechRecognitionException(string message) : Exception(message);

internal sealed class UnsupportedSpeechRecognizer : ISpineSpeechRecognizer
{
    public bool IsSupported => false;

    public bool IsOnDevice(string? language = null) => false;

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public ISpeechRecognition Start(RecognitionOptions? options = null) =>
        throw new InvalidOperationException("Speech recognition is not supported on this platform yet; iOS and Mac Catalyst have it.");
}
