using Microsoft.Extensions.Time.Testing;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class AvatarSchedulerTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeClock _clock = new();

    private AvatarScheduler Create(string avatar = "dotling", int seed = 7)
    {
        var package = AvatarArchiveTests.Load(avatar);
        var scheduler = new AvatarScheduler(package, package.Manifest.Representations[0], _time, seed) { PlaybackClock = _clock };
        scheduler.Update();
        return scheduler;
    }

    private AvatarRenderFrame Run(AvatarScheduler scheduler, TimeSpan duration, bool advanceClock = true)
    {
        var frame = scheduler.Update();
        for (var t = TimeSpan.Zero; t < duration; t += Frame)
        {
            _time.Advance(Frame);
            if (advanceClock && _clock.IsPlaying)
                _clock.Position += Frame;
            frame = scheduler.Update();
        }
        return frame;
    }

    private static float Weight(ReadOnlySpan<AvatarPoseWeight> poses, string pose)
    {
        foreach (var p in poses)
            if (p.Pose == pose) return p.Weight;
        return 0;
    }

    private static float Sum(ReadOnlySpan<AvatarPoseWeight> poses)
    {
        float sum = 0;
        foreach (var p in poses) sum += p.Weight;
        return sum;
    }

    [Fact]
    public void SameSeedAndClockGiveTheSameFrames()
    {
        var a = Create(seed: 3);
        var b = new AvatarScheduler(AvatarArchiveTests.Load("dotling"), AvatarArchiveTests.Load("dotling").Manifest.Representations[0], _time, 3);
        var c = new AvatarScheduler(AvatarArchiveTests.Load("dotling"), AvatarArchiveTests.Load("dotling").Manifest.Representations[0], _time, 4);
        var differs = false;

        for (var i = 0; i < 2000; i++)
        {
            _time.Advance(Frame);
            var fa = a.Update();
            var fb = b.Update();
            var fc = c.Update();
            Assert.Equal(fa.Blink, fb.Blink);
            Assert.Equal(fa.Clips.ToArray(), fb.Clips.ToArray());
            differs |= fa.Blink != fc.Blink;
        }
        Assert.True(differs, "another seed should blink at other times");
    }

    [Fact]
    public void BlinksFollowTheManifestInterval()
    {
        var scheduler = Create();
        var starts = new List<double>();
        var wasOpen = true;
        for (var i = 0; i < 120_000 / 16; i++)
        {
            _time.Advance(Frame);
            var frame = scheduler.Update();
            if (frame.Blink > 0 && wasOpen)
                starts.Add(frame.ElapsedSeconds);
            wasOpen = frame.Blink == 0;
        }

        var gaps = starts.Zip(starts.Skip(1), (x, y) => y - x).ToList();
        Assert.True(gaps.Count > 15);
        Assert.All(gaps, gap => Assert.True(gap is > 0.3 and < 0.36 || gap is >= 2.79 and <= 6.52, $"gap {gap:F3}s"));
        Assert.Contains(gaps, gap => gap < 0.36);
    }

    [Fact]
    public void VisemesFollowThePlaybackClock()
    {
        var scheduler = Create();
        scheduler.SetState(AvatarState.Speaking);
        _clock.Start(generation: 1);
        Assert.True(scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.3), 10));

        var before = Run(scheduler, TimeSpan.FromSeconds(0.3));
        Assert.Equal(0, Weight(before.Speech, "viseme_aa"));

        var during = Run(scheduler, TimeSpan.FromSeconds(0.35));
        Assert.Equal(1, Weight(during.Speech, "viseme_aa"), 3);

        var after = Run(scheduler, TimeSpan.FromSeconds(0.3));
        Assert.Equal(0, Sum(after.Speech));
    }

    [Fact]
    public void ClosedConsonantStaysClosedThroughTheNextLeadIn()
    {
        var scheduler = Create();
        scheduler.SetState(AvatarState.Speaking);
        scheduler.SetExpression(new(AvatarExpression.Happy, 1, TimeSpan.FromSeconds(5)));
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.3), 1);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.2), 10);

        // 0.49 s: inside PP, inside aa's 35 ms lead-in.
        _clock.Position = TimeSpan.FromSeconds(0.49);
        _time.Advance(Frame);
        var frame = scheduler.Update();

        Assert.Equal(1, Weight(frame.Speech, "viseme_PP"), 3);
        Assert.Equal(0, Weight(frame.Speech, "viseme_aa"), 3);
        Assert.Equal(1, frame.SpeakingWeight, 3);
    }

    [Fact]
    public void LeadInBlendsIntoTheNextVowel()
    {
        var scheduler = Create();
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.3), 13);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.2), 10);

        _clock.Position = TimeSpan.FromSeconds(0.49);
        _time.Advance(Frame);
        var frame = scheduler.Update();

        Assert.InRange(Weight(frame.Speech, "viseme_aa"), 0.05f, 0.95f);
        Assert.True(Sum(frame.Speech) <= 1.0001f);
    }

    [Fact]
    public void InterruptClosesTheMouthWithin90MsAndReturnsToIdle()
    {
        var scheduler = Create();
        scheduler.SetState(AvatarState.Speaking);
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.Zero, TimeSpan.FromSeconds(1.5), 10);
        Assert.Equal(1, Weight(Run(scheduler, TimeSpan.FromSeconds(0.4)).Speech, "viseme_aa"), 3);

        scheduler.Interrupt();
        _clock.Flush();
        Assert.Equal(AvatarState.Interrupted, scheduler.State);
        Assert.Equal(0, Sum(Run(scheduler, TimeSpan.FromMilliseconds(90)).Speech));

        Run(scheduler, TimeSpan.FromMilliseconds(130));
        Assert.Equal(AvatarState.Idle, scheduler.State);
    }

    [Fact]
    public void CuesOfAnOlderGenerationAreIgnored()
    {
        var scheduler = Create();
        Assert.True(scheduler.EnqueueViseme(2, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), 10));
        Assert.False(scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), 12));
        Assert.Equal(1, scheduler.Diagnostics.StaleCues);

        // A clock that has moved on to generation 3 drops generation 2's cue.
        _clock.Start(generation: 3);
        var frame = Run(scheduler, TimeSpan.FromSeconds(0.15));
        Assert.Equal(0, Sum(frame.Speech));
        Assert.Equal(0, scheduler.Diagnostics.QueuedCues);
    }

    [Fact]
    public void CuesFarAheadAreRejected()
    {
        var scheduler = Create();
        Assert.False(scheduler.EnqueueViseme(0, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(0.1), 10));
        Assert.Equal(1, scheduler.Diagnostics.RejectedCues);
    }

    [Fact]
    public void OutOfOrderCuesPlayInOrder()
    {
        var scheduler = Create();
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(0.2), 13);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.2), 10);

        Assert.Equal(1, Weight(Run(scheduler, TimeSpan.FromSeconds(0.3)).Speech, "viseme_aa"), 3);
        Assert.Equal(1, Weight(Run(scheduler, TimeSpan.FromSeconds(0.4)).Speech, "viseme_O"), 3);
    }

    [Fact]
    public void PauseFreezesTheMouth()
    {
        var scheduler = Create();
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.4), 10);
        var playing = Weight(Run(scheduler, TimeSpan.FromSeconds(0.2)).Speech, "viseme_aa");

        _clock.IsPlaying = false;
        var paused = Run(scheduler, TimeSpan.FromSeconds(1));

        Assert.Equal(playing, Weight(paused.Speech, "viseme_aa"));
        Assert.Equal(0, scheduler.Diagnostics.Underruns);
    }

    [Fact]
    public void StalledClockClosesTheMouth()
    {
        var scheduler = Create();
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), 10);
        Run(scheduler, TimeSpan.FromSeconds(0.2));

        var stalled = Run(scheduler, TimeSpan.FromSeconds(0.4), advanceClock: false);

        Assert.Equal(1, scheduler.Diagnostics.Underruns);
        Assert.Equal(0, Sum(stalled.Speech));
    }

    [Fact]
    public void ExpressionExpiresBackToTheBase()
    {
        var scheduler = Create();
        scheduler.SetExpression(new(AvatarExpression.Happy, 0.8f, TimeSpan.FromSeconds(1)));

        var shown = Run(scheduler, TimeSpan.FromSeconds(0.5));
        Assert.Equal(0.8f, Weight(shown.Expression, "expr_happy"), 3);

        var back = Run(scheduler, TimeSpan.FromSeconds(1));
        Assert.Equal(0, Weight(back.Expression, "expr_happy"));
        Assert.Equal(0.65f, Weight(back.Expression, "expr_neutral"), 3);
    }

    [Fact]
    public void NonFiniteIntensityIsRejected()
    {
        var scheduler = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.SetExpression(new(AvatarExpression.Happy, float.NaN, TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void UnknownGestureNamesTheKnownOnes()
    {
        var scheduler = Create();
        var error = Assert.Throws<KeyNotFoundException>(() => scheduler.PlayGesture("wave"));
        Assert.Contains("nod", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReducedMotionStopsIdleAndGestures()
    {
        var scheduler = Create();
        scheduler.SetReducedMotion(true);
        var frame = Run(scheduler, TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, scheduler.PlayGesture("nod"));
        foreach (var clip in frame.Clips)
            Assert.Equal(AvatarClipLayer.Reflex, clip.Layer);
        Assert.Equal(1, Weight(frame.Activity, "reduced_motion"), 3);
    }

    [Fact]
    public void LevelsDecayWhenTheFeedStops()
    {
        var scheduler = Create();
        scheduler.SetOutputLevel(0.8f);
        Assert.InRange(Run(scheduler, TimeSpan.FromMilliseconds(150)).OutputLevel, 0.75f, 0.8f);

        Assert.True(Run(scheduler, TimeSpan.FromSeconds(1)).OutputLevel < 0.01f);
    }

    [Fact]
    public void AmbientAvatarHasNoSpeechPoses()
    {
        var scheduler = Create("voice-totem");
        _clock.Start(generation: 1);
        scheduler.EnqueueViseme(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), 10);

        Assert.False(scheduler.HasSpeechArticulation);
        Assert.Equal(0, Run(scheduler, TimeSpan.FromSeconds(0.3)).Speech.Length);
    }

    [Fact]
    public void UpdateDoesNotAllocateAfterWarmUp()
    {
        var scheduler = Create();
        scheduler.SetState(AvatarState.Speaking);
        _clock.Start(generation: 1);
        for (var i = 0; i < 40; i++)
            scheduler.EnqueueViseme(1, TimeSpan.FromSeconds(i * 0.05), TimeSpan.FromSeconds(0.05), i % 15);
        Run(scheduler, TimeSpan.FromSeconds(0.5));

        // Only the scheduler's own calls are measured; the fake time provider allocates when it advances.
        long allocated = 0;
        for (var i = 0; i < 600; i++)
        {
            _time.Advance(Frame);
            _clock.Position += Frame;
            var before = GC.GetAllocatedBytesForCurrentThread();
            scheduler.SetOutputLevel(0.5f);
            scheduler.Update();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void InterruptSquashesThenOvershootsAndSettles()
    {
        var scheduler = Create();
        Run(scheduler, TimeSpan.FromSeconds(1));
        scheduler.Interrupt();

        var squashes = new List<float>();
        for (var i = 0; i < 180; i++)
        {
            _time.Advance(Frame);
            squashes.Add(scheduler.Update().Squash);
        }

        Assert.True(squashes.Min() < -0.02f, $"min {squashes.Min()}");
        Assert.True(squashes.Max() > 0.002f, "an underdamped spring overshoots past rest");
        Assert.InRange(squashes[^1], -0.002f, 0.002f);
    }

    [Fact]
    public void SecondaryMotionIsOffUnderReducedMotion()
    {
        var scheduler = Create();
        scheduler.SetReducedMotion(true);
        scheduler.SetState(AvatarState.Speaking);
        scheduler.SetOutputLevel(0.9f);
        var frame = Run(scheduler, TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, frame.Squash);
        Assert.Equal(0, frame.Lift);
        Assert.Equal(0, frame.Tilt);
    }

    [Fact]
    public void SpringsDoNotDependOnTheFrameRate()
    {
        var a = Create(seed: 1);
        a.SetState(AvatarState.Interrupted);
        var at60 = Run(a, TimeSpan.FromMilliseconds(400)).Squash;

        var time = new FakeTimeProvider();
        var package = AvatarArchiveTests.Load("dotling");
        var b = new AvatarScheduler(package, package.Manifest.Representations[0], time, 1);
        b.Update();
        b.SetState(AvatarState.Interrupted);
        AvatarRenderFrame frame = b.Update();
        for (var t = 0; t < 400; t += 50)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            frame = b.Update();
        }
        Assert.Equal(at60, frame.Squash, 2);
    }

    private sealed class FakeClock : IAvatarPlaybackClock
    {
        public long Generation { get; set; }

        public TimeSpan Position { get; set; }

        public bool IsPlaying { get; set; }

        public void Start(long generation)
        {
            Generation = generation;
            Position = TimeSpan.Zero;
            IsPlaying = true;
        }

        public void Flush()
        {
            Generation++;
            Position = TimeSpan.Zero;
            IsPlaying = false;
        }

        public AvatarPlaybackSnapshot GetSnapshot() =>
            new(Generation, Position, IsPlaying, PositionIsPresentationTime: true, TimeSpan.Zero, TimeSpan.FromMilliseconds(1), 0);
    }
}
