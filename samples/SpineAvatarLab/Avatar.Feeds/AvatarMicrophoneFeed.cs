using Plugin.Maui.Spine.Controls.Avatar.Core;

namespace Plugin.Maui.Spine.Controls.Avatar.Feeds;

/// <summary>
/// The microphone as input level and bands for the Listening state. The audio thread meters each
/// buffer; the UI thread hands the latest values to the scheduler once a frame. Lab-only, like the
/// player: Spine.Voice's engine (#515) owns the microphone in the product, with echo cancellation.
/// </summary>
public sealed partial class AvatarMicrophoneFeed : IDisposable
{
    private readonly Lock _gate = new();
    private readonly float[] _bands = new float[AvatarRenderFrame.BandCount];
    private readonly float[] _latestBands = new float[AvatarRenderFrame.BandCount];
    private AvatarLevelMeter? _meter;
    private float _latestLevel;
    private AvatarView? _view;

    public bool IsRunning => _view is not null;

    /// <summary>What the platform reported when the microphone could not start.</summary>
    public string? Error { get; private set; }

    public async Task<bool> StartAsync(AvatarView view)
    {
        if (IsRunning)
            return true;
        Error = null;
        if (await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
        {
            Error = "Microphone permission was not granted.";
            return false;
        }

        try
        {
            PlatformStart();
        }
        catch (Exception e)
        {
            Error = e.Message;
            return false;
        }

        _view = view;
        view.FrameRendered += OnFrame;
        view.State = AvatarState.Listening;
        return true;
    }

    public void Stop()
    {
        PlatformStop();
        if (_view is { } view)
        {
            view.FrameRendered -= OnFrame;
            if (view.State == AvatarState.Listening)
                view.State = AvatarState.Idle;
        }
        _view = null;
    }

    public void Dispose() => Stop();

    // Called on the audio thread with mono float samples.
    private void OnSamples(ReadOnlySpan<float> samples, int sampleRate)
    {
        _meter ??= new AvatarLevelMeter(sampleRate);
        _meter.Push(samples);
        var level = _meter.Analyze(_bands);
        lock (_gate)
        {
            _latestLevel = level;
            _bands.CopyTo(_latestBands, 0);
        }
    }

    private readonly float[] _frameBands = new float[AvatarRenderFrame.BandCount];

    private void OnFrame(object? sender, AvatarRenderFrame frame)
    {
        float level;
        lock (_gate)
        {
            level = _latestLevel;
            _latestBands.CopyTo(_frameBands, 0);
        }
        _view?.Scheduler?.SetInputLevel(level, _frameBands);

        // With the microphone on, being idle means listening: after a reply ends the avatar listens again.
        if (_view is { State: AvatarState.Idle } view)
            view.State = AvatarState.Listening;
    }
}

#if IOS || MACCATALYST
public sealed partial class AvatarMicrophoneFeed
{
    private AVFoundation.AVAudioEngine? _engine;
    private float[] _samples = [];

    private void PlatformStart()
    {
        var session = AVFoundation.AVAudioSession.SharedInstance();
        var error = session.SetCategory(AVFoundation.AVAudioSessionCategory.PlayAndRecord,
            AVFoundation.AVAudioSessionCategoryOptions.DefaultToSpeaker | AVFoundation.AVAudioSessionCategoryOptions.AllowBluetoothA2DP);
        if (error is not null)
            throw new InvalidOperationException($"Audio session: {error.LocalizedDescription}");
        session.SetActive(true);

        _engine = new AVFoundation.AVAudioEngine();
        var input = _engine.InputNode;
        var format = input.GetBusOutputFormat(0);
        if (format.SampleRate <= 0 || format.ChannelCount == 0)
            throw new InvalidOperationException("No microphone input is available.");
        var rate = (int)format.SampleRate;

        input.InstallTapOnBus(0, 1024, format, (buffer, _) =>
        {
            var frames = (int)buffer.FrameLength;
            if (frames == 0)
                return;
            if (_samples.Length < frames)
                _samples = new float[frames];
            var channel = System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer.FloatChannelData);
            System.Runtime.InteropServices.Marshal.Copy(channel, _samples, 0, frames);
            OnSamples(_samples.AsSpan(0, frames), rate);
        });

        _engine.Prepare();
        if (!_engine.StartAndReturnError(out var startError))
            throw new InvalidOperationException($"AVAudioEngine did not start: {startError?.LocalizedDescription}");
    }

    private void PlatformStop()
    {
        if (_engine is null)
            return;
        _engine.InputNode.RemoveTapOnBus(0);
        _engine.Stop();
        _engine.Dispose();
        _engine = null;
    }
}
#elif ANDROID
public sealed partial class AvatarMicrophoneFeed
{
    private const int SampleRate = 16000;
    private Android.Media.AudioRecord? _record;
    private CancellationTokenSource? _cancellation;

    private void PlatformStart()
    {
        var size = Math.Max(Android.Media.AudioRecord.GetMinBufferSize(SampleRate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit), 4096);
        _record = new Android.Media.AudioRecord(Android.Media.AudioSource.VoiceRecognition, SampleRate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, size);
        if (_record.State != Android.Media.State.Initialized)
            throw new InvalidOperationException("AudioRecord did not initialize.");
        _record.StartRecording();

        var record = _record;
        var cancellation = _cancellation = new CancellationTokenSource();
        new Thread(() =>
        {
            var pcm = new short[512];
            var samples = new float[512];
            while (!cancellation.IsCancellationRequested)
            {
                var read = record.Read(pcm, 0, pcm.Length);
                if (read <= 0)
                    continue;
                for (var i = 0; i < read; i++)
                    samples[i] = pcm[i] / 32768f;
                OnSamples(samples.AsSpan(0, read), SampleRate);
            }
        }) { IsBackground = true, Name = "Avatar microphone" }.Start();
    }

    private void PlatformStop()
    {
        _cancellation?.Cancel();
        _cancellation = null;
        if (_record is null)
            return;
        _record.Stop();
        _record.Release();
        _record = null;
    }
}
#else
public sealed partial class AvatarMicrophoneFeed
{
    private void PlatformStart() => throw new PlatformNotSupportedException("The lab's microphone feed runs on iOS, Mac Catalyst and Android.");

    private void PlatformStop() { }
}
#endif
