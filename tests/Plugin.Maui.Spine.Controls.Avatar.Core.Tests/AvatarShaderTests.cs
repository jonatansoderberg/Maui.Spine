using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

[Collection(Measurements.Name)]
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

    public static TheoryData<string> Minis => ["mini-brown", "mini-red", "mini-orange", "mini-amber", "mini-green", "mini-teal", "mini-blue", "mini-purple", "mini-pink"];

    private static SKBitmap RenderMini(string name, Action<AvatarRenderFrame> setup)
    {
        var package = AvatarArchiveTests.Load(name);
        using var renderer = SkiaAvatarModel.Compile(package, package.Manifest.Representations[0]).CreateRenderer();
        var frame = new AvatarRenderFrame { ElapsedSeconds = 2 };
        setup(frame);
        renderer.Evaluate(frame);
        var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Draw(canvas, new SKRect(0, 0, 200, 200), dark: false);
        return bitmap;
    }

    // The eyes are the only white on a mini.
    private static int EyePixels(SKBitmap b)
    {
        var count = 0;
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y) is { Alpha: > 200, Red: > 225, Green: > 225, Blue: > 225 }) count++;
        return count;
    }

    [Theory]
    [MemberData(nameof(Minis))]
    public void MinisBlinkAndTalkWithTheirEyes(string name)
    {
        using var open = RenderMini(name, f => f.AddActivity("idle", 1));
        using var closed = RenderMini(name, f => { f.AddActivity("idle", 1); f.Blink = 1; });
        using var quiet = RenderMini(name, f => f.AddActivity("speaking", 1));
        using var talking = RenderMini(name, f =>
        {
            f.AddActivity("speaking", 1);
            f.SpeakingWeight = 1;
            f.AddSpeech("viseme_aa", 1);
        });

        Assert.True(open.GetPixel(100, 100).Alpha > 200 && open.GetPixel(1, 1).Alpha == 0, "a body in the middle, nothing in the corner");
        Assert.True(EyePixels(open) > 60, $"{EyePixels(open)} eye pixels open");
        Assert.True(EyePixels(closed) < EyePixels(open) * 0.4, $"{EyePixels(closed)} closed vs {EyePixels(open)} open");
        Assert.True(EyePixels(talking) > EyePixels(quiet) * 1.25, $"{EyePixels(talking)} talking vs {EyePixels(quiet)} quiet");
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
