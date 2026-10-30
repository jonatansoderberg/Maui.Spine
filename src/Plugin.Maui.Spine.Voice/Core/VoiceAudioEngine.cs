using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// What every platform's engine shares: resampling both ways, metering, the playback queue, duplex and mute, and the
/// state. A platform opens its devices and calls <see cref="OnCaptured"/> with the microphone's samples and
/// <see cref="RenderOutput"/> when its player wants more.
/// </summary>
internal abstract class VoiceAudioEngine : IVoiceAudio
{
    /// <summary>How long the microphone stays held back after the answer's last sample in half duplex: what the device still plays.</summary>
    private static readonly TimeSpan HalfDuplexTail = TimeSpan.FromMilliseconds(300);

    private readonly object _stateGate = new();
    private VoiceState _frameState = (VoiceState)(-1);    // none yet, so the first frame takes the state's own start
    private long _frameSince;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private ITimer? _convergence;
    private long _lastAudible;
    private readonly VoiceFrame _frame = new();
    private float[] _inputBuffer = new float[4096];
    private short[] _inputPcm = new short[4096];
    private short[] _outputPcm = new short[4096];
    private float[] _outputFloat = new float[4096];
    private float[] _fifo = new float[16384];
    private int _fifoCount;
    private PcmResampler? _inputResampler;
    private PcmResampler? _outputResampler;
    private VoiceMeter? _inputMeter;
    private VoiceMeter? _outputMeter;
    private PlaybackQueue? _queue;
    private volatile bool _flushOutput;
    private volatile bool _muted;
    private volatile bool _interrupted;
    private volatile int _duplex;
    private long _startedAt;
    private long _stateSince;
    private VoiceState _state = VoiceState.Idle;
    private string? _route;

    protected VoiceAudioEngine(ILogger? logger, TimeProvider? time)
    {
        Logger = logger ?? NullLogger.Instance;
        Time = time ?? TimeProvider.System;
    }

    protected ILogger Logger { get; }
    protected TimeProvider Time { get; }
    protected VoiceAudioOptions Options { get; private set; } = new();

