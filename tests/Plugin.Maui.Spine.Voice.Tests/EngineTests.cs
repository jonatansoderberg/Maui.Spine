using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Voice.Tests;

/// <summary>A platform that is driven by the test: it captures what it is told and renders when asked.</summary>
internal sealed class FakePlatform(TimeProvider time) : VoiceAudioEngine(null, time)
{
    public override bool IsSupported => true;

    public Exception? StartThrows { get; set; }
    public TaskCompletionSource? StartGate { get; set; }
    public int Starts { get; private set; }
    public VoiceAudioOptions? StartedWith { get; private set; }
    public int Stops { get; private set; }

    public void Capture(float[] samples, int rate) => OnCaptured(samples, rate);

    public float[] Render(int frames, int rate)
    {
        var buffer = new float[frames];
        RenderOutput(buffer, rate);
        return buffer;
    }

    public void Interrupt(bool on) => SetInterrupted(on);

    protected override async Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken)
    {
        Starts++;
        StartedWith = options;
        if (StartGate is { } gate)
            await gate.Task;
        if (StartThrows is { } e)
            throw e;
        IsVoiceProcessingOn = options.VoiceProcessing;
        SetRoute("Speaker");
    }

    protected override Task StopPlatformAsync()
    {
        Stops++;
        return Task.CompletedTask;
    }
}

