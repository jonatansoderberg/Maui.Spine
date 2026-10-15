#if IOS || MACCATALYST
using AudioToolbox;
using AVFoundation;
using Foundation;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Microphone and player through one <see cref="AVAudioEngine"/>, with voice processing on its input node. Apple's echo
/// canceller only knows what it cancels when the playback goes through the same engine, so the answer is rendered by
/// an <see cref="AVAudioSourceNode"/> in it rather than by a player of its own.
/// </summary>
internal sealed class AppleVoiceAudio(ILogger? logger, TimeProvider? time) : VoiceAudioEngine(logger, time)
{
    private const uint TapFrames = 1024;

    private AVAudioEngine? _engine;
    private AVAudioSourceNode? _source;
    private readonly List<NSObject> _observers = [];
    private NSObject? _configurationObserver;
    private float[] _render = new float[4096];
    private double _outputRate;
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
        // Asking without the usage string ends the app with no message; say what is missing instead.
        if (NSBundle.MainBundle.ObjectForInfoDictionary("NSMicrophoneUsageDescription") is null)
            throw new VoiceAudioException(VoiceAudioProblem.NotConfigured,
                "Add NSMicrophoneUsageDescription to Info.plist to use the microphone.");

        if (!await RequestPermissionAsync())
            throw new VoiceAudioException(VoiceAudioProblem.PermissionDenied, "The user did not allow the microphone.");
        cancellationToken.ThrowIfCancellationRequested();

        // Off the calling thread: activating a session can take hundreds of milliseconds (iOS warns of a hang).
        await Task.Run(() => ConfigureSession(options), cancellationToken);
        Build(options);

