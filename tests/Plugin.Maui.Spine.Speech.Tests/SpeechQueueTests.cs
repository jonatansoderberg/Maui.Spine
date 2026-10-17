using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Speech.Tests;

public class SpeechQueueTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void UtterancesPlayOneAfterAnother()
    {
        var engine = new FakeEngine(_time);
        var first = engine.Speak("first");
        var second = engine.Speak("second");

        Assert.Equal(["first"], engine.Started);
        Assert.Equal(SpeechUtteranceState.Queued, second.State);

        engine.Begin(first);
        engine.Complete(first);

        Assert.Equal(["first", "second"], engine.Started);
        Assert.Equal(SpeechEndReason.Completed, first.Completed.Result);
    }

    [Fact]
    public void WordsCarryThePositionTheyWereReachedAt()
    {
        var engine = new FakeEngine(_time);
        var utterance = engine.Speak("Hello there");
        var words = new List<SpeechWord>();
        utterance.Word += (_, w) => words.Add(w);

        engine.Begin(utterance);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        utterance.OnWord(6, 5);
        utterance.OnWord(20, 3);

        Assert.Equal([new SpeechWord(6, 5, TimeSpan.FromMilliseconds(400))], words);
        Assert.Equal(TimeSpan.FromMilliseconds(400), utterance.Position);
    }

    [Fact]
    public void PositionFreezesWhenItEnds()
    {
        var engine = new FakeEngine(_time);
        var utterance = engine.Speak("Hello");
        engine.Begin(utterance);
        _time.Advance(TimeSpan.FromSeconds(1));
        engine.Complete(utterance);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(1), utterance.Position);
        Assert.False(utterance.IsSpeaking);
    }

    [Fact]
    public void StoppingAQueuedUtteranceLeavesTheOneSpeaking()
    {
        var engine = new FakeEngine(_time);
        var first = engine.Speak("first");
        var second = engine.Speak("second");
        var third = engine.Speak("third");
        engine.Begin(first);

        second.Stop();
        engine.Complete(first);

        Assert.Equal(SpeechEndReason.Stopped, second.Completed.Result);
        Assert.Equal(["first", "third"], engine.Started);
        Assert.Empty(engine.Stopped);
    }

    [Fact]
    public void StoppingTheOneSpeakingAsksThePlatformAndStartsTheNext()
    {
        var engine = new FakeEngine(_time);
        var first = engine.Speak("first");
        var second = engine.Speak("second");
        engine.Begin(first);

        first.Stop();

        Assert.Equal(["first"], engine.Stopped);
        Assert.Equal(SpeechEndReason.Stopped, first.Completed.Result);
        Assert.Equal(["first", "second"], engine.Started);
    }

    [Fact]
    public void StopAllEndsEveryUtterance()
    {
        var engine = new FakeEngine(_time);
        var first = engine.Speak("first");
        var second = engine.Speak("second");
        engine.Begin(first);

        engine.StopAll();

        Assert.Equal(SpeechEndReason.Stopped, first.Completed.Result);
        Assert.Equal(SpeechEndReason.Stopped, second.Completed.Result);
        Assert.Equal(["first"], engine.Started);
    }

    [Fact]
    public void EndedIsRaisedOnce()
    {
        var engine = new FakeEngine(_time);
        var utterance = engine.Speak("x");
        var ended = 0;
        utterance.Ended += (_, _) => ended++;
        engine.Begin(utterance);

        engine.Complete(utterance);
        utterance.OnEnded(SpeechEndReason.Failed);
        utterance.Stop();

        Assert.Equal(1, ended);
        Assert.Equal(SpeechEndReason.Completed, utterance.Completed.Result);
    }

    [Fact]
    public void AnEngineThatThrowsFailsTheUtteranceAndGoesOn()
    {
        var engine = new FakeEngine(_time) { ThrowOn = "bad" };
        var bad = engine.Speak("bad");
        var good = engine.Speak("good");

        Assert.Equal(SpeechEndReason.Failed, bad.Completed.Result);
        Assert.Equal(["bad", "good"], engine.Started);
    }

    [Fact]
    public void AnUnsupportedPlatformFailsAtOnce()
    {
        var speech = new UnsupportedSpeech(a => a());

        Assert.False(speech.IsSupported);
        Assert.Equal(SpeechEndReason.Failed, speech.Speak("hello").Completed.Result);
    }

    [Fact]
    public void TheBestVoiceForTheLanguageIsPicked()
    {
        SpeechVoice[] voices =
        [
            new("a", "Alva", "sv-SE", SpeechVoiceQuality.Default),
            new("b", "Klara", "sv-SE", SpeechVoiceQuality.Premium),
            new("c", "Sara", "sv-FI", SpeechVoiceQuality.Premium),
            new("d", "Daniel", "en-GB", SpeechVoiceQuality.Enhanced),
            new("e", "Svea", "sva", SpeechVoiceQuality.Premium),
        ];

        Assert.Equal("b", SpeechVoices.Pick(voices, new SpeakOptions { Language = "sv-SE" })!.Id);
        Assert.Equal("c", SpeechVoices.Pick(voices, new SpeakOptions { Language = "sv-FI" })!.Id);
        Assert.Equal("a", SpeechVoices.Pick(voices, new SpeakOptions { Language = "sv-SE", VoiceId = "a" })!.Id);
        Assert.Equal(["b", "c", "a"], SpeechVoices.For(voices, "sv").Select(v => v.Id));
        Assert.Null(SpeechVoices.Pick(voices, new SpeakOptions()));
    }

    private sealed class FakeEngine(TimeProvider time) : QueuedSpeech(a => a(), time)
    {
        public List<string> Started { get; } = [];

        public List<string> Stopped { get; } = [];

        public string? ThrowOn { get; init; }

        public override bool IsSupported => true;

        public override Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(string? language = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpeechVoice>>([]);

        public void Begin(SpeechUtterance utterance) => utterance.OnStarted();

        public void Complete(SpeechUtterance utterance) => Finished(utterance, SpeechEndReason.Completed);

        protected override void StartNative(SpeechUtterance utterance)
        {
            Started.Add(utterance.Text);
            if (utterance.Text == ThrowOn)
                throw new InvalidOperationException("the platform refused it");
        }

        protected override void StopNative(SpeechUtterance utterance)
        {
            Stopped.Add(utterance.Text);
            Finished(utterance, SpeechEndReason.Stopped);
        }
    }
}
