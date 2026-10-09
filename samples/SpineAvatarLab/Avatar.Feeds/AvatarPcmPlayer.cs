using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar.Core;

namespace Plugin.Maui.Spine.Controls.Avatar.Feeds;

/// <summary>
/// The lab's minimal PCM player with a playback clock, so cues can follow what is heard rather than
/// wall time. Throwaway: Spine.Voice's audio engine (#515) replaces it.
/// </summary>
/// <remarks>
/// iOS and Mac Catalyst: <c>AVAudioEngine</c> + <c>AVAudioPlayerNode</c>; the position is the node's
/// render time, so the reported output latency is subtracted once. Android: <c>AudioTrack</c> in static
/// mode; <c>getTimestamp</c> gives presentation time. Elsewhere: a silent stopwatch clock, reported as such.
/// </remarks>
public sealed partial class AvatarPcmPlayer : IAvatarPlaybackClock, IDisposable
{
    private long _generation;
    private double _duration;

    public long Generation => Interlocked.Read(ref _generation);

    public TimeSpan Duration => TimeSpan.FromSeconds(_duration);

    /// <summary>What kind of clock this platform gives, for the lab's diagnostics.</summary>
    public static string ClockDescription => PlatformClockDescription;

    /// <summary>Starts <paramref name="pcm"/> from the beginning in a new generation, dropping anything playing.</summary>
    public long Play(AvatarPcm pcm)
    {
        Stop();
        _duration = pcm.Duration.TotalSeconds;
        var generation = Interlocked.Increment(ref _generation);
        PlatformPlay(pcm);
        return generation;
    }

    public void Pause() => PlatformPause();

    public void Resume() => PlatformResume();

    /// <summary>Stops and flushes: the generation moves on, so every cue of the old one is invalid.</summary>
    public void Stop()
    {
        PlatformStop();
        Interlocked.Increment(ref _generation);
    }

    public AvatarPlaybackSnapshot GetSnapshot() => PlatformSnapshot();

    public void Dispose() => PlatformDispose();
}

#if IOS || MACCATALYST
public sealed partial class AvatarPcmPlayer
{
    private const string PlatformClockDescription = "AVAudioPlayerNode render time − output latency";

    private readonly AVFoundation.AVAudioEngine _engine = new();
    private readonly AVFoundation.AVAudioPlayerNode _node = new();
    private bool _attached;
    private bool _paused;
    private double _lastPosition;

    private void PlatformPlay(AvatarPcm pcm)
    {
        var session = AVFoundation.AVAudioSession.SharedInstance();
        session.SetCategory(AVFoundation.AVAudioSessionCategory.Playback);
        session.SetActive(true);

        var format = new AVFoundation.AVAudioFormat(AVFoundation.AVAudioCommonFormat.PCMFloat32, pcm.SampleRate, 1, false);
        if (!_attached)
        {
            _engine.AttachNode(_node);
            _attached = true;
        }
        _engine.Connect(_node, _engine.MainMixerNode, format);
        _engine.Prepare();
        if (!_engine.StartAndReturnError(out var error))
            throw new InvalidOperationException($"AVAudioEngine did not start: {error?.LocalizedDescription}");

        var buffer = new AVFoundation.AVAudioPcmBuffer(format, (uint)pcm.Samples.Length) { FrameLength = (uint)pcm.Samples.Length };
        var floats = new float[pcm.Samples.Length];
        for (var i = 0; i < floats.Length; i++)
            floats[i] = pcm.Samples[i] / 32768f;
        var channel = System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer.FloatChannelData);
        System.Runtime.InteropServices.Marshal.Copy(floats, 0, channel, floats.Length);

        _node.ScheduleBuffer(buffer, () => { });
        _node.Play();
        _paused = false;
    }

    private void PlatformPause()
    {
        _lastPosition = RenderPosition() ?? _lastPosition;
        _node.Pause();
        _paused = true;
    }

    private void PlatformResume()
    {
        _node.Play();
        _paused = false;
    }

    private void PlatformStop()
    {
        if (_attached)
            _node.Stop();
        _paused = false;
        _lastPosition = 0;
    }

    // A paused or stopped node has no render time; the position it held is kept instead.
    private double? RenderPosition() =>
        _node.LastRenderTime is { } renderTime && _node.GetPlayerTimeFromNodeTime(renderTime) is { SampleTimeValid: true } playerTime
            ? playerTime.SampleTime / playerTime.SampleRate
            : null;

    private AvatarPlaybackSnapshot PlatformSnapshot()
    {
        var position = _paused ? _lastPosition : RenderPosition() ?? _lastPosition;
        _lastPosition = position;

        var session = AVFoundation.AVAudioSession.SharedInstance();
        var latency = session.OutputLatency + session.IOBufferDuration;
        var playing = _node.Playing && !_paused && position < _duration + latency;
        return new AvatarPlaybackSnapshot(Generation, TimeSpan.FromSeconds(position), playing,
            PositionIsPresentationTime: false, TimeSpan.FromSeconds(latency), TimeSpan.FromSeconds(session.IOBufferDuration), Stopwatch.GetTimestamp());
    }

    private void PlatformDispose()
    {
        PlatformStop();
        _engine.Stop();
        _node.Dispose();
        _engine.Dispose();
    }
}
#elif ANDROID
public sealed partial class AvatarPcmPlayer
{
    private const string PlatformClockDescription = "AudioTrack.getTimestamp (presentation time)";

