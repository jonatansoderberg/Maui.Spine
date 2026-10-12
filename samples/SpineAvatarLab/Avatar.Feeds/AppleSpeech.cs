#if IOS || MACCATALYST
using AVFoundation;
using Foundation;

namespace Plugin.Maui.Spine.Controls.Avatar.Feeds;

/// <summary>
/// The platform's own speech synthesiser, used directly: MAUI's TextToSpeech picks the first voice for
/// a language, which is the compact one, while the downloadable Enhanced and Premium voices sound far
/// more natural. It also reports each word as it is spoken, which keeps the lip sync on the voice.
/// </summary>
internal sealed class AppleSpeech : NSObject, IAVSpeechSynthesizerDelegate
{
    private readonly AVSpeechSynthesizer _synthesizer = new();
    private TaskCompletionSource? _done;
    private Action? _started;
    private Action<int, int>? _word;

    public AppleSpeech() => _synthesizer.Delegate = this;

    /// <summary>The best installed voice for <paramref name="language"/> (BCP-47): Premium, then Enhanced, then the default.</summary>
    public static AVSpeechSynthesisVoice? BestVoice(string language)
    {
        var voices = AVSpeechSynthesisVoice.GetSpeechVoices();
        var prefix = language.Split('-')[0];
        return voices.Where(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(v => (long)v.Quality).FirstOrDefault()
            ?? voices.Where(v => v.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(v => (long)v.Quality).FirstOrDefault();
    }

    /// <summary>Speaks <paramref name="text"/>; <paramref name="started"/> when the audio starts, <paramref name="word"/> (offset, length) as each word starts.</summary>
    public async Task SpeakAsync(string text, AVSpeechSynthesisVoice? voice, float rate, float pitch, Action started, Action<int, int> word, CancellationToken cancellationToken)
    {
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _started = started;
        _word = word;
        var utterance = new AVSpeechUtterance(text)
        {
            Voice = voice,
            // AVSpeechUtteranceDefaultSpeechRate is 0.5; the request's rate scales it.
            Rate = Math.Clamp(0.5f * rate, AVSpeechUtterance.MinimumSpeechRate, AVSpeechUtterance.MaximumSpeechRate),
            PitchMultiplier = Math.Clamp(pitch, 0.5f, 2f),
        };
        using var registration = cancellationToken.Register(() => _synthesizer.StopSpeaking(AVSpeechBoundary.Immediate));
        _synthesizer.SpeakUtterance(utterance);
        await _done.Task;
        cancellationToken.ThrowIfCancellationRequested();
    }

    [Export("speechSynthesizer:didStartSpeechUtterance:")]
    public void DidStartSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => _started?.Invoke();

    [Export("speechSynthesizer:willSpeakRangeOfSpeechString:utterance:")]
    public void WillSpeakRangeOfSpeechString(AVSpeechSynthesizer synthesizer, NSRange characterRange, AVSpeechUtterance utterance) =>
        _word?.Invoke((int)characterRange.Location, (int)characterRange.Length);

    [Export("speechSynthesizer:didFinishSpeechUtterance:")]
    public void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => _done?.TrySetResult();

    [Export("speechSynthesizer:didCancelSpeechUtterance:")]
    public void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => _done?.TrySetResult();
}
#endif
