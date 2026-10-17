using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Spike S5 (issue #613): blurred vector scenes on the CPU raster (Android, Windows), drawn directly and with the blurred
// layers cached. Times go to the test output, the cached pictures to bin/…/skia2d/ for review.
[Collection(Measurements.Name)]
public class Spine2dTimingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("pip", 320)]
    [InlineData("pip", 960)]
    [InlineData("aurora", 320)]
    [InlineData("aurora-motion", 320)]
    [InlineData("aurora-motion", 960)]
    public void CachedBlursLookTheSameAndCostLess(string name, int size)
    {
        using var direct = Run(name, size, cache: false);
        using var cached = Run(name, size, cache: true);

        var largest = 0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                SKColor a = direct.GetPixel(x, y), b = cached.GetPixel(x, y);
                largest = Math.Max(largest, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
            }
        output.WriteLine($"{name} {size} px: largest channel difference {largest}");
        Assert.True(largest <= 16, $"cached and direct differ by {largest}");

        var directory = Path.Combine(AppContext.BaseDirectory, "skia2d");
        Directory.CreateDirectory(directory);
        using var png = cached.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, $"{name}-{size}.png"), png.ToArray());
    }

    private SKBitmap Run(string name, int size, bool cache)
    {
        var package = AvatarArchiveTests.Load(name);
        var representation = package.Manifest.Representations[0];
        var time = new FakeTimeProvider();
        var scheduler = new AvatarScheduler(package, representation, time, 1);
        using var renderer = (Spine2dRenderer)SkiaAvatarModel.Compile(package, representation).CreateRenderer();
        renderer.CacheBlurs = cache;
        var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        var bounds = new SKRect(0, 0, size, size);

        scheduler.SetState(AvatarState.Speaking);
        scheduler.SetOutputLevel(0.6f);
        void Frame()
        {
            time.Advance(TimeSpan.FromMilliseconds(33));
            renderer.Evaluate(scheduler.Update());
            canvas.Clear(SKColors.White);
            renderer.Draw(canvas, bounds, dark: false);
            canvas.Flush();
        }
        for (var i = 0; i < 20; i++)
            Frame();

        const int frames = 120;
        var times = new double[frames];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < frames; i++)
        {
            var start = Stopwatch.GetTimestamp();
            Frame();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / frames;
        Array.Sort(times);
        output.WriteLine($"{name} {size} px {(cache ? "cached" : "direct")}: p50 {times[frames / 2]:0.00} ms, p95 {times[(int)(frames * 0.95)]:0.00} ms, {bytes} B per frame (CPU raster)");
        return bitmap;
    }
}
