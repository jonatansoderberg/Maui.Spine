using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>
/// Which way <see cref="TiltTracker"/> moves a layer above the screen. The directions follow
/// <c>UIInterpolatingMotionEffect</c>: its viewer offset is positive when the screen turns to the
/// viewer's right or down, and a positive depth then moves right or down.
/// </summary>
public class TiltTrackerTests
{
    const long Frame = 16_666_667;

    static readonly float[] Flat = Rotation(0, 0);

    /// <summary>
    /// A device-to-world rotation, row-major: <paramref name="awayRight"/> degrees with the right edge
    /// turned away from the viewer (about the device's y axis), then <paramref name="towardTop"/> degrees
    /// with the top edge turned toward the viewer (about its x axis).
    /// </summary>
    static float[] Rotation(double awayRight, double towardTop)
    {
        var a = awayRight * Math.PI / 180;
        var b = towardTop * Math.PI / 180;
        double[,] ry = { { Math.Cos(a), 0, Math.Sin(a) }, { 0, 1, 0 }, { -Math.Sin(a), 0, Math.Cos(a) } };
        double[,] rx = { { 1, 0, 0 }, { 0, Math.Cos(b), -Math.Sin(b) }, { 0, Math.Sin(b), Math.Cos(b) } };

        var m = new float[9];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                double sum = 0;
                for (var k = 0; k < 3; k++)
                    sum += ry[row, k] * rx[k, col];
                m[row * 3 + col] = (float)sum;
            }

