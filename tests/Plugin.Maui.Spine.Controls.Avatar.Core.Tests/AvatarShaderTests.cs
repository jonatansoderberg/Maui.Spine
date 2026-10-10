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

    [Fact]
    public void ShaderPaintsAnOrbWithTransparentCorners()
    {
        using var idle = Render(f => f.AddActivity("idle", 1));

        Assert.Equal(255, idle.GetPixel(100, 100).Alpha);
        Assert.True(idle.GetPixel(2, 2).Alpha < 20);
    }

    [Fact]
    public void VoiceWidensTheOrb()
    {
        static int Covered(SKBitmap b)
        {
            var count = 0;
            for (var x = 0; x < b.Width; x++)
                if (b.GetPixel(x, 100).Alpha > 200) count++;
            return count;
        }
        using var idle = Render(f => f.AddActivity("speaking", 1));
        using var loud = Render(f =>
        {
            f.AddActivity("speaking", 1);
            f.OutputReactiveWeight = 1;
            f.OutputLevel = 1;
            f.OutputBands.Fill(0.8f);
        });

        Assert.True(Covered(loud) > Covered(idle) + 6, $"{Covered(loud)} vs {Covered(idle)}");
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