public sealed class EngineTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_microphone_arrives_as_pcm16_at_the_providers_rate()
    {
        await using var engine = new FakePlatform(_time);
        var received = new List<byte>();
        engine.InputAvailable += pcm => received.AddRange(pcm.ToArray());
        await engine.StartAsync();

        engine.Capture(Signals.Sine(440, 48_000, 48_000, 0.5f), 48_000);

        var samples = new short[received.Count / 2];
        Buffer.BlockCopy(received.ToArray(), 0, samples, 0, received.Count);
        Assert.InRange(samples.Length, 23_960, 24_002);
        var floats = samples.Skip(200).Take(23_000).Select(s => s / 32768f).ToArray();
        Assert.InRange(Signals.Frequency(floats, 24_000), 437, 443);
        Assert.Equal(0.5 / Math.Sqrt(2), Signals.Rms(floats), 0.02);
    }

    [Fact]
    public async Task Connecting_lasts_the_convergence_then_listening()
    {
        await using var engine = new FakePlatform(_time);
        var states = new List<VoiceState>();
        engine.StateChanged += states.Add;

        await engine.StartAsync(new VoiceAudioOptions { Convergence = TimeSpan.FromSeconds(1) });
        Assert.Equal(VoiceState.Connecting, engine.UpdateFrame().State);

        _time.Advance(TimeSpan.FromSeconds(1));
        var frame = engine.UpdateFrame();

        Assert.Equal(VoiceState.Listening, frame.State);
        Assert.Equal(TimeSpan.Zero, frame.TimeInState);
        Assert.Equal(TimeSpan.FromSeconds(1), frame.Time);
        Assert.Equal([VoiceState.Connecting, VoiceState.Listening], states);
    }

    [Fact]
    public async Task Muted_and_interrupted_upload_nothing_and_meter_silence()
    {
        await using var engine = new FakePlatform(_time);
        var uploads = 0;
        engine.InputAvailable += _ => uploads++;
        await engine.StartAsync(new VoiceAudioOptions { Convergence = TimeSpan.Zero });

        engine.IsMuted = true;
        engine.Capture(Signals.Sine(440, 24_000, 24_000), 24_000);
        Assert.Equal(VoiceState.Muted, engine.UpdateFrame().State);
        Assert.Equal(0, engine.Frame.InputLevel);

        engine.IsMuted = false;
        engine.Interrupt(true);
        engine.Capture(Signals.Sine(440, 24_000, 2400), 24_000);
        Assert.Equal(VoiceState.Muted, engine.UpdateFrame().State);

        engine.Interrupt(false);
        engine.Capture(Signals.Sine(440, 24_000, 2400), 24_000);
        Assert.Equal(VoiceState.Listening, engine.UpdateFrame().State);
        Assert.Equal(1, uploads);
        Assert.True(engine.Frame.InputLevel > 0.5f);
    }

    [Fact]
    public async Task Half_duplex_holds_the_microphone_back_while_an_answer_plays()
    {
        await using var engine = new FakePlatform(_time);
        var uploads = 0;
        engine.InputAvailable += _ => uploads++;
        await engine.StartAsync(new VoiceAudioOptions { Duplex = VoiceDuplex.HalfWhileSpeaking, Prebuffer = TimeSpan.Zero });

        engine.Enqueue("answer", Signals.Bytes(new short[24_000]));
        engine.Capture(new float[480], 24_000);
        Assert.Equal(0, uploads);

        engine.Flush();
        engine.Capture(new float[480], 24_000);
        Assert.Equal(1, uploads);
    }

    [Fact]
    public async Task The_answer_plays_at_the_devices_rate_and_is_metered_as_it_plays()
    {
        await using var engine = new FakePlatform(_time);
        await engine.StartAsync(new VoiceAudioOptions { Prebuffer = TimeSpan.Zero });
        engine.Enqueue("answer", Signals.Bytes(Signals.Pcm(Signals.Sine(440, 24_000, 24_000, 0.5f))));
        engine.Complete("answer");

        var played = new List<float>();
        for (var i = 0; i < 100; i++)
            played.AddRange(engine.Render(480, 48_000));      // 10 ms buffers at 48 kHz: one second

        var settled = played.Skip(200).Take(47_000).ToArray();
        Assert.InRange(Signals.Frequency(settled, 48_000), 437, 443);
        Assert.Equal(0.5 / Math.Sqrt(2), Signals.Rms(settled), 0.02);
        Assert.True(engine.UpdateFrame().OutputLevel > 0.8f, $"output level {engine.Frame.OutputLevel}");
        Assert.InRange(engine.Played("answer").TotalMilliseconds, 990, 1000);
    }

    [Fact]
    public async Task The_frame_says_speaking_while_an_answer_plays_but_the_state_stays_listening()
    {
        await using var engine = new FakePlatform(_time);
        await engine.StartAsync(new VoiceAudioOptions { Convergence = TimeSpan.Zero, Prebuffer = TimeSpan.Zero });
        engine.UpdateFrame();
        engine.Enqueue("answer", Signals.Bytes(Signals.Pcm(Signals.Sine(440, 24_000, 2400, 0.5f))));
        engine.Complete("answer");

        _time.Advance(TimeSpan.FromSeconds(5));             // listening for a while before the answer
        Assert.Equal(VoiceState.Speaking, engine.UpdateFrame().State);
        Assert.Equal(VoiceState.Listening, engine.State);
        Assert.Equal(TimeSpan.Zero, engine.Frame.TimeInState);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        engine.Render(4800, 48_000);                          // all of it
        var frame = engine.UpdateFrame();
        Assert.Equal(VoiceState.Listening, frame.State);
        Assert.Equal(TimeSpan.Zero, frame.TimeInState);
    }

    [Fact]
    public async Task Flush_stops_the_answer_at_the_next_buffer()
    {
        await using var engine = new FakePlatform(_time);
        await engine.StartAsync(new VoiceAudioOptions { Prebuffer = TimeSpan.Zero });
        engine.Enqueue("answer", Signals.Bytes(Signals.Pcm(Signals.Sine(440, 24_000, 24_000, 0.5f))));
        engine.Render(4800, 48_000);                          // 100 ms

        var position = engine.Flush();
        var after = engine.Render(480, 48_000);

        Assert.Equal("answer", position?.ItemId);
        Assert.InRange(position!.Value.Played.TotalMilliseconds, 100, 101);
        Assert.All(after, s => Assert.Equal(0, s));
        Assert.False(engine.IsPlaying);
    }

    [Fact]
    public async Task Stop_drops_queued_audio_and_goes_idle()
    {
        var engine = new FakePlatform(_time);
        await engine.StartAsync();
        engine.Enqueue("answer", Signals.Bytes(new short[24_000]));

        await engine.StopAsync();

        Assert.Equal(VoiceState.Idle, engine.UpdateFrame().State);
        Assert.False(engine.IsPlaying);
        await Assert.ThrowsAsync<ArgumentException>(() => engine.StartAsync(new VoiceAudioOptions { InputRate = 100 }));
    }

    [Fact]
    public async Task A_failed_start_closes_what_the_platform_opened()
    {
        await using var engine = new FakePlatform(_time) { StartThrows = new VoiceAudioException(VoiceAudioProblem.Failed, "No engine.") };

        await Assert.ThrowsAsync<VoiceAudioException>(() => engine.StartAsync());

        Assert.Equal(1, engine.Stops);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task Listening_is_announced_when_the_convergence_ends_without_a_tick()
    {
        await using var engine = new FakePlatform(_time);
        var states = new List<VoiceState>();
        engine.StateChanged += states.Add;
        await engine.StartAsync(new VoiceAudioOptions { Convergence = TimeSpan.FromSeconds(1) });

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([VoiceState.Connecting, VoiceState.Listening], states);
    }

    [Fact]
    public async Task Two_starts_at_once_open_the_platform_once()
    {
        await using var engine = new FakePlatform(_time) { StartGate = new TaskCompletionSource() };

        var first = engine.StartAsync();
        var second = engine.StartAsync();
        engine.StartGate.SetResult();
        await first;

        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public async Task Half_duplex_waits_for_the_tail_the_device_still_plays()
    {
        await using var engine = new FakePlatform(_time);
        var uploads = 0;
        engine.InputAvailable += _ => uploads++;
        await engine.StartAsync(new VoiceAudioOptions { Duplex = VoiceDuplex.HalfWhileSpeaking, Prebuffer = TimeSpan.Zero });
        engine.Enqueue("answer", Signals.Bytes(new short[480]));
        engine.Complete("answer");
        engine.Render(960, 48_000);                           // the whole answer is handed to the device

        engine.Capture(new float[480], 24_000);
        _time.Advance(TimeSpan.FromMilliseconds(350));
        engine.Capture(new float[480], 24_000);

        Assert.Equal(1, uploads);
    }

    [Fact]
    public async Task A_stopped_engine_shows_silence()
    {
        await using var engine = new FakePlatform(_time);
        await engine.StartAsync();
        engine.Capture(Signals.Sine(440, 24_000, 24_000), 24_000);
        Assert.True(engine.UpdateFrame().InputLevel > 0.5f);

        await engine.StopAsync();

        Assert.Equal(0, engine.UpdateFrame().InputLevel);
        Assert.All(engine.Frame.InputBands.ToArray(), b => Assert.Equal(0, b));
    }
}
