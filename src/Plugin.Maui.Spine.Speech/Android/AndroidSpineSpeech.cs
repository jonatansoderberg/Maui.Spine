#if ANDROID
using Android.OS;
using Android.Speech.Tts;
using TextToSpeech = Android.Speech.Tts.TextToSpeech;

namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// Android's <c>TextToSpeech</c>. Words come from <c>UtteranceProgressListener.onRangeStart</c>, which engines call as the
/// audio reaches each range (Android 8 and later, and only engines that implement it, such as Google's).
/// </summary>
internal sealed class AndroidSpineSpeech : QueuedSpeech
{
    private readonly Lazy<Task<TextToSpeech?>> _engine;
    private SpeechUtterance? _speaking;
    private string? _speakingId;
    private int _nextId;

    public AndroidSpineSpeech() : base(MainThread.BeginInvokeOnMainThread) => _engine = new(CreateAsync);

    public override bool IsSupported => true;

    public override async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
        await _engine.Value.WaitAsync(cancellationToken) is { } tts ? SpeechVoices.For(Voices(tts), language) : [];

    protected override void StartNative(SpeechUtterance utterance) => _ = StartAsync(utterance);

    protected override void StopNative(SpeechUtterance utterance)
    {
        if (_engine.Value.IsCompletedSuccessfully && _engine.Value.Result is { } tts)
            tts.Stop();
        // TextToSpeech.stop() reports onStop only on some engines; the end is reported here once.
        End(utterance, SpeechEndReason.Stopped);
    }

    private async Task StartAsync(SpeechUtterance utterance)
    {
        if (await _engine.Value is not { } tts)
        {
            Finished(utterance, SpeechEndReason.Failed);
            return;
        }

        var options = utterance.Options;
        if (SpeechVoices.Pick(Voices(tts), options) is { } picked && tts.Voices?.FirstOrDefault(v => v.Name == picked.Id) is { } voice)
            tts.SetVoice(voice);
        else if (options.Language is { } language)
            tts.SetLanguage(Java.Util.Locale.ForLanguageTag(language));
        tts.SetSpeechRate(Math.Clamp(options.Rate, 0.5f, 2f));
        tts.SetPitch(Math.Clamp(options.Pitch, 0.5f, 2f));

        var id = $"spine-{Interlocked.Increment(ref _nextId)}";
        _speaking = utterance;
        _speakingId = id;
        var parameters = new Bundle();
        parameters.PutFloat(TextToSpeech.Engine.KeyParamVolume, Math.Clamp(options.Volume, 0f, 1f));
        if (tts.Speak(utterance.Text, QueueMode.Flush, parameters, id) != OperationResult.Success)
            End(utterance, SpeechEndReason.Failed);
    }

    private void End(SpeechUtterance utterance, SpeechEndReason reason)
    {
        if (_speaking != utterance)
            return;
        _speaking = null;
        _speakingId = null;
        Finished(utterance, reason);
    }

    private SpeechUtterance? For(string? id) => id is not null && id == _speakingId ? _speaking : null;

    private static IEnumerable<SpeechVoice> Voices(TextToSpeech tts) =>
        (tts.Voices ?? []).Where(v => !v.IsNetworkConnectionRequired && v.Locale is not null && v.Name is not null).Select(v => new SpeechVoice(
            v.Name!, v.Name!, v.Locale!.ToLanguageTag(),
            (int)v.Quality >= 500 ? SpeechVoiceQuality.Premium : (int)v.Quality >= 400 ? SpeechVoiceQuality.Enhanced : SpeechVoiceQuality.Default));

    private async Task<TextToSpeech?> CreateAsync()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tts = new TextToSpeech(Android.App.Application.Context, new InitListener(status => ready.TrySetResult(status == OperationResult.Success)));
        if (!await ready.Task)
            return null;
        tts.SetOnUtteranceProgressListener(new ProgressListener(this));
        return tts;
    }

    private sealed class InitListener(Action<OperationResult> done) : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status) => done(status);
    }

    private sealed class ProgressListener(AndroidSpineSpeech owner) : UtteranceProgressListener
    {
        public override void OnStart(string? utteranceId) => owner.For(utteranceId)?.OnStarted();

        public override void OnRangeStart(string? utteranceId, int start, int end, int frame) => owner.For(utteranceId)?.OnWord(start, end - start);

        public override void OnDone(string? utteranceId)
        {
            if (owner.For(utteranceId) is { } utterance)
                owner.End(utterance, SpeechEndReason.Completed);
        }

        [Obsolete("Superseded by OnError(string, TextToSpeechError); engines still call it.")]
        public override void OnError(string? utteranceId)
        {
            if (owner.For(utteranceId) is { } utterance)
                owner.End(utterance, SpeechEndReason.Failed);
        }

        public override void OnError(string? utteranceId, TextToSpeechError errorCode)
        {
            if (owner.For(utteranceId) is { } utterance)
                owner.End(utterance, SpeechEndReason.Failed);
        }

        public override void OnStop(string? utteranceId, bool interrupted)
        {
            if (owner.For(utteranceId) is { } utterance)
                owner.End(utterance, SpeechEndReason.Stopped);
        }
    }
}
#endif
