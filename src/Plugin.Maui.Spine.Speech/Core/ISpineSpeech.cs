namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// The platform's text-to-speech with what an avatar or a caption needs to follow it: the installed voices with their
/// quality, and for each utterance the moment it starts, each word as the voice reaches it, its position and its end.
/// </summary>
/// <remarks>
/// iOS and Mac Catalyst speak with <c>AVSpeechSynthesizer</c>, Android with <c>TextToSpeech</c> (words from API 26), Windows
/// with <c>SpeechSynthesizer</c> played through <c>MediaPlayer</c>. Utterances play one after another; events arrive on the
/// main thread.
/// </remarks>
/// <example>
/// <code>
/// var utterance = speech.Speak("Welcome back", new SpeakOptions { Language = "en-GB" });
/// utterance.Word += (_, word) => Highlight(word.Start, word.Length);
/// await utterance.Completed;
/// </code>
/// </example>
public interface ISpineSpeech
{
    /// <summary>Whether this platform can speak; when it cannot, every utterance ends at once as <see cref="SpeechEndReason.Failed"/>.</summary>
    bool IsSupported { get; }

    /// <summary>The installed voices, best first; only those for <paramref name="language"/> (BCP-47, or its language part) when given.</summary>
    Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default);

    /// <summary>Queues <paramref name="text"/> and returns its utterance at once; it starts when those before it end.</summary>
    SpeechUtterance Speak(string text, SpeakOptions? options = null);

    /// <summary>Stops the utterance speaking and drops those queued; each ends as <see cref="SpeechEndReason.Stopped"/>.</summary>
    void StopAll();
}

/// <summary>How an utterance is spoken.</summary>
public sealed class SpeakOptions
{
    /// <summary>BCP-47, such as <c>sv-SE</c>. Unset, the voice's or the device's language.</summary>
    public string? Language { get; init; }

    /// <summary>A voice from <see cref="ISpineSpeech.GetVoicesAsync"/>. Unset, the best installed voice for <see cref="Language"/>.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Speed, 1 the voice's normal pace, 0.5–2.</summary>
    public float Rate { get; init; } = 1;

    /// <summary>Pitch, 1 the voice's own, 0.5–2.</summary>
    public float Pitch { get; init; } = 1;

    /// <summary>Volume, 0–1.</summary>
    public float Volume { get; init; } = 1;
}

/// <summary>An installed voice.</summary>
/// <param name="Id">The platform's identifier, for <see cref="SpeakOptions.VoiceId"/>.</param>
/// <param name="Name">A name to show.</param>
/// <param name="Language">BCP-47.</param>
/// <param name="Quality">How natural it sounds, as the platform rates it.</param>
public sealed record SpeechVoice(string Id, string Name, string Language, SpeechVoiceQuality Quality);

/// <summary>How natural a voice sounds, as the platform rates it.</summary>
public enum SpeechVoiceQuality
{
    /// <summary>The compact voice a platform ships.</summary>
    Default,

    /// <summary>A larger, downloaded voice (Apple's Enhanced, Android's high quality).</summary>
    Enhanced,

    /// <summary>The best voices (Apple's Premium, Android's very high quality).</summary>
    Premium,
}

/// <summary>A word as the voice reaches it.</summary>
/// <param name="Start">Its first character in the utterance's text.</param>
/// <param name="Length">Its length in characters.</param>
/// <param name="At">The utterance's position when it was reached.</param>
public readonly record struct SpeechWord(int Start, int Length, TimeSpan At);

/// <summary>A mouth shape with its time, from a voice that reports them (a server voice, step S3 of the avatar plan).</summary>
/// <param name="Id">Azure Speech's viseme id, 0–21.</param>
/// <param name="At">Where in the utterance it starts.</param>
/// <param name="Duration">How long it lasts.</param>
public readonly record struct SpeechViseme(int Id, TimeSpan At, TimeSpan Duration);

/// <summary>Why an utterance ended.</summary>
public enum SpeechEndReason
{
    /// <summary>Spoken to the end.</summary>
    Completed,

    /// <summary>Stopped before the end, or dropped from the queue.</summary>
    Stopped,

    /// <summary>The platform could not speak it.</summary>
    Failed,
}
