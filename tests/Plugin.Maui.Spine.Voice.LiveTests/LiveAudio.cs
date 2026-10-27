using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Voice.LiveTests;

/// <summary>
/// An audio engine in real time without a device: every 20 ms it hands the session the next piece of what the test said
/// (silence when nothing is queued) and plays 20 ms of the answer into nothing, so the answer takes as long as it would
/// out of a speaker.
/// </summary>
internal sealed class LiveAudio() : VoiceAudioEngine(null, TimeProvider.System)
{
    private const int Rate = 24_000;
    private const int Frame = Rate / 50;
    private readonly Lock _gate = new();
    private readonly Queue<float[]> _said = new();
    private CancellationTokenSource? _run;

    public override bool IsSupported => true;

    /// <summary>Queues PCM16 at 24 kHz as the user's words, then silence.</summary>
    public void Say(byte[] pcm16)
    {
        var samples = MemoryMarshal.Cast<byte, short>(pcm16);
        lock (_gate)
        {
            for (var at = 0; at < samples.Length; at += Frame)
            {
                var frame = new float[Frame];
                for (var i = 0; i < Frame && at + i < samples.Length; i++)
                    frame[i] = samples[at + i] / 32768f;
                _said.Enqueue(frame);
            }
        }
    }

    /// <summary>Whether the words queued with <see cref="Say"/> have all gone to the session.</summary>
    public bool HasSaid
    {
        get
        {
            lock (_gate)
                return _said.Count == 0;
        }
    }

    protected override Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken)
    {
        IsVoiceProcessingOn = options.VoiceProcessing;
        SetRoute("Speaker");
        _run = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_run.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override Task StopPlatformAsync()
    {
        _run?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        var silence = new float[Frame];
        var output = new float[Frame];
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                float[]? frame;
                lock (_gate)
                    _said.TryDequeue(out frame);
                OnCaptured(frame ?? silence, Rate);
                RenderOutput(output, Rate);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