        return m;
    }

    /// <summary>A sample and a display frame every 1/60 s, as on a 60 Hz phone.</summary>
    static TiltTracker Settled(float[] rotation, int frames, int quarterTurns = 0)
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, quarterTurns);
        for (var i = 1; i <= frames; i++)
        {
            tracker.Update(rotation, i * Frame, quarterTurns);
            tracker.Advance(Frame / 1e9);
        }

        return tracker;
    }

    /// <summary>A tilt held from the start, sampled at one rate and drawn at another, for <paramref name="seconds"/>.</summary>
    static TiltTracker Run(float[] rotation, double seconds, double sampleHz, double frameHz)
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, 0);

        var sample = 1 / sampleHz;
        var frame = 1 / frameHz;
        var nextSample = sample;
        var lastFrame = 0.0;
        for (var t = frame; t <= seconds + 1e-9; t += frame)
        {
            for (; nextSample <= t + 1e-9; nextSample += sample)
                tracker.Update(rotation, (long)Math.Round(nextSample * 1e9), 0);
            tracker.Advance(t - lastFrame);
            lastFrame = t;
        }

        return tracker;
    }

    [Fact]
    public void Starts_at_the_centre()
    {
        var tracker = new TiltTracker();
        tracker.Update(Rotation(15, 10), 0, 0);

        Assert.Equal(0, tracker.X);
        Assert.Equal(0, tracker.Y);
    }

    [Fact]
    public void Right_edge_away_moves_right()
    {
        var tracker = Settled(Rotation(10, 0), 30);

        Assert.True(tracker.X > 0.3, $"X = {tracker.X}");
        Assert.Equal(0, tracker.Y, 3);
    }

    [Fact]
    public void Top_edge_toward_the_viewer_moves_down()
    {
        var tracker = Settled(Rotation(0, 10), 30);

        Assert.True(tracker.Y > 0.3, $"Y = {tracker.Y}");
        Assert.Equal(0, tracker.X, 3);
    }

    [Fact]
    public void A_large_tilt_stops_at_full_depth()
    {
        var tracker = Settled(Rotation(-60, -60), 30);

        Assert.Equal(-1, tracker.X, 1);
        Assert.Equal(-1, tracker.Y, 1);
    }

    [Fact]
    public void A_held_tilt_comes_back_to_the_centre()
    {
        var tilted = Rotation(10, 0);
        var early = Settled(tilted, 30).X;
        var later = Settled(tilted, 60 * 15).X;

        Assert.True(later < early * 0.1, $"after half a second {early}, after 15 s {later}");
    }

    [Theory]
    [InlineData(1, 0, -1)]   // landscape, the device's top to the left: its right edge is the screen's top
    [InlineData(2, -1, 0)]   // upside down
    [InlineData(3, 0, 1)]    // landscape, the device's top to the right
    public void Follows_the_turned_display(int quarterTurns, int expectedX, int expectedY)
    {
        var tracker = Settled(Rotation(10, 0), 30, quarterTurns);

        Assert.Equal(expectedX, Math.Sign(Math.Round(tracker.X, 3)));
        Assert.Equal(expectedY, Math.Sign(Math.Round(tracker.Y, 3)));
    }

    [Fact]
    public void A_sample_sets_the_target_and_leaves_the_layers_to_the_frames()
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, 0);
        tracker.Update(Rotation(10, 0), Frame, 0);

        Assert.True(tracker.TargetX > 0.3, $"TargetX = {tracker.TargetX}");
        Assert.Equal(0, tracker.X);

        Assert.True(tracker.Advance(Frame / 1e9));
        Assert.InRange(tracker.X, 0.0001, tracker.TargetX);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(200, 90)]
    [InlineData(200, 120)]
    [InlineData(50, 144)]
    public void Moves_as_far_at_any_frame_rate(double sampleHz, double frameHz)
    {
        // The same 0.1 s at 60 Hz samples and frames, the old pace.
        var reference = Run(Rotation(10, 0), 0.1, 60, 60).X;
        var tracker = Run(Rotation(10, 0), 0.1, sampleHz, frameHz);

        Assert.Equal(reference, tracker.X, 1);
    }

    [Fact]
    public void Faster_frames_take_smaller_steps()
    {
        var at60 = new TiltTracker();
        var at120 = new TiltTracker();
        foreach (var tracker in new[] { at60, at120 })
        {
            tracker.Update(Flat, 0, 0);
            tracker.Update(Rotation(10, 0), Frame, 0);
        }

        at60.Advance(1.0 / 60);
        at120.Advance(1.0 / 120);
        var firstHalf = at120.X;
        at120.Advance(1.0 / 120);

        Assert.InRange(firstHalf, 0.0001, at60.X * 0.6);
        Assert.Equal(at60.X, at120.X, 9);
    }

    [Fact]
    public void A_frame_after_a_stall_does_not_jump_further_than_a_quarter_second()
    {
        var stalled = new TiltTracker();
        var quarter = new TiltTracker();
        foreach (var tracker in new[] { stalled, quarter })
        {
            tracker.Update(Flat, 0, 0);
            tracker.Update(Rotation(1, 0), Frame, 0);
        }

        stalled.Advance(5);
        quarter.Advance(TiltTracker.MaxStepSeconds);

        Assert.Equal(quarter.X, stalled.X, 9);
    }

    [Fact]
    public void Comes_to_rest_on_the_target()
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, 0);
        tracker.Update(Rotation(10, 0), Frame, 0);

        for (var i = 0; i < 120 && tracker.Advance(1.0 / 120); i++)
        {
        }

        Assert.Equal(tracker.TargetX, tracker.X);
        Assert.True(tracker.IsAtRest);
        Assert.False(tracker.Advance(1.0 / 120));
    }

    static int FramesToRest(double rest)
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, 0);
        tracker.Update(Rotation(10, 0), Frame, 0);

        var frames = 0;
        while (!tracker.IsAtRest && frames < 1000)
        {
            tracker.Advance(1.0 / 90, rest);
            frames++;
        }

        return frames;
    }

    [Fact]
    public void A_coarser_rest_stops_sooner()
    {
        // 0.25 px at a 16 pt depth on a 2× screen, against the default.
        var coarse = FramesToRest(0.25 / 32);
        var fine = FramesToRest(TiltTracker.DefaultRest);

        Assert.InRange(coarse, 1, fine - 10);
    }

    [Fact]
    public void Noise_within_the_rest_does_not_wake_it_and_a_tilt_does()
    {
        const double rest = 0.25 / 32;
        var tracker = Settled(Rotation(0, 0), 60);
        Assert.True(tracker.IsAtRest);

        // A hundredth of a degree: the noise of a device lying on a table.
        tracker.Update(Rotation(0.01, 0.01), 61 * Frame, 0);
        Assert.False(tracker.IsAway(rest));

        tracker.Update(Rotation(1, 0), 62 * Frame, 0);
        Assert.True(tracker.IsAway(rest));
        Assert.True(tracker.Advance(1.0 / 90, rest));
        Assert.False(tracker.IsAtRest);
    }

    [Fact]
    public void Reset_clears_the_target()
    {
        var tracker = Settled(Rotation(10, 0), 30);
        tracker.Reset();

        Assert.Equal(0, tracker.TargetX);
        Assert.Equal(0, tracker.X);
        Assert.False(tracker.Advance(1.0 / 60));
    }
}
