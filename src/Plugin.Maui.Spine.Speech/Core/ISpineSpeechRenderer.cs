namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// The platform's voices rendered to audio instead of played: what a voice conversation needs to play an answer through
/// its own engine, with its echo cancellation and its mouth on the sound, rather than through the system speaker.
/// </summary>
/// <remarks>
/// iOS and Mac Catalyst render with <c>AVSpeechSynthesizer.write(_:toBufferCallback:)</c>, with the same voices as
/// <see cref="ISpineSpeech"/>. Android and Windows are not supported yet.
/// </remarks>
public interface ISpineSpeechRenderer
{
    /// <summary>Whether this platform can render speech.</summary>
    bool IsSupported { get; }

    /// <summary>Renders <paramref name="text"/>, piece by piece as the voice makes it.</summary>
    /// <exception cref="InvalidOperationException">Not supported on this platform.</exception>
    IAsyncEnumerable<SpeechAudio> RenderAsync(string text, SpeakOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>A piece of rendered speech.</summary>
/// <param name="Pcm16">PCM16, little-endian mono.</param>
/// <param name="SampleRate">Its rate, the voice's own (22.05 kHz for most of Apple's).</param>
public readonly record struct SpeechAudio(ReadOnlyMemory<byte> Pcm16, int SampleRate);

internal sealed class UnsupportedSpeechRenderer : ISpineSpeechRenderer
{
    public bool IsSupported => false;

    public IAsyncEnumerable<SpeechAudio> RenderAsync(string text, SpeakOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Rendering speech is not supported on this platform yet; iOS and Mac Catalyst have it.");
}
