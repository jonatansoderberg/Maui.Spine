using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Voice;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class VoiceDriverTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    private readonly FakeTimeProvider _time = new();

    private (AvatarRuntime Runtime, FakeAudio Audio) Create()
    {
        var package = AvatarArchiveTests.Load("nova");
        var runtime = new AvatarRuntime();
        runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0], _time, 5));
        var audio = new FakeAudio();
        runtime.Driver = new VoiceAvatarDriver(audio);
        return (runtime, audio);
    }

    private AvatarRenderFrame Run(AvatarRuntime runtime, FakeAudio audio, int frames)
    {
        AvatarRenderFrame frame = null!;
        for (var i = 0; i < frames; i++)
        {
            _time.Advance(Frame);
            if (audio.Playing is { } playing)
                audio.Playing = playing with { Played = playing.Played + Frame };
            frame = runtime.Tick(Frame)!;
        }
        return frame;
    }

    [Theory]
    [InlineData(VoiceState.Connecting, AvatarState.Connecting)]
    [InlineData(VoiceState.Listening, AvatarState.Listening)]
    [InlineData(VoiceState.Thinking, AvatarState.Thinking)]
    [InlineData(VoiceState.Speaking, AvatarState.Speaking)]
    [InlineData(VoiceState.Muted, AvatarState.Muted)]
    [InlineData(VoiceState.Interrupted, AvatarState.Interrupted)]
    public void TheStateFollowsOneToOne(VoiceState voice, AvatarState avatar)
    {
        var (runtime, audio) = Create();
        audio.Frame.State = voice;
        Run(runtime, audio, 1);

        Assert.Equal(avatar, runtime.Input.State);
    }

    [Fact]
    public void ListeningFollowsTheMicrophone()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Listening;
        audio.Frame.InputLevel = 0.8f;
        var frame = Run(runtime, audio, 10);

        Assert.True(frame.InputLevel > 0.5f);
        Assert.Equal(AvatarLipSyncQuality.AudioReactive, runtime.Input.LipSyncQuality);
    }

    [Fact]
    public void VisemesFromTheSessionBecomeTimedCues()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        audio.Playing = new("item-1", TimeSpan.Zero);
        audio.IsPlaying = true;
        audio.Frame.Viseme = Visemes.P;
        Run(runtime, audio, 3);
        audio.Frame.Viseme = Visemes.Aa;
        var frame = Run(runtime, audio, 3);

        Assert.Equal(AvatarLipSyncQuality.TimedVisemes, runtime.Input.LipSyncQuality);
        Assert.Contains(frame.Speech.ToArray(), w => w.Pose == "viseme_aa" && w.Weight > 0.3f);
    }

    [Fact]
    public void WithoutVisemesTheMouthOpensWithTheAnswersLevel()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        audio.Playing = new("item-1", TimeSpan.Zero);
        audio.IsPlaying = true;
        audio.Frame.OutputLevel = 0.6f;
        var frame = Run(runtime, audio, 6);

        Assert.Equal(AvatarLipSyncQuality.AudioReactive, runtime.Input.LipSyncQuality);
        Assert.Contains(frame.Speech.ToArray(), w => w.Pose.StartsWith("viseme_", StringComparison.Ordinal) && w.Weight > 0.2f);
    }

    [Fact]
    public void WithoutVisemesASilentAnswerKeepsTheMouthShut()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        audio.Playing = new("item-1", TimeSpan.Zero);
        audio.IsPlaying = true;
        audio.Frame.OutputLevel = 0;
        var frame = Run(runtime, audio, 6);

        Assert.DoesNotContain(frame.Speech.ToArray(), w => w.Weight > 0.05f);
    }

    [Theory]
    [InlineData(VoiceState.Speaking)]
    [InlineData(VoiceState.Listening)]
    public void BarsAndRingGetTheSpectrumWhileTheConversationIsLive(VoiceState state)
    {
        foreach (var avatar in new[] { "bars", "ring" })
        {
            var package = AvatarArchiveTests.Load(avatar);
            var runtime = new AvatarRuntime();
            runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0], _time, 5));
            var audio = new FakeAudio();
            runtime.Driver = new VoiceAvatarDriver(audio);
            audio.Frame.State = state;
            audio.Frame.OutputLevel = audio.Frame.InputLevel = 0.6f;
            for (var b = 0; b < VoiceFrame.BandCount; b++)
                audio.Frame.WritableOutputBands[b] = audio.Frame.WritableInputBands[b] = 0.7f;
            var frame = Run(runtime, audio, 20);

            Assert.True(frame.OutputReactiveWeight > 0.9f && frame.InputReactiveWeight > 0.9f, $"{avatar}: reactive {frame.OutputReactiveWeight}, {frame.InputReactiveWeight}");
            Assert.True(frame.OutputBands[3] > 0.3f && frame.InputBands[3] > 0.3f, $"{avatar}: bands {frame.OutputBands[3]}, {frame.InputBands[3]}");
        }
    }

    [Fact]
    public void TheMouthClosesWhenTheAnswerHasPlayed()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        audio.Playing = new("item-1", TimeSpan.Zero);
        audio.IsPlaying = true;
        audio.Frame.OutputLevel = 0.6f;
        Assert.Contains(Run(runtime, audio, 6).Speech.ToArray(), w => w.Weight > 0.2f);

        // The last sample has played: nothing is playing, the level is gone, the session listens again.
        audio.Playing = null;
        audio.IsPlaying = false;
        audio.Frame.OutputLevel = 0;
        audio.Frame.State = VoiceState.Listening;
        var frame = Run(runtime, audio, 20);

        Assert.DoesNotContain(frame.Speech.ToArray(), w => w.Pose.StartsWith("viseme_", StringComparison.Ordinal) && w.Weight > 0.05f);
    }

    [Fact]
    public void ANewAnswerDropsTheCuesOfTheOneCutOff()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        audio.Playing = new("item-1", TimeSpan.Zero);
        audio.IsPlaying = true;
        audio.Frame.Viseme = Visemes.Aa;
        Run(runtime, audio, 5);
        var resets = runtime.Scheduler!.Diagnostics.Resets;

        audio.Playing = new("item-2", TimeSpan.Zero);
        Run(runtime, audio, 1);

        Assert.Equal(resets + 1, runtime.Scheduler.Diagnostics.Resets);
    }

    [Fact]
    public void InterruptFlushesTheEngine()
    {
        var (runtime, audio) = Create();
        audio.Frame.State = VoiceState.Speaking;
        Run(runtime, audio, 2);

        runtime.Interrupt();

        Assert.Equal(1, audio.Flushes);
    }

    [Fact]
    public void MutedShowsTheBadge()
    {
        var (runtime, audio) = Create();
        audio.IsMuted = true;
        audio.Frame.State = VoiceState.Muted;
        var frame = Run(runtime, audio, 20);

        Assert.True(runtime.Input.IsMuted);
        Assert.True(frame.MicMutedWeight > 0.5f);
    }

    [Fact]
    public void The_session_asks_for_the_avatars_voice()
    {
        var runtime = new AvatarRuntime();
        var session = new FakeSession();
        runtime.Driver = new VoiceAvatarDriver(session);

        var nova = AvatarArchiveTests.Load("nova");
        runtime.Load(new AvatarScheduler(nova, nova.Manifest.Representations[0], _time, 5));
        runtime.Tick(Frame);
        Assert.Equal("cedar", session.Voice);

        // An avatar that names no voice leaves the feature's own.
        var bars = AvatarArchiveTests.Load("bars");
        runtime.Load(new AvatarScheduler(bars, bars.Manifest.Representations[0], _time, 5));
        runtime.Tick(Frame);
        Assert.Null(session.Voice);
    }

    [Fact]
    public void Without_UsesAvatarVoice_the_app_keeps_its_choice()
    {
        var runtime = new AvatarRuntime();
        var session = new FakeSession { Voice = "verse" };
        runtime.Driver = new VoiceAvatarDriver(session) { UsesAvatarVoice = false };

        var nova = AvatarArchiveTests.Load("nova");
        runtime.Load(new AvatarScheduler(nova, nova.Manifest.Representations[0], _time, 5));
        runtime.Tick(Frame);

        Assert.Equal("verse", session.Voice);
    }

    private sealed class FakeSession : IVoiceSession
    {
        private readonly VoiceFrame _frame = new();

        public VoiceState State => VoiceState.Idle;
        public bool IsConnected => false;
        public bool IsMuted { get; set; }
        public VoiceDuplex Duplex { get; set; }
        public IList<Microsoft.Extensions.AI.ChatMessage> Transcript { get; } = [];
        public TimeSpan? TimeLeft => null;
        public bool EndsOnGoodbye { get; set; }
        public string? Voice { get; set; }
        public string? SpokenVoice => null;
        public event Action<VoiceState>? StateChanged { add { } remove { } }
        public event Action<VoiceSessionEnd, Exception?>? Ended { add { } remove { } }
        public Task ConnectAsync(string feature, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EndAsync() => Task.CompletedTask;
        public void Interrupt() { }
        public void BeginTurn() { }
        public void EndTurn() { }
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public VoiceFrame UpdateFrame() => _frame;

        public void Prefer(string function, Plugin.Maui.Spine.AI.SpineAIFunctionSide side) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAudio : IVoiceAudio
    {
        public VoiceFrame Frame { get; } = new();

        public bool IsSupported => true;

        public VoiceState State => Frame.State;

        public VoiceDuplex Duplex { get; set; } = VoiceDuplex.Full;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public bool IsRunning => true;

        public bool IsVoiceProcessingOn => true;

        public string? Route => "speaker";

        public bool IsMuted { get; set; }

        public bool UseSpeaker { get; set; }

        public bool IsPlaying { get; set; }

        public PlaybackPosition? Playing { get; set; }

        public int Flushes { get; private set; }

        public event VoiceInputHandler? InputAvailable { add { } remove { } }

        public event Action<VoiceState>? StateChanged { add { } remove { } }

        public event Action<string>? RouteChanged { add { } remove { } }

        public event Action<VoiceAudioException>? Failed { add { } remove { } }

        public Task StartAsync(VoiceAudioOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void Enqueue(string itemId, ReadOnlySpan<byte> pcm16) { }

        public void Complete(string itemId) { }

        public PlaybackPosition? Flush()
        {
            Flushes++;
            return Playing;
        }

        public TimeSpan Played(string itemId) => Playing?.Played ?? TimeSpan.Zero;

        public VoiceFrame UpdateFrame() => Frame;
    }
}
