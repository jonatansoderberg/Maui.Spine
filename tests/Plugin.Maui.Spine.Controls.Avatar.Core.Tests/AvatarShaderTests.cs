using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class AvatarShaderTests
{
    private static SKBitmap Render(Action<AvatarRenderFrame> setup, bool dark = true)
    {
        var package = AvatarArchiveTests.Load("aurora-flow");
        using var renderer = SkiaAvatarModel.Compile(package, package.Manifest.Representations[0]).CreateRenderer();
        var frame = new AvatarRenderFrame { ElapsedSeconds = 2 };
        setup(frame);
        renderer.Evaluate(frame);
        var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Draw(canvas, new SKRect(0, 0, 200, 200), dark);
        return bitmap;
    }

    private static int Lit(SKBitmap b)
    {
        var count = 0;
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).Alpha > 128) count++;
        return count;
    }

    [Fact]
    public void IdleGlowsInTheMiddleAndFadesBeforeTheEdges()
    {
        using var idle = Render(f => f.AddActivity("idle", 1));

        Assert.True(idle.GetPixel(100, 100).Alpha > 80);
        Assert.All(new[] { (2, 2), (100, 2), (2, 100), (197, 100) }, p => Assert.True(idle.GetPixel(p.Item1, p.Item2).Alpha < 16));
    }

    [Fact]
    public void VoiceGrowsTheRibbons()
    {
        using var quiet = Render(f => f.AddActivity("speaking", 1));
        using var loud = Render(f =>
        {
            f.AddActivity("speaking", 1);
            f.OutputReactiveWeight = 1;
            f.OutputLevel = 1;
            f.OutputBands.Fill(0.8f);
        });

        Assert.True(Lit(loud) > Lit(quiet) * 1.5, $"{Lit(loud)} vs {Lit(quiet)}");
    }

    [Fact]
    public void UnknownUniformIsRefusedWithItsName()
    {
        var bytes = AvatarArchiveTests.Load("aurora-flow").GetFile("models/aurora-flow.sksl").ToArray();
        var source = System.Text.Encoding.UTF8.GetString(bytes).Replace("uniform float mouth;", "uniform float mouth;\nuniform float sparkle;");

        var unknown = AvatarShaderContract.Declarations(source).Where(d => !AvatarShaderContract.Uniforms.ContainsKey(d.Name)).Select(d => d.Name);
        Assert.Contains("sparkle", unknown);
    }
}
