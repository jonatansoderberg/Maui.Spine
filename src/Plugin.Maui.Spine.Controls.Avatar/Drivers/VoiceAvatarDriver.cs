using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Voice;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Drives an avatar from a realtime voice conversation: the state 1:1 (the seven values are the same), the microphone's
/// and the answer's levels and 24 bands each tick, and the mouth from the session's visemes on the answer's playback clock.
/// The sound in and out of a conversation is drawn this way: a character, or the built-in bars and ring
/// (<c>&lt;AvatarView Source="bars" /&gt;</c>).
/// </summary>
/// <remarks>
/// <para>Quality TimedVisemes once the session reports visemes (<c>SpineVoiceOptions.VisemeSource</c>), AudioReactive until
/// then: the realtime providers send none, so the mouth opens with the answer's level (aa, rounder O or wider E by where
/// its energy sits).</para>
/// <para>The view's <c>Interrupt()</c> interrupts the session (or flushes a bare engine's playback).</para>
/// </remarks>
/// <example>
/// <code>
/// avatar.Driver = new VoiceAvatarDriver(voiceSession);   // or new VoiceAvatarDriver(audioEngine)
/// </code>
/// </example>
public sealed class VoiceAvatarDriver : AvatarDriver, IAvatarPlaybackClock
{
    private static readonly TimeSpan MinimumCue = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan CueOverlap = TimeSpan.FromMilliseconds(30);
    private const float MouthFloor = 0.06f;
    private const int Aa = 10, E = 11, O = 13;

    private readonly IVoiceSession? _session;
    private readonly IVoiceAudio? _audio;
    private VoiceState _state = VoiceState.Idle;
    private string? _item;
    private TimeSpan _played;
    private long _generation;
    private bool _sawViseme;

    /// <summary>Follows <paramref name="session"/>: its state, levels, transcript-side visemes and playback.</summary>
    public VoiceAvatarDriver(IVoiceSession session)
    {
        _session = session;
        _audio = (session as VoiceSession)?.Audio;
        LipSyncQuality = AvatarLipSyncQuality.AudioReactive;
    }

    /// <summary>Follows a bare engine, for a page that drives the audio itself (Listening with the microphone's level).</summary>
    public VoiceAvatarDriver(IVoiceAudio audio)
    {
        _audio = audio;
        LipSyncQuality = AvatarLipSyncQuality.AudioReactive;
    }


    AvatarPlaybackSnapshot IAvatarPlaybackClock.GetSnapshot() =>
        new(_generation, _played, _audio?.IsPlaying ?? _state == VoiceState.Speaking, PositionIsPresentationTime: true,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(20), Stopwatch.GetTimestamp());

    /// <inheritdoc />
    protected internal override void OnAttached()
    {
        Input.PlaybackClock = this;
        Input.ResetSpeech(++_generation);
        _state = VoiceState.Idle;
    }

    /// <inheritdoc />
    protected internal override void OnDetached()
    {
        Input.ResetSpeech(++_generation);
        Input.SetInputLevel(0);
        Input.SetOutputLevel(0);
        if (ReferenceEquals(Input.PlaybackClock, this))
            Input.PlaybackClock = null;
    }

    /// <inheritdoc />
    protected internal override void OnInterrupted()
    {
        if (_session is not null)
            _session.Interrupt();
        else
            _audio?.Flush();
    }

    /// <inheritdoc />
    protected internal override void OnTick(TimeSpan delta)
    {
        var frame = _session?.UpdateFrame() ?? _audio?.UpdateFrame();
        if (frame is null)
            return;

        if (frame.State != _state)
        {
            _state = frame.State;
            if (_state == VoiceState.Interrupted)
                Input.Interrupt();
            else
                Input.SetState((AvatarState)_state);
        }
        Input.SetMuted(_session?.IsMuted ?? _audio?.IsMuted ?? false);
        Input.SetInputLevel(frame.InputLevel, frame.InputBands);
        Input.SetOutputLevel(frame.OutputLevel, frame.OutputBands);

        // The answer's clock: a new item or a flush starts a new generation, so cues of a cut-off answer go.
        if (_audio?.Playing is { } playing)
        {
            if (playing.ItemId != _item || playing.Played < _played)
            {
                _item = playing.ItemId;
                Input.ResetSpeech(++_generation);
            }
            _played = playing.Played;
        }
        else if (_audio is null && _state == VoiceState.Speaking)
            _played += delta;   // no engine to ask: the driver's own clock, running while the answer plays

        var viseme = AvatarVisemes.FromAzure(frame.Viseme is >= 0 and < 22 ? frame.Viseme : -1);
        if (viseme >= 0)
        {
            if (!_sawViseme)
            {
                _sawViseme = true;
                LipSyncQuality = AvatarLipSyncQuality.TimedVisemes;
            }
            // The session says which shape is under the playhead now; it lasts until the next tick says otherwise.
            var length = (delta > MinimumCue ? delta : MinimumCue) + CueOverlap;
            Input.EnqueueViseme(_generation, _played, length, viseme);
        }
        else if (!_sawViseme && _state == VoiceState.Speaking && frame.OutputLevel >= MouthFloor)
            LevelMouth(frame, delta);
    }

    // AudioReactive: no shape is known, so the mouth opens with the answer's level, rounder when its energy sits low and
    // wider when it sits high. Without it the answer only moved the body, and the face looked as if someone else spoke.
    private void LevelMouth(VoiceFrame frame, TimeSpan delta)
    {
        var bands = frame.OutputBands;
        float low = 0, high = 0;
        for (var i = 0; i < bands.Length; i++)
        {
            if (i < bands.Length / 3)
                low += bands[i];
            else if (i >= bands.Length * 2 / 3)
                high += bands[i];
        }
        var total = low + high;
        var shape = total <= 0 ? Aa : low > 2 * high ? O : high > low ? E : Aa;
        var length = (delta > MinimumCue ? delta : MinimumCue) + CueOverlap;
        Input.EnqueueViseme(_generation, _played, length, shape, Math.Clamp(frame.OutputLevel * 1.4f, 0.25f, 1f));
    }
}
