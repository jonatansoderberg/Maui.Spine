#if ANDROID
using Android.Content;
using Android.Media;
using Android.Media.Audiofx;
using Android.OS;
using Microsoft.Extensions.Logging;
using AndroidProcess = Android.OS.Process;
using Encoding = Android.Media.Encoding;
using ThreadPriority = Android.OS.ThreadPriority;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Microphone and player on Android: an <see cref="AudioRecord"/> with the voice-communication source and an
/// <see cref="AudioTrack"/> for voice communication on the same audio session, which is what puts the platform's echo
/// canceller between them, with <see cref="AcousticEchoCanceler"/> and <see cref="NoiseSuppressor"/> on top where the
/// device offers them. Each device runs on a thread of its own at the rate it opened with; the base resamples.
/// </summary>
internal sealed class AndroidVoiceAudio(ILogger? logger, TimeProvider? time) : VoiceAudioEngine(logger, time)
{
    private static readonly TimeSpan Chunk = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan TrackBuffer = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan JoinLimit = TimeSpan.FromSeconds(2);

    private AudioManager? _audio;
    private AudioRecord? _record;
    private AudioTrack? _track;
    private AcousticEchoCanceler? _echo;
    private NoiseSuppressor? _noise;
    private AudioFocusRequestClass? _focus;
    private FocusListener? _focusListener;
    private DeviceCallback? _devices;
    private Thread? _recorder;
    private Thread? _player;
    private int _recordRate;
    private Mode _modeBefore = Mode.Normal;
    private volatile bool _stopping;
    private volatile bool _flushTrack;
    private bool _speaker;

    public override bool IsSupported => true;

    public override bool UseSpeaker
    {
        get => _speaker;
        set
        {
            _speaker = value;
            if (IsRunning)
                ApplySpeaker();
        }
    }

