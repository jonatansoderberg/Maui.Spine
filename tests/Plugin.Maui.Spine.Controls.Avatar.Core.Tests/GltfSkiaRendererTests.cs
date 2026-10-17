using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// The Skia glTF renderer (spike S2, issue #607): which models it takes, and its cost per frame, written to the test output
// with its pictures for review.
[Collection(Measurements.Name)]
public class GltfSkiaRendererTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("nova", true)]
    [InlineData("plush-puff", true)]
    [InlineData("pebble-bot", true)]
    [InlineData("robot-expressive", true)]
    [InlineData("kairo", true)]
    [InlineData("mpfb", false)]
    public void TakesModelsWithoutAlphaMasks(string name, bool drawn)
    {
        var package = AvatarArchiveTests.Load(name);
        var representation = package.Manifest.Representations.First(r => r.Format == "glb");
        Assert.Equal(drawn, GltfSkiaModel.TryCreate(package, representation) is not null);
    }

    [Theory]
    [InlineData("nova", 320)]
    [InlineData("nova", 960)]
    [InlineData("plush-puff", 320)]
    [InlineData("pebble-bot", 320)]
    [InlineData("robot-expressive", 320)]
    public void DrawsAndTimes(string name, int size)
    {
        var package = AvatarArchiveTests.Load(name);
        var representation = package.Manifest.Representations[0];
        var time = new FakeTimeProvider();
        var scheduler = new AvatarScheduler(package, representation, time, 1);
        using var renderer = new GltfSkiaModel(package, representation).CreateRenderer();
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        var bounds = new SKRect(0, 0, size, size);

        scheduler.SetState(AvatarState.Speaking);
        scheduler.SetOutputLevel(0.6f);
        for (var i = 0; i < 20; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(33));
            renderer.Evaluate(scheduler.Update());
            canvas.Clear(SKColors.White);
            renderer.Draw(canvas, bounds, dark: false);
        }

        const int frames = 120;
        var times = new double[frames];
        var watch = new Stopwatch();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < frames; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(33));
            var frame = scheduler.Update();
            watch.Restart();
            renderer.Evaluate(frame);
            canvas.Clear(SKColors.White);
            renderer.Draw(canvas, bounds, dark: false);
            surface.Flush();
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / frames;
        Array.Sort(times);
        var phases = ((GltfSkiaRenderer)renderer).LastPhases;
        output.WriteLine($"{name} {size} px: last frame pose {phases.Pose:0.00} ms, sort {phases.Sort:0.00} ms, raster {phases.Raster:0.00} ms");
        output.WriteLine($"{name} {size} px: p50 {times[frames / 2]:0.00} ms, p95 {times[(int)(frames * 0.95)]:0.00} ms, {bytes} B per frame (CPU raster, {size} px, Mac)");

        var directory = Path.Combine(AppContext.BaseDirectory, "skia3d");
        Directory.CreateDirectory(directory);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, $"{name}-{size}.png"), png.ToArray());
    }
}