        // Notifications come on any thread, and an exception that leaves one ends the app: each is handled on the main
        // thread, one at a time, inside a guard.
        _observers.Add(AVAudioSession.Notifications.ObserveInterruption((_, args) =>
        {
            var began = args.InterruptionType == AVAudioSessionInterruptionType.Began;
            OnMain("an interruption", () => OnInterruption(began));
        }));
        _observers.Add(AVAudioSession.Notifications.ObserveRouteChange((_, _) => OnMain("a route change", OnRouteChange)));
        _observers.Add(AVAudioSession.Notifications.ObserveMediaServicesWereReset((_, _) => OnMain("a media services reset", () => Rebuild("media services were reset"))));
        SetRoute(RouteName(AVAudioSession.SharedInstance()));
    }

    protected override async Task StopPlatformAsync()
    {
        foreach (var observer in _observers)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            observer.Dispose();
        }
        _observers.Clear();
        TearDown();
        await Task.Run(() => AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _));
    }

    private static Task<bool> RequestPermissionAsync()
    {
        if (OperatingSystem.IsIOSVersionAtLeast(17) || OperatingSystem.IsMacCatalystVersionAtLeast(17))
            return AVAudioApplication.RequestRecordPermissionAsync();

        var done = new TaskCompletionSource<bool>();
        AVAudioSession.SharedInstance().RequestRecordPermission(granted => done.TrySetResult(granted));
        return done.Task;
    }

    private void ConfigureSession(VoiceAudioOptions options)
    {
        var session = AVAudioSession.SharedInstance();
        // No DefaultToSpeaker: it sends the sound to the loudspeaker whatever the override says, so the receiver could
        // never be chosen. The loudspeaker is the override instead (UseSpeaker).
        var categoryOptions = options.AllowBluetooth ? AVAudioSessionCategoryOptions.AllowBluetooth : default;

        // Set once before activation: changing the mode of an active session reroutes it mid-sentence. Voice chat mode
        // goes with voice processing; without it, the default mode leaves the microphone unprocessed, for measuring.
        var mode = options.VoiceProcessing ? AVAudioSessionMode.VoiceChat : AVAudioSessionMode.Default;
        Check(session.SetCategory(AVAudioSessionCategory.PlayAndRecord.GetConstant()!, mode.GetConstant()!, categoryOptions, out var error), error, "set the audio category");
        session.SetAllowHapticsAndSystemSoundsDuringRecording(true, out _);
        session.SetPreferredIOBufferDuration(0.01, out _);
        Check(session.SetActive(true, out error), error, "activate the audio session");
        ApplySpeaker();
    }

    private void Build(VoiceAudioOptions options)
    {
        if (TryBuild(options, options.VoiceProcessing, out var error))
            return;

        // Some Macs' devices refuse the voice-processing unit (kAudioUnitErr_FailedInitialization). The conversation still
        // runs, without echo cancellation, and says so: IsVoiceProcessingOn is false and the log names the reason.
        if (options.VoiceProcessing)
        {
            Logger.LogWarning("Spine.Voice: the engine did not start with voice processing ({Error}); starting without it.", error);
            if (TryBuild(options, voiceProcessing: false, out error))
                return;
        }

        throw new VoiceAudioException(VoiceAudioProblem.Failed, $"The audio engine did not start: {error}.");
    }

    private bool TryBuild(VoiceAudioOptions options, bool voiceProcessing, out string? error)
    {
        var engine = new AVAudioEngine();
        var input = engine.InputNode;

        // The mixer and output first: on macOS the voice-processing unit spans input and output, and fails to
        // initialise when the output side is not connected yet.
        var mixer = engine.MainMixerNode;
        var output = engine.OutputNode;

        if (voiceProcessing)
        {
            if (!input.SetVoiceProcessingEnabled(true, out var vpError))
                Logger.LogWarning("Spine.Voice: voice processing could not be turned on: {Error}.", vpError?.LocalizedDescription);
            else if (!output.VoiceProcessingEnabled)
                output.SetVoiceProcessingEnabled(true, out _);
        }
        IsVoiceProcessingOn = input.VoiceProcessingEnabled;

        // With voice processing the input node can report several channels; the first is the processed microphone.
        var inputFormat = input.GetBusOutputFormat(0);
        var inputRate = (int)inputFormat.SampleRate;
        input.InstallTapOnBus(0, TapFrames, inputFormat, (buffer, _) => OnTap(buffer, inputRate));

        _outputRate = mixer.GetBusOutputFormat(0).SampleRate;
        var outputFormat = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, _outputRate, 1, false);
        var source = new AVAudioSourceNode(outputFormat, Render);
        engine.AttachNode(source);
        engine.Connect(source, mixer, outputFormat);

        engine.Prepare();
        if (!engine.StartAndReturnError(out var startError))
        {
            error = startError?.LocalizedDescription;
            input.RemoveTapOnBus(0);
            engine.DetachNode(source);
            source.Dispose();
            engine.Dispose();
            return false;
        }

        _engine = engine;
        _source = source;
        // This engine only: another AVAudioEngine in the app (speech, a game) changes configuration on its own.
        _configurationObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVAudioEngine.ConfigurationChangeNotification, _ => OnMain("a configuration change", OnConfigurationChange), engine);
        error = null;
        Logger.LogInformation("Spine.Voice: engine at {Input} Hz in ({Channels} channels), {Output} Hz out, voice processing {Processing}.",
            inputRate, inputFormat.ChannelCount, _outputRate, IsVoiceProcessingOn ? "on" : "off");
        return true;
    }

    private unsafe void OnTap(AVAudioPcmBuffer buffer, int rate)
    {
        try
        {
            var frames = (int)buffer.FrameLength;
            if (frames == 0 || buffer.FloatChannelData == IntPtr.Zero)
                return;

            var channels = (float**)buffer.FloatChannelData;
            OnCaptured(new ReadOnlySpan<float>(channels[0], frames), rate);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Spine.Voice: the microphone's buffer failed.");
        }
    }

    private unsafe int Render(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
    {
        var frames = (int)frameCount;
        try
        {
            if (_render.Length < frames)
                _render = new float[frames * 2];

            var span = _render.AsSpan(0, frames);
            RenderOutput(span, (int)_outputRate);

            for (var b = 0; b < outputData.Count; b++)
            {
                var target = new Span<float>((void*)outputData[b].Data, Math.Min(frames, (int)(outputData[b].DataByteSize / sizeof(float))));
                span[..target.Length].CopyTo(target);
            }

            isSilence = false;
        }
        catch (Exception e)
        {
            isSilence = true;
            Logger.LogError(e, "Spine.Voice: rendering the answer failed.");
        }
        return 0;
    }

    private void OnMain(string what, Action action) => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            if (IsRunning)
                action();
        }
        catch (Exception e)
        {
            Fail(e as VoiceAudioException ?? new VoiceAudioException(VoiceAudioProblem.Failed, $"Spine.Voice failed handling {what}: {e.Message}", e));
        }
    });

    private void OnConfigurationChange()
    {
        // The engine stops when its hardware configuration changes, such as when voice processing comes on or a headset
        // is plugged in. Apple's advice is to start it again; a rebuild is the fallback.
        if (_engine is not { } engine || engine.Running)
            return;
        if (!engine.StartAndReturnError(out var error))
            Rebuild($"the engine did not restart after a configuration change ({error?.LocalizedDescription})");
    }

    private void OnInterruption(bool began)
    {
        if (began)
        {
            SetInterrupted(true);
            return;
        }

        // The call ended: the session and engine come back as they were.
        AVAudioSession.SharedInstance().SetActive(true, out _);
        if (_engine is { Running: false } engine && !engine.StartAndReturnError(out var error))
            Fail(new VoiceAudioException(VoiceAudioProblem.Failed, $"The audio engine did not restart after an interruption: {error?.LocalizedDescription}."));
        SetInterrupted(false);
    }

    private void OnRouteChange()
    {
        // A headset coming or going resets the override; back on the built-in route, the choice is made again.
        if (RouteName(AVAudioSession.SharedInstance()) is "Receiver" && _speaker)
            ApplySpeaker();
        SetRoute(RouteName(AVAudioSession.SharedInstance()));
    }

    private void Rebuild(string why)
    {
        if (!IsRunning)
            return;

        Logger.LogInformation("Spine.Voice: rebuilding the engine because {Why}.", why);
        TearDown();
        try
        {
            ConfigureSession(Options);
            Build(Options);
            SetRoute(RouteName(AVAudioSession.SharedInstance()));
        }
        catch (Exception e)
        {
            Fail(e as VoiceAudioException ?? new VoiceAudioException(VoiceAudioProblem.Failed, $"The audio engine could not be rebuilt: {e.Message}", e));
        }
    }

    private void TearDown()
    {
        if (_configurationObserver is { } observer)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            observer.Dispose();
            _configurationObserver = null;
        }

        if (_engine is { } engine)
        {
            engine.InputNode.RemoveTapOnBus(0);
            engine.Stop();
            if (_source is not null)
                engine.DetachNode(_source);
            engine.Dispose();
        }
        _engine = null;
        _source?.Dispose();
        _source = null;
    }

    private void ApplySpeaker()
    {
        // Only meaningful on the built-in route: a headset keeps the sound whatever this says.
        AVAudioSession.SharedInstance().OverrideOutputAudioPort(
            _speaker ? AVAudioSessionPortOverride.Speaker : AVAudioSessionPortOverride.None, out _);
    }

    private static string RouteName(AVAudioSession session) =>
        session.CurrentRoute?.Outputs?.FirstOrDefault() is { } port
            ? port.PortType == AVAudioSession.PortBuiltInSpeaker ? "Speaker"
            : port.PortType == AVAudioSession.PortBuiltInReceiver ? "Receiver"
            : port.PortName ?? "Unknown"
            : "None";

    private static void Check(bool ok, NSError? error, string what)
    {
        if (!ok)
            throw new VoiceAudioException(VoiceAudioProblem.Failed, $"Spine.Voice could not {what}: {error?.LocalizedDescription}.");
    }
}
#endif