    protected override async Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken)
    {
        var context = Platform.AppContext;
        _audio = context.GetSystemService(Context.AudioService) as AudioManager
            ?? throw new VoiceAudioException(VoiceAudioProblem.Failed, "Android gave no audio service.");

        if (options.Input)
        {
            if (!DeclaresMicrophone(context))
                throw new VoiceAudioException(VoiceAudioProblem.NotConfigured,
                    "Add <uses-permission android:name=\"android.permission.RECORD_AUDIO\" /> to AndroidManifest.xml to use the microphone.");
            if (await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>) != PermissionStatus.Granted)
                throw new VoiceAudioException(VoiceAudioProblem.PermissionDenied, "The user did not allow the microphone.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        _stopping = false;
        _flushTrack = false;
        _modeBefore = _audio.Mode;
        try
        {
            // The communication mode is what routes the microphone and the player through the device's own echo
            // canceller; a measuring run (voice processing off) and playback alone stay out of it. Setting it needs
            // MODIFY_AUDIO_SETTINGS, which the system otherwise refuses silently.
            if (options.Input && options.VoiceProcessing)
            {
                if (Declares(context, Android.Manifest.Permission.ModifyAudioSettings))
                    _audio.Mode = Mode.InCommunication;
                else
                    Logger.LogWarning("Spine.Voice: without MODIFY_AUDIO_SETTINGS in AndroidManifest.xml the communication mode, and with it the device's own echo canceller, stays off.");
            }
            RequestFocus(options);
            if (options.Input)
                OpenRecord(options);
            else
                IsVoiceProcessingOn = false;
            OpenTrack(options);
            ApplySpeaker();
            _devices = new DeviceCallback(this);
            _audio.RegisterAudioDeviceCallback(_devices, new Handler(Looper.MainLooper!));
            SetRoute(RouteName());

            _player = Start("Spine.Voice player", PlayLoop);
            if (_record is not null)
                _recorder = Start("Spine.Voice recorder", RecordLoop);
        }
        catch
        {
            Close();
            throw;
        }
    }

    protected override Task StopPlatformAsync() => Task.Run(Close);

    protected override void OnFlushed() => _flushTrack = true;

    private static bool DeclaresMicrophone(Context context) => Declares(context, Android.Manifest.Permission.RecordAudio);

    private static bool Declares(Context context, string permission)
    {
        try
        {
            var info = context.PackageManager!.GetPackageInfo(context.PackageName!, Android.Content.PM.PackageInfoFlags.Permissions);
            return info?.RequestedPermissions?.Contains(permission) == true;
        }
        catch (Java.Lang.Exception)
        {
            return true;
        }
    }

    private void OpenRecord(VoiceAudioOptions options)
    {
        // The provider's rate first, so nothing is resampled; otherwise the rates every device has.
        var source = options.VoiceProcessing ? AudioSource.VoiceCommunication : AudioSource.VoiceRecognition;
        foreach (var rate in new[] { options.InputRate, 16_000, 48_000, 44_100 }.Distinct())
        {
            var minimum = AudioRecord.GetMinBufferSize(rate, ChannelIn.Mono, Encoding.Pcm16bit);
            if (minimum <= 0)
                continue;

            AudioRecord record;
            try
            {
                record = new AudioRecord(source, rate, ChannelIn.Mono, Encoding.Pcm16bit, Math.Max(minimum, Bytes(rate, TimeSpan.FromMilliseconds(200))));
            }
            catch (Java.Lang.Exception)
            {
                continue;
            }

            if (record.State != Android.Media.State.Initialized)
            {
                record.Release();
                continue;
            }

            _record = record;
            _recordRate = rate;
            break;
        }

        if (_record is null)
            throw new VoiceAudioException(VoiceAudioProblem.Unavailable, "No microphone could be opened.");

        if (options.VoiceProcessing)
        {
            var session = _record.AudioSessionId;
            if (AcousticEchoCanceler.IsAvailable)
            {
                _echo = AcousticEchoCanceler.Create(session);
                _echo?.SetEnabled(true);
            }
            if (NoiseSuppressor.IsAvailable)
            {
                _noise = NoiseSuppressor.Create(session);
                _noise?.SetEnabled(true);
            }
        }

        // The communication source is echo-cancelled by the device's own audio path on most devices; the effect is the
        // part that can be asked about.
        IsVoiceProcessingOn = options.VoiceProcessing && (_echo?.Enabled == true || source == AudioSource.VoiceCommunication);
        Logger.LogInformation("Spine.Voice: microphone at {Rate} Hz, source {Source}, echo canceller {Echo}, noise suppressor {Noise}.",
            _recordRate, source, _echo?.Enabled == true ? "on" : "none", _noise?.Enabled == true ? "on" : "none");
    }

    private void OpenTrack(VoiceAudioOptions options)
    {
        var rate = options.OutputRate;
        var minimum = AudioTrack.GetMinBufferSize(rate, ChannelOut.Mono, Encoding.Pcm16bit);
        if (minimum <= 0)
            throw new VoiceAudioException(VoiceAudioProblem.Failed, $"Android cannot play {rate} Hz mono PCM16.");

        var format = new AudioFormat.Builder().SetSampleRate(rate)!.SetEncoding(Encoding.Pcm16bit)!.SetChannelMask(ChannelOut.Mono)!.Build()!;
        // A short buffer: what is in it still plays after a cut-off, over and above the flush.
        var builder = new AudioTrack.Builder()
            .SetAudioAttributes(Attributes(options))!
            .SetAudioFormat(format)!
            .SetBufferSizeInBytes(Math.Max(minimum, Bytes(rate, TrackBuffer)))!
            .SetTransferMode(AudioTrackMode.Stream)!;
        if (_record is not null)
            builder.SetSessionId(_record.AudioSessionId);
        var track = builder.Build()!;
        if (track.State != AudioTrackState.Initialized)
        {
            track.Release();
            throw new VoiceAudioException(VoiceAudioProblem.Failed, "The player did not open.");
        }
        _track = track;
    }

    private static AudioAttributes Attributes(VoiceAudioOptions options) => new AudioAttributes.Builder()
        .SetUsage(options.Input && options.VoiceProcessing ? AudioUsageKind.VoiceCommunication : AudioUsageKind.Media)!
        .SetContentType(AudioContentType.Speech)!
        .Build()!;

    private void RequestFocus(VoiceAudioOptions options)
    {
        _focusListener = new FocusListener(this);
        _focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(Attributes(options))!
            .SetOnAudioFocusChangeListener(_focusListener)!
            .Build()!;
        if (_audio!.RequestAudioFocus(_focus) != AudioFocusRequest.Granted)
            Logger.LogWarning("Spine.Voice: audio focus was not granted; the conversation goes on without it.");
    }

    private static Thread Start(string name, ThreadStart loop)
    {
        var thread = new Thread(loop) { Name = name, IsBackground = true };
        thread.Start();
        return thread;
    }

    private void RecordLoop()
    {
        AndroidProcess.SetThreadPriority(ThreadPriority.UrgentAudio);
        var record = _record!;
        var rate = _recordRate;
        var frames = Frames(rate, Chunk);
        var pcm = new short[frames];
        var samples = new float[frames];
        try
        {
            record.StartRecording();
            if (record.RecordingState != RecordState.Recording)
                throw new VoiceAudioException(VoiceAudioProblem.Failed, "The microphone did not start recording.");

            while (!_stopping)
            {
                var read = record.Read(pcm, 0, frames);
                if (read < 0)
                    throw new VoiceAudioException(VoiceAudioProblem.Failed, $"The microphone stopped delivering ({(RecordStatus)read}).");
                if (read == 0)
                    continue;

                for (var i = 0; i < read; i++)
                    samples[i] = pcm[i] / 32768f;
                OnCaptured(samples.AsSpan(0, read), rate);
            }
        }
        catch (Exception e) when (!_stopping)
        {
            Fail(e as VoiceAudioException ?? new VoiceAudioException(VoiceAudioProblem.Failed, $"The microphone failed: {e.Message}", e));
        }
    }

    private void PlayLoop()
    {
        AndroidProcess.SetThreadPriority(ThreadPriority.UrgentAudio);
        var track = _track!;
        var rate = Options.OutputRate;
        var frames = Frames(rate, Chunk);
        var samples = new float[frames];
        var pcm = new short[frames];
        try
        {
            track.Play();
            while (!_stopping)
            {
                if (_flushTrack)
                {
                    _flushTrack = false;
                    track.Pause();
                    track.Flush();
                    track.Play();
                }

                RenderOutput(samples, rate);
                for (var i = 0; i < frames; i++)
                    pcm[i] = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
                // Blocking: the write paces the loop at the device's speed.
                var written = track.Write(pcm, 0, frames);
                if (written < 0)
                    throw new VoiceAudioException(VoiceAudioProblem.Failed, $"The player stopped taking audio ({(TrackStatus)written}).");
            }
        }
        catch (Exception e) when (!_stopping)
        {
            Fail(e as VoiceAudioException ?? new VoiceAudioException(VoiceAudioProblem.Failed, $"The player failed: {e.Message}", e));
        }
    }

    private void Close()
    {
        _stopping = true;
        foreach (var thread in new[] { _recorder, _player })
        {
            if (thread is not null && thread != Thread.CurrentThread && !thread.Join(JoinLimit))
                Logger.LogWarning("Spine.Voice: the {Thread} thread did not stop in time.", thread.Name);
        }
        _recorder = null;
        _player = null;

        if (_record is { } record)
        {
            try
            {
                if (record.RecordingState == RecordState.Recording)
                    record.Stop();
            }
            catch (Java.Lang.Exception)
            {
            }
            record.Release();
        }
        _record = null;

        if (_track is { } track)
        {
            try
            {
                track.Pause();
                track.Flush();
                track.Stop();
            }
            catch (Java.Lang.Exception)
            {
            }
            track.Release();
        }
        _track = null;

        _echo?.Release();
        _echo = null;
        _noise?.Release();
        _noise = null;

        if (_audio is { } audio)
        {
            if (_devices is not null)
                audio.UnregisterAudioDeviceCallback(_devices);
            if (_focus is not null)
                audio.AbandonAudioFocusRequest(_focus);
            if (OperatingSystem.IsAndroidVersionAtLeast(31) && audio.Mode == Mode.InCommunication)
                audio.ClearCommunicationDevice();
            audio.Mode = _modeBefore;
        }
        _devices = null;
        _focus = null;
        _focusListener = null;
    }

    private void OnFocus(AudioFocus change)
    {
        switch (change)
        {
            case AudioFocus.Loss:
            case AudioFocus.LossTransient:
                SetInterrupted(true);
                break;
            case AudioFocus.Gain:
                SetInterrupted(false);
                break;
        }
    }

    private void ApplySpeaker()
    {
        if (!Options.Input || _audio is not { } audio)
            return;

        // Only meaningful on the built-in route: a headset or a Bluetooth device keeps the sound whatever this says.
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var devices = audio.AvailableCommunicationDevices;
            if (devices is null || devices.Any(d => d.Type is not (AudioDeviceType.BuiltinSpeaker or AudioDeviceType.BuiltinEarpiece or AudioDeviceType.Telephony)))
                return;
            var wanted = _speaker ? AudioDeviceType.BuiltinSpeaker : AudioDeviceType.BuiltinEarpiece;
            if (devices.FirstOrDefault(d => d.Type == wanted) is { } device)
                audio.SetCommunicationDevice(device);
            return;
        }

        if (audio.Mode == Mode.InCommunication)
#pragma warning disable CA1422
            audio.SpeakerphoneOn = _speaker;
#pragma warning restore CA1422
    }

    private string RouteName()
    {
        if (_audio is not { } audio)
            return "None";

        var device = OperatingSystem.IsAndroidVersionAtLeast(31) && audio.Mode == Mode.InCommunication ? audio.CommunicationDevice : null;
        device ??= audio.GetDevices(GetDevicesTargets.Outputs)?
            .OrderBy(d => d.Type is AudioDeviceType.BuiltinSpeaker or AudioDeviceType.BuiltinEarpiece or AudioDeviceType.Telephony ? 1 : 0)
            .FirstOrDefault();
        return device?.Type switch
        {
            null => "None",
            AudioDeviceType.BuiltinSpeaker or AudioDeviceType.BuiltinSpeakerSafe => "Speaker",
            AudioDeviceType.BuiltinEarpiece => "Receiver",
            AudioDeviceType.WiredHeadset or AudioDeviceType.WiredHeadphones or AudioDeviceType.UsbHeadset => "Headset",
            AudioDeviceType.Hdmi or AudioDeviceType.HdmiArc or AudioDeviceType.HdmiEarc => "HDMI",
            _ => device.ProductName?.ToString() is { Length: > 0 } name ? name : device.Type.ToString(),
        };
    }

    private static int Frames(int rate, TimeSpan length) => (int)(rate * length.TotalSeconds);

    private static int Bytes(int rate, TimeSpan length) => Frames(rate, length) * sizeof(short);

    private sealed class FocusListener(AndroidVoiceAudio owner) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange) => owner.OnFocus(focusChange);
    }

    private sealed class DeviceCallback(AndroidVoiceAudio owner) : AudioDeviceCallback
    {
        public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices) => Changed();

        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices) => Changed();

        private void Changed()
        {
            if (owner.IsRunning)
                owner.SetRoute(owner.RouteName());
        }
    }
}
#endif
