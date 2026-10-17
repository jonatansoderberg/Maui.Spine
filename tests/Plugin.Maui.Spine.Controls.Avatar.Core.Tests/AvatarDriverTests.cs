using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class AvatarDriverTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly FakeTimeProvider _time = new();

    private AvatarRuntime Runtime(string avatar = "nova")
    {
        var package = AvatarArchiveTests.Load(avatar);
        var runtime = new AvatarRuntime();
        runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0], _time, 7));
        return runtime;
    }

    private AvatarRenderFrame? Run(AvatarRuntime runtime, TimeSpan duration)
    {
        AvatarRenderFrame? frame = null;
        for (var t = TimeSpan.Zero; t < duration; t += Frame)
        {
            _time.Advance(Frame);
            frame = runtime.Tick(Frame);
        }
        return frame;
    }

    [Fact]
    public void AzureVisemesMapIntoTheCanonicalSet()
    {
        for (var azure = 0; azure < 22; azure++)
            Assert.InRange(AvatarVisemes.FromAzure(azure), 0, 14);

        Assert.Equal(AvatarVisemes.Silence, AvatarVisemes.FromAzure(0));
        Assert.Equal(AvatarVisemes.PP, AvatarVisemes.FromAzure(21));
        Assert.Equal(AvatarVisemes.FF, AvatarVisemes.FromAzure(18));
        Assert.Equal(AvatarVisemes.SS, AvatarVisemes.FromAzure(15));
        Assert.Equal(AvatarVisemes.AA, AvatarVisemes.FromAzure(2));
        Assert.Equal(AvatarVisemes.U, AvatarVisemes.FromAzure(7));
        Assert.Equal(-1, AvatarVisemes.FromAzure(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AvatarVisemes.FromAzure(22));
    }

    [Fact]
    public void EveryCanonicalVisemeSurvivesARoundTripThroughAzure()
    {
        for (var canonical = 0; canonical < AvatarVisemes.Count; canonical++)
            Assert.Equal(canonical, AvatarVisemes.FromAzure(AvatarVisemes.ToAzure(canonical)));
    }

    [Fact]
    public void DriverTicksBeforeTheSchedulerUpdates()
    {
        var runtime = Runtime();
        var driver = new FakeDriver();
        runtime.Driver = driver;
        driver.OnEachTick = input => input.SetOutputLevel(1);

        _time.Advance(Frame);
        var frame = runtime.Tick(Frame)!;

        Assert.Equal(["attached", "tick 16"], driver.Calls);
        // The level the driver set in this tick is already in this tick's frame.
        Assert.True(frame.OutputLevel > 0);
    }

    [Fact]
    public void ReplacingTheDriverDetachesTheOldOneFirst()
    {
        var runtime = Runtime();
        var first = new FakeDriver();
        var second = new FakeDriver { Log = first.Calls };
        runtime.Driver = first;
        runtime.Driver = second;
        runtime.Driver = null;

        Assert.Equal(["attached", "detached", "attached", "detached"], first.Calls);
        Assert.False(first.IsAttached);
        Assert.Throws<InvalidOperationException>(() => first.ReadInput());
    }

    [Fact]
    public void ADriverDrivesOneAvatarAtATime()
    {
        var driver = new FakeDriver();
        Runtime().Driver = driver;

        Assert.Throws<InvalidOperationException>(() => Runtime().Driver = driver);
    }

    [Fact]
    public void StateOutlivesASourceChange()
    {
        var runtime = Runtime();
        runtime.Input.SetState(AvatarState.Thinking);
        runtime.Input.SetMuted(true);

        var package = AvatarArchiveTests.Load("plush-puff");
        runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0], _time, 1));
        var frame = Run(runtime, TimeSpan.FromMilliseconds(600))!;

        Assert.Equal(AvatarState.Thinking, runtime.Scheduler!.State);
        Assert.True(frame.MicMutedWeight > 0.9f);
    }

    [Fact]
    public void TextIsMouthedThenTheAvatarGoesIdle()
    {
        var runtime = Runtime();
        var text = new TextAvatarDriver { CharactersPerSecond = 20 };
        runtime.Driver = text;
        Assert.Equal(AvatarLipSyncQuality.EstimatedText, runtime.Scheduler!.LipSyncQuality);

        text.Begin();
        Run(runtime, TimeSpan.FromMilliseconds(100));
        Assert.Equal(AvatarState.Thinking, runtime.Input.State);

        text.Append("mamma mia");
        var frame = Run(runtime, TimeSpan.FromMilliseconds(150))!;
        Assert.Equal(AvatarState.Speaking, runtime.Input.State);
        Assert.True(frame.OutputLevel > 0);
        // Cues are laid down about a second ahead, not all at once.
        Assert.InRange(runtime.Scheduler.Diagnostics.AcceptedCues, 1, AvatarTextVisemes.FromText("mamma mia", 20).Count);

        // Streamed text that has run out without Complete: waiting for more.
        frame = Run(runtime, TimeSpan.FromSeconds(1.5))!;
        Assert.Equal(AvatarState.Thinking, runtime.Input.State);
        Assert.True(text.IsBusy);

        text.Append(" ciao");
        text.Complete();
        Run(runtime, TimeSpan.FromMilliseconds(100));
        Assert.Equal(AvatarState.Speaking, runtime.Input.State);
        Run(runtime, TimeSpan.FromSeconds(1));
        Assert.Equal(AvatarState.Idle, runtime.Input.State);
        Assert.False(text.IsBusy);
        Assert.Equal(AvatarTextVisemes.FromText("mamma mia", 20).Count + AvatarTextVisemes.FromText(" ciao", 20).Count, runtime.Scheduler.Diagnostics.AcceptedCues);
    }

    [Fact]
    public void ClearDropsQueuedCuesAndClosesTheMouth()
    {
        var runtime = Runtime();
        var text = new TextAvatarDriver();
        runtime.Driver = text;
        text.Mouth("a long sentence that is cut short by the user before it ends");
        Run(runtime, TimeSpan.FromMilliseconds(300));
        var queued = runtime.Scheduler!.Diagnostics.QueuedCues;

        text.Clear();
        var frame = Run(runtime, TimeSpan.FromSeconds(1))!;

        Assert.True(queued > 0);
        Assert.Equal(0, runtime.Scheduler.Diagnostics.QueuedCues);
        Assert.Equal(AvatarState.Idle, runtime.Input.State);
        Assert.True(frame.OutputLevel < 0.05f);
    }

    private sealed class FakeDriver : AvatarDriver
    {
        public List<string> Calls { get; init; } = [];

        public List<string>? Log { get; init; }

        public Action<AvatarInput>? OnEachTick { get; set; }

        public AvatarInput ReadInput() => Input;

        protected internal override void OnAttached() => (Log ?? Calls).Add("attached");

        protected internal override void OnDetached() => (Log ?? Calls).Add("detached");

        protected internal override void OnTick(TimeSpan delta)
        {
            (Log ?? Calls).Add($"tick {delta.TotalMilliseconds:0}");
            OnEachTick?.Invoke(Input);
        }
    }
}