    public abstract bool IsSupported { get; }
    public bool IsRunning { get; private set; }
    public VoiceState State { get { lock (_stateGate) return _state; } }
    public bool IsVoiceProcessingOn { get; protected set; }
    public string? Route => _route;
    public VoiceFrame Frame => _frame;
    public bool IsPlaying => _queue?.IsPlaying == true;
    public PlaybackPosition? Playing => _queue?.Current;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            Refresh();
        }
    }

    public VoiceDuplex Duplex
    {
        get => (VoiceDuplex)_duplex;
        set => _duplex = (int)value;
    }

    public virtual bool UseSpeaker { get; set; }

    public event VoiceInputHandler? InputAvailable;
    public event Action<VoiceState>? StateChanged;
    public event Action<string>? RouteChanged;
    public event Action<VoiceAudioException>? Failed;

    public async Task StartAsync(VoiceAudioOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new VoiceAudioOptions();
        options.Validate();
        if (!IsSupported)
            throw new VoiceAudioException(VoiceAudioProblem.Unavailable, "Spine.Voice has no audio engine on this platform yet.");

        // One start or stop at a time: a double tap must not open the microphone twice.
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StartLockedAsync(options, cancellationToken);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task StartLockedAsync(VoiceAudioOptions options, CancellationToken cancellationToken)
    {
        if (IsRunning)
            throw new InvalidOperationException("The voice engine already runs; stop it before starting it again.");

        Options = options;
        _duplex = (int)options.Duplex;
        _inputMeter = new VoiceMeter(options.InputRate);
        _outputMeter = new VoiceMeter(options.OutputRate);
        _queue = new PlaybackQueue(options.OutputRate, options.Prebuffer);
        _inputResampler = null;
        _outputResampler = null;
        _fifoCount = 0;
        _interrupted = false;
        UseSpeaker = options.PreferSpeaker;

        try
        {
            await StartPlatformAsync(options, cancellationToken);
        }
        catch
        {
            // A platform that got halfway (the session active, the engine not) is closed again, so the app's other
            // audio is not left ducked.
            try
            {
                await StopPlatformAsync();
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Spine.Voice: closing after a failed start failed.");
            }
            throw;
        }

        _startedAt = Time.GetTimestamp();
        IsRunning = true;
        // Listening is announced when the convergence is over, also when nothing calls UpdateFrame.
        _convergence = Time.CreateTimer(_ => Refresh(), null, options.Input ? options.Convergence : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        Refresh();
        Logger.LogInformation("Spine.Voice: started at {Input}/{Output} Hz, voice processing {Processing}, route {Route}.",
            options.InputRate, options.OutputRate, IsVoiceProcessingOn ? "on" : "off", _route);
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            _convergence?.Dispose();
            _convergence = null;
            _queue?.Reset();
            await StopPlatformAsync();
            Refresh();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public void Enqueue(string itemId, ReadOnlySpan<byte> pcm16)
    {
        if (_queue is not { } queue)
            throw new InvalidOperationException("Start the voice engine before queuing audio.");
        queue.Enqueue(itemId, MemoryMarshal.Cast<byte, short>(pcm16));
    }

    public void Complete(string itemId) => _queue?.Complete(itemId);

    public PlaybackPosition? Flush()
    {
        var position = _queue?.Flush();
        // The audio thread drops what it has already resampled, at its next render.
        _flushOutput = true;
        OnFlushed();
        return position;
    }

    public TimeSpan Played(string itemId) => _queue?.Played(itemId) ?? TimeSpan.Zero;

    public VoiceFrame UpdateFrame()
    {
        if (IsRunning)
        {
            _frame.InputLevel = _inputMeter?.Read(_frame.WritableInputBands) ?? 0;
            _frame.OutputLevel = _outputMeter?.Read(_frame.WritableOutputBands) ?? 0;
        }
        else
        {
            // A stopped engine shows silence, not the last levels it heard.
            _frame.InputLevel = 0;
            _frame.OutputLevel = 0;
            _frame.WritableInputBands.Clear();
            _frame.WritableOutputBands.Clear();
        }
        var now = Time.GetTimestamp();
        var playing = IsPlaying;
        _frame.Time = IsRunning ? Time.GetElapsedTime(_startedAt, now) : TimeSpan.Zero;
        Refresh();
        lock (_stateGate)
        {
            var state = _state == VoiceState.Listening && playing ? VoiceState.Speaking : _state;
            if (state != _frameState)
            {
                // A change of the engine's own state counts from when it changed; Speaking from this tick.
                _frameSince = state == _state && _stateSince > _frameSince ? _stateSince : now;
                _frameState = state;
            }
            _frame.State = state;
            _frame.TimeInState = Time.GetElapsedTime(_frameSince, now);
        }
        return _frame;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Opens the platform's devices; sets <see cref="IsVoiceProcessingOn"/> and the route.</summary>
    protected abstract Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken);

    /// <summary>Closes the platform's devices.</summary>
    protected abstract Task StopPlatformAsync();

    /// <summary>The answer was cut off: a platform whose player buffers ahead of <see cref="RenderOutput"/> drops that too.</summary>
    protected virtual void OnFlushed()
    {
    }

    /// <summary>The microphone's samples, -1..1 at the device's rate, after the platform's echo cancellation.</summary>
    protected void OnCaptured(ReadOnlySpan<float> samples, int deviceRate)
    {
        if (!IsRunning || _inputMeter is not { } meter)
            return;

        if (_inputResampler?.InputRate != deviceRate)
            _inputResampler = new PcmResampler(deviceRate, Options.InputRate);

        var capacity = _inputResampler.MaxOutput(samples.Length);
        Grow(ref _inputBuffer, capacity);
        var count = _inputResampler.Process(samples, _inputBuffer);
        var resampled = _inputBuffer.AsSpan(0, count);

        if (_muted || _interrupted)
        {
            meter.Decay(count);
            return;
        }
        meter.Write(resampled);

        if (Duplex == VoiceDuplex.HalfWhileSpeaking && (IsPlaying || Time.GetElapsedTime(Interlocked.Read(ref _lastAudible)) < HalfDuplexTail))
            return;

        if (InputAvailable is not { } handler)
            return;

        Grow(ref _inputPcm, count);
        for (var i = 0; i < count; i++)
            _inputPcm[i] = (short)Math.Clamp(resampled[i] * 32767f, short.MinValue, short.MaxValue);
        handler(MemoryMarshal.AsBytes(_inputPcm.AsSpan(0, count)));
    }

    /// <summary>Fills the player's buffer, -1..1 at the device's rate, with the answer or silence.</summary>
    protected void RenderOutput(Span<float> destination, int deviceRate)
    {
        if (_flushOutput)
        {
            _flushOutput = false;
            _fifoCount = 0;
            _outputResampler?.Reset();
        }

        if (!IsRunning || _queue is not { } queue || _outputMeter is not { } meter)
        {
            destination.Clear();
            return;
        }

        if (_outputResampler?.OutputRate != deviceRate)
            _outputResampler = new PcmResampler(Options.OutputRate, deviceRate);

        var played = 0;
        while (_fifoCount < destination.Length)
        {
            var wanted = (int)Math.Ceiling((destination.Length - _fifoCount) * (double)Options.OutputRate / deviceRate) + 1;
            Grow(ref _outputPcm, wanted);
            var real = queue.Read(_outputPcm.AsSpan(0, wanted));
            if (real == 0)
                break;

            meter.Write(_outputPcm.AsSpan(0, real));
            played += real;

            Grow(ref _outputFloat, real);
            for (var i = 0; i < real; i++)
                _outputFloat[i] = _outputPcm[i] / 32768f;

            Grow(ref _fifo, _fifoCount + _outputResampler.MaxOutput(real), keep: true);
            _fifoCount += _outputResampler.Process(_outputFloat.AsSpan(0, real), _fifo.AsSpan(_fifoCount));
        }

        if (played == 0)
            meter.Decay((int)((long)destination.Length * Options.OutputRate / deviceRate));
        if (played > 0 || _fifoCount > 0)
            Interlocked.Exchange(ref _lastAudible, Time.GetTimestamp());

        var n = Math.Min(_fifoCount, destination.Length);
        _fifo.AsSpan(0, n).CopyTo(destination);
        destination[n..].Clear();
        _fifo.AsSpan(n, _fifoCount - n).CopyTo(_fifo);
        _fifoCount -= n;
    }

    /// <summary>The system took the audio away (a phone call) or gave it back.</summary>
    protected void SetInterrupted(bool interrupted)
    {
        _interrupted = interrupted;
        Logger.LogInformation("Spine.Voice: audio {What}.", interrupted ? "interrupted" : "resumed");
        Refresh();
    }

    /// <summary>The sound's current route.</summary>
    protected void SetRoute(string route)
    {
        if (route == _route)
            return;
        _route = route;
        if (IsRunning)
            RouteChanged?.Invoke(route);
    }

    /// <summary>The engine stopped working.</summary>
    protected void Fail(VoiceAudioException exception)
    {
        Logger.LogError(exception, "Spine.Voice: the audio engine failed.");
        Failed?.Invoke(exception);
    }

    private void Refresh()
    {
        VoiceState next;
        if (!IsRunning)
            next = VoiceState.Idle;
        else if (_muted || _interrupted)
            next = VoiceState.Muted;
        else if (Options.Input && Time.GetElapsedTime(_startedAt) < Options.Convergence)
            next = VoiceState.Connecting;
        else
            next = VoiceState.Listening;

        lock (_stateGate)
        {
            if (next == _state)
                return;
            _state = next;
            _stateSince = Time.GetTimestamp();
        }
        StateChanged?.Invoke(next);
    }

    private static void Grow<T>(ref T[] buffer, int length, bool keep = false)
    {
        if (buffer.Length >= length)
            return;
        var grown = new T[Math.Max(length, buffer.Length * 2)];
        if (keep)
            buffer.CopyTo(grown, 0);
        buffer = grown;
    }
}
