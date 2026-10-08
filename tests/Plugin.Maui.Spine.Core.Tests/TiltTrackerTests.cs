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

    static TiltTracker Settled(float[] rotation, int frames, int quarterTurns = 0)
    {
        var tracker = new TiltTracker();
        tracker.Update(Flat, 0, quarterTurns);
        for (var i = 1; i <= frames; i++)
            tracker.Update(rotation, i * Frame, quarterTurns);
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
}
