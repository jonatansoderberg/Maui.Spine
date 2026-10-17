#if WINDOWS
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Plugin.Maui.Spine.Speech;

/// <summary>
/// <c>SpeechSynthesizer</c> with word-boundary metadata, played through a <c>MediaPlayer</c>: the words arrive as the
/// player's timed-metadata cues at their playback time, and the player's position is the utterance's.
/// </summary>
internal sealed class WindowsSpineSpeech() : QueuedSpeech(MainThread.BeginInvokeOnMainThread)
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private MediaPlayer? _player;
    private SpeechUtterance? _speaking;

    public override bool IsSupported => true;

    public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(SpeechVoices.For(Voices(), language));

    protected override void StartNative(SpeechUtterance utterance) => _ = StartAsync(utterance);

    protected override void StopNative(SpeechUtterance utterance)
    {
        if (_speaking != utterance)
            return;
        _player?.Pause();
        End(utterance, SpeechEndReason.Stopped);
    }

    private async Task StartAsync(SpeechUtterance utterance)
    {
        try
        {
            var options = utterance.Options;
            if (SpeechVoices.Pick(Voices(), options) is { } picked && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == picked.Id) is { } voice)
                _synthesizer.Voice = voice;
            _synthesizer.Options.IncludeWordBoundaryMetadata = true;
            _synthesizer.Options.SpeakingRate = Math.Clamp(options.Rate, 0.5, 2);
            _synthesizer.Options.AudioPitch = Math.Clamp(options.Pitch, 0.5, 2);
            _synthesizer.Options.AudioVolume = Math.Clamp(options.Volume, 0, 1);

            var stream = await _synthesizer.SynthesizeTextToStreamAsync(utterance.Text);
            var item = new MediaPlaybackItem(MediaSource.CreateFromStream(stream, stream.ContentType));
            item.TimedMetadataTracksChanged += (sender, _) =>
            {
                for (var i = 0; i < sender.TimedMetadataTracks.Count; i++)
                {
                    var track = sender.TimedMetadataTracks[i];
                    if (track.Id != "SpeechWord")
                        continue;
                    sender.TimedMetadataTracks.SetPresentationMode((uint)i, TimedMetadataTrackPresentationMode.ApplicationPresented);
                    track.CueEntered += (_, e) =>
                    {
                        if (e.Cue is SpeechCue { StartPositionInInput: { } start, EndPositionInInput: { } end })
                            utterance.OnWord(start, end - start + 1);
                    };
                }
            };

            var player = _player ??= CreatePlayer();
            _speaking = utterance;
            utterance.PositionSource = () => player.PlaybackSession.Position;
            player.Source = item;
            player.Play();
            utterance.OnStarted();
        }
        catch (Exception)
        {
            End(utterance, SpeechEndReason.Failed);
        }
    }

    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer { AutoPlay = false };
        player.MediaEnded += (_, _) => { if (_speaking is { } u) End(u, SpeechEndReason.Completed); };
        player.MediaFailed += (_, _) => { if (_speaking is { } u) End(u, SpeechEndReason.Failed); };
        return player;
    }

    private void End(SpeechUtterance utterance, SpeechEndReason reason)
    {
        if (_speaking != utterance)
            return;
        _speaking = null;
        utterance.PositionSource = null;
        Finished(utterance, reason);
    }

    private static IEnumerable<SpeechVoice> Voices() =>
        SpeechSynthesizer.AllVoices.Select(v => new SpeechVoice(v.Id, v.DisplayName, v.Language,
            v.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase) ? SpeechVoiceQuality.Premium : SpeechVoiceQuality.Default));
}
#endif