    private Android.Media.AudioTrack? _track;
    private readonly Android.Media.AudioTimestamp _timestamp = new();
    private int _sampleRate = 1;
    private bool _paused;
    private double _pausedAt;

    private void PlatformPlay(AvatarPcm pcm)
    {
        _sampleRate = pcm.SampleRate;
        var bytes = new byte[pcm.Samples.Length * 2];
        Buffer.BlockCopy(pcm.Samples, 0, bytes, 0, bytes.Length);

        _track = new Android.Media.AudioTrack.Builder()
            .SetAudioAttributes(new Android.Media.AudioAttributes.Builder()
                .SetUsage(Android.Media.AudioUsageKind.Media)!
                .SetContentType(Android.Media.AudioContentType.Speech)!
                .Build()!)
            .SetAudioFormat(new Android.Media.AudioFormat.Builder()
                .SetEncoding(Android.Media.Encoding.Pcm16bit)!
                .SetSampleRate(pcm.SampleRate)!
                .SetChannelMask(Android.Media.ChannelOut.Mono)!
                .Build()!)
            .SetTransferMode(Android.Media.AudioTrackMode.Static)
            .SetBufferSizeInBytes(bytes.Length)
            .Build();
        _track.Write(bytes, 0, bytes.Length);
        _track.Play();
        _paused = false;
    }

    private void PlatformPause()
    {
        if (_track is null)
            return;
        _pausedAt = Position();
        _track.Pause();
        _paused = true;
    }

    private void PlatformResume()
    {
        _track?.Play();
        _paused = false;
    }

    private void PlatformStop()
    {
        if (_track is null)
            return;
        _track.Stop();
        _track.Release();
        _track.Dispose();
        _track = null;
        _paused = false;
    }

    private double Position()
    {
        if (_track is null)
            return 0;
        if (_track.GetTimestamp(_timestamp))
        {
            // Frame position at a known instant, carried forward to now: what is coming out of the speaker.
            var since = (Java.Lang.JavaSystem.NanoTime() - _timestamp.NanoTime) / 1e9;
            return _timestamp.FramePosition / (double)_sampleRate + Math.Max(0, since);
        }
        return _track.PlaybackHeadPosition / (double)_sampleRate;
    }

    private AvatarPlaybackSnapshot PlatformSnapshot()
    {
        var position = _paused ? _pausedAt : Math.Min(Position(), _duration);
        var playing = _track is not null && !_paused && position < _duration;
        return new AvatarPlaybackSnapshot(Generation, TimeSpan.FromSeconds(position), playing,
            PositionIsPresentationTime: true, TimeSpan.Zero, TimeSpan.FromMilliseconds(5), Stopwatch.GetTimestamp());
    }

    private void PlatformDispose()
    {
        PlatformStop();
        _timestamp.Dispose();
    }
}
#else
public sealed partial class AvatarPcmPlayer
{
    private const string PlatformClockDescription = "silent stopwatch (no audio output on this platform)";

    private readonly Stopwatch _clock = new();

    private void PlatformPlay(AvatarPcm pcm) => _clock.Restart();

    private void PlatformPause() => _clock.Stop();

    private void PlatformResume() => _clock.Start();

    private void PlatformStop() => _clock.Reset();

    private AvatarPlaybackSnapshot PlatformSnapshot()
    {
        var position = Math.Min(_clock.Elapsed.TotalSeconds, _duration);
        return new AvatarPlaybackSnapshot(Generation, TimeSpan.FromSeconds(position), _clock.IsRunning && position < _duration,
            PositionIsPresentationTime: true, TimeSpan.Zero, TimeSpan.FromMilliseconds(15), Stopwatch.GetTimestamp());
    }

    private void PlatformDispose() => _clock.Stop();
}
#endif
