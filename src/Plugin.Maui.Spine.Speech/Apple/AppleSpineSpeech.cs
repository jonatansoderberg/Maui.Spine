#if IOS || MACCATALYST
using AVFoundation;
using Foundation;

namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// <c>AVSpeechSynthesizer</c>, used directly: MAUI's TextToSpeech picks the first voice for a language, which is the compact
/// one, while the downloadable Enhanced and Premium voices sound far more natural; and it reports each word as it is spoken.
/// </summary>
internal sealed class AppleSpineSpeech() : QueuedSpeech(MainThread.BeginInvokeOnMainThread)
{
    private readonly Synthesizer _synthesizer = new();

    public override bool IsSupported => true;

    public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(SpeechVoices.For(Voices(), language));

    protected override void StartNative(SpeechUtterance utterance)
    {
        var options = utterance.Options;
        var voice = SpeechVoices.Pick(Voices(), options) is { } picked ? AVSpeechSynthesisVoice.FromIdentifier(picked.Id)
            : options.Language is { } language ? AVSpeechSynthesisVoice.FromLanguage(language)
            : null;
        var native = new AVSpeechUtterance(utterance.Text)
        {
            Voice = voice,
            // AVSpeechUtteranceDefaultSpeechRate is 0.5; Rate scales it.
            Rate = Math.Clamp(0.5f * options.Rate, AVSpeechUtterance.MinimumSpeechRate, AVSpeechUtterance.MaximumSpeechRate),
            PitchMultiplier = Math.Clamp(options.Pitch, 0.5f, 2f),
            Volume = Math.Clamp(options.Volume, 0f, 1f),
        };
        _synthesizer.Start(native, utterance, Finished);
    }

    protected override void StopNative(SpeechUtterance utterance) => _synthesizer.Stop();

    private static IEnumerable<SpeechVoice> Voices() =>
        // Without the novelty voices (Bad News, Bells, Boing…), which would otherwise lead an alphabetical list. Quality by
        // value: Premium (3) is named only from iOS 16, but older systems simply never report it.
        AVSpeechSynthesisVoice.GetSpeechVoices()
            .Where(v => !v.Identifier.StartsWith("com.apple.speech.synthesis.voice.", StringComparison.Ordinal))
            .Select(v => new SpeechVoice(v.Identifier, v.Name, v.Language, (long)v.Quality switch
        {
            3 => SpeechVoiceQuality.Premium,
            2 => SpeechVoiceQuality.Enhanced,
            _ => SpeechVoiceQuality.Default,
        }));

    // The delegate: one native utterance at a time, mapped back to Spine's.
    private sealed class Synthesizer : NSObject, IAVSpeechSynthesizerDelegate
    {
        private readonly AVSpeechSynthesizer _native = new();
        private AVSpeechUtterance? _speaking;
        private SpeechUtterance? _utterance;
        private Action<SpeechUtterance, SpeechEndReason>? _finished;
        private bool _stopping;
        private bool _sessionOurs;

        public Synthesizer() => _native.Delegate = this;

        // Spoken audio, as Spine.Voice plays a server's voice: heard with the Ring/Silent switch on silent (the synthesizer's
        // own default follows the switch, so the voice was mute while the mouth still moved) and the system pauses a podcast
        // rather than mixing over it. A session that records (Spine.Voice listening) is left as it is.
        private void BeginSession()
        {
            var session = AVAudioSession.SharedInstance();
            if (session.Category == AVAudioSessionCategory.PlayAndRecord.GetConstant() || session.Category == AVAudioSessionCategory.Record.GetConstant())
                return;
            if (session.SetCategory(AVAudioSessionCategory.Playback.GetConstant()!, AVAudioSessionMode.SpokenAudio.GetConstant()!, default, out _)
                && session.SetActive(true, out _))
                _sessionOurs = true;
        }

        private void EndSession()
        {
            if (!_sessionOurs)
                return;
            _sessionOurs = false;
            // Off the main thread: deactivating waits for the audio to stop. Others (a paused podcast) are told to resume.
            _ = Task.Run(() => AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _));
        }

        public void Start(AVSpeechUtterance native, SpeechUtterance utterance, Action<SpeechUtterance, SpeechEndReason> finished)
        {
            _speaking = native;
            _utterance = utterance;
            _finished = finished;
            _stopping = false;
            BeginSession();
            _native.SpeakUtterance(native);
        }

        public void Stop()
        {
            _stopping = true;
            _native.StopSpeaking(AVSpeechBoundary.Immediate);
        }

        private SpeechUtterance? For(AVSpeechUtterance native) => native == _speaking ? _utterance : null;

        private void End(AVSpeechUtterance native, SpeechEndReason reason)
        {
            if (For(native) is not { } utterance)
                return;
            _speaking = null;
            _utterance = null;
            if (!_native.Speaking)
                EndSession();
            // A stop can arrive as didFinish rather than didCancel (seen on the iOS 26 simulator).
            _finished?.Invoke(utterance, _stopping ? SpeechEndReason.Stopped : reason);
        }

        [Export("speechSynthesizer:didStartSpeechUtterance:")]
        public void DidStartSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => For(utterance)?.OnStarted();

        [Export("speechSynthesizer:willSpeakRangeOfSpeechString:utterance:")]
        public void WillSpeakRangeOfSpeechString(AVSpeechSynthesizer synthesizer, NSRange characterRange, AVSpeechUtterance utterance) =>
            For(utterance)?.OnWord((int)characterRange.Location, (int)characterRange.Length);

        [Export("speechSynthesizer:didFinishSpeechUtterance:")]
        public void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => End(utterance, SpeechEndReason.Completed);

        [Export("speechSynthesizer:didCancelSpeechUtterance:")]
        public void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => End(utterance, SpeechEndReason.Stopped);
    }
}
#endif
