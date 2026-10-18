using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class SpeechDriverTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly FakeTimeProvider _time = new();

    private (AvatarRuntime Runtime, FakeVoice Voice, SpeechAvatarDriver Driver) Create()
    {
        var package = AvatarArchiveTests.Load("nova");
        var runtime = new AvatarRuntime();
        runtime.Load(new AvatarScheduler(package, package.Manifest.Representations[0], _time, 3));
        var voice = new FakeVoice(_time);
        var driver = new SpeechAvatarDriver(voice);
        runtime.Driver = driver;
        return (runtime, voice, driver);
    }

    private void Run(AvatarRuntime runtime, TimeSpan duration)
    {
        for (var t = TimeSpan.Zero; t < duration; t += Frame)
        {
            _time.Advance(Frame);
            runtime.Tick(Frame);
        }
    }

    [Fact]
    public void ThinkingUntilTheVoiceStartsThenSpeakingThenIdle()
    {
        var (runtime, voice, driver) = Create();
        var done = driver.SpeakAsync("Hej och välkommen");
        Run(runtime, TimeSpan.FromMilliseconds(200));
        Assert.Equal(AvatarState.Thinking, runtime.Input.State);

        var utterance = voice.Current!;
        utterance.OnStarted();
        utterance.OnWord(0, 3);
        Run(runtime, TimeSpan.FromMilliseconds(150));
        Assert.Equal(AvatarState.Speaking, runtime.Input.State);
        Assert.Equal(AvatarLipSyncQuality.EstimatedText, runtime.Input.LipSyncQuality);
        Assert.True(runtime.Scheduler!.Diagnostics.AcceptedCues > 0);

        voice.Complete(utterance);
        Run(runtime, TimeSpan.FromMilliseconds(50));
        Assert.Equal(AvatarState.Idle, runtime.Input.State);
        Assert.Equal(SpeechEndReason.Completed, done.Result);
    }

    [Fact]
    public void EachWordIsMouthedWhereTheVoiceReachedIt()
    {
        var (runtime, voice, driver) = Create();
        _ = driver.SpeakAsync("one two");
        var utterance = voice.Current!;
        utterance.OnStarted();
        utterance.OnWord(0, 3);
        Run(runtime, TimeSpan.FromMilliseconds(100));
        var afterFirst = runtime.Scheduler!.Diagnostics.AcceptedCues;

        // The voice pauses: the second word's visemes come only when it is reached.
        Run(runtime, TimeSpan.FromMilliseconds(900));
        Assert.Equal(afterFirst, runtime.Scheduler.Diagnostics.AcceptedCues);
        utterance.OnWord(4, 3);
        Run(runtime, TimeSpan.FromMilliseconds(32));
        Assert.True(runtime.Scheduler.Diagnostics.AcceptedCues > afterFirst);
    }

    [Fact]
    public void AVoiceThatReportsNoWordsStillMovesTheMouth()
    {
        var (runtime, voice, driver) = Create();
        _ = driver.SpeakAsync("one two three");
        voice.Current!.OnStarted();
        Run(runtime, TimeSpan.FromMilliseconds(900));
        Assert.Equal(0, runtime.Scheduler!.Diagnostics.AcceptedCues);

        // A second of silence while the voice speaks: the words are guessed from the text, one after the other.
        Run(runtime, TimeSpan.FromMilliseconds(300));
        var afterFirst = runtime.Scheduler.Diagnostics.AcceptedCues;
        Assert.True(afterFirst > 0);
        Run(runtime, TimeSpan.FromSeconds(1));
        Assert.True(runtime.Scheduler.Diagnostics.AcceptedCues > afterFirst);
        Assert.Equal(AvatarLipSyncQuality.EstimatedText, runtime.Input.LipSyncQuality);
    }

    [Fact]
    public void AWordAlreadyGuessedIsNotMouthedAgain()
    {
        var (runtime, voice, driver) = Create();
        _ = driver.SpeakAsync("one two");
        var utterance = voice.Current!;
        utterance.OnStarted();
        Run(runtime, TimeSpan.FromSeconds(3));
        var guessed = runtime.Scheduler!.Diagnostics.AcceptedCues;
        Assert.True(guessed > 0);

        utterance.OnWord(0, 3);
        utterance.OnWord(4, 3);
        Run(runtime, TimeSpan.FromMilliseconds(100));
        Assert.Equal(guessed, runtime.Scheduler.Diagnostics.AcceptedCues);
    }

    [Fact]
    public void InterruptStopsTheVoice()
    {
        var (runtime, voice, driver) = Create();
        var done = driver.SpeakAsync("a long answer that the user talks over");
        voice.Current!.OnStarted();
        Run(runtime, TimeSpan.FromMilliseconds(100));

        runtime.Interrupt();
        Run(runtime, TimeSpan.FromMilliseconds(32));

        Assert.Equal(["a long answer that the user talks over"], voice.Stopped);
        Assert.Equal(SpeechEndReason.Stopped, done.Result);
        Assert.Equal(AvatarState.Interrupted, runtime.Input.State);
    }

    [Fact]
    public void TimedVisemesReplaceTheGuesses()
    {
        var (runtime, voice, driver) = Create();
        _ = driver.SpeakAsync("Hej");
        var utterance = voice.Current!;
        utterance.OnStarted();
        utterance.OnViseme(21, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(80));
        utterance.OnViseme(2, TimeSpan.FromMilliseconds(130), TimeSpan.FromMilliseconds(120));
        Run(runtime, TimeSpan.FromMilliseconds(32));

        Assert.Equal(AvatarLipSyncQuality.TimedVisemes, runtime.Input.LipSyncQuality);
        Assert.Equal(2, runtime.Scheduler!.Diagnostics.AcceptedCues);
    }

    [Fact]
    public void ANewTextStopsTheOldOne()
    {
        var (_, voice, driver) = Create();
        var first = driver.SpeakAsync("first");
        voice.Current!.OnStarted();
        var second = driver.SpeakAsync("second");

        Assert.Equal(SpeechEndReason.Stopped, first.Result);
        Assert.Equal("second", voice.Current!.Text);
        Assert.False(second.IsCompleted);
    }

    private sealed class FakeVoice(TimeProvider time) : QueuedSpeech(a => a(), time)
    {
        public SpeechUtterance? Current { get; private set; }

        public List<string> Stopped { get; } = [];

        public override bool IsSupported => true;

        public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpeechVoice>>([]);

        public void Complete(SpeechUtterance utterance) => Finished(utterance, SpeechEndReason.Completed);

        protected override void StartNative(SpeechUtterance utterance) => Current = utterance;

        protected override void StopNative(SpeechUtterance utterance)
        {
            Stopped.Add(utterance.Text);
            Finished(utterance, SpeechEndReason.Stopped);
        }
    }
}
