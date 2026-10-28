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

    // Runs of columns with eye pixels, separated by at least two columns without.
    private static int EyeCount(SKBitmap b)
    {
        var columns = Enumerable.Range(0, b.Width).Where(x => Enumerable.Range(0, b.Height).Any(y => b.GetPixel(x, y) is { Alpha: > 200, Red: > 225, Green: > 225, Blue: > 225 })).ToList();
        return columns.Count == 0 ? 0 : 1 + columns.Zip(columns.Skip(1)).Count(p => p.Second - p.First > 2);
    }

    [Theory]
    [MemberData(nameof(Minis))]
    public void MinisKeepBothEyesWhenTheyLookFarAway(string name)
    {
        using var ahead = RenderMini(name, f => f.AddActivity("listening", 1));
        foreach (var (x, y) in new[] { (-3f, 0f), (3f, 0f), (-3f, 0.5f), (3f, -0.5f) })
        {
            foreach (var surprised in new[] { 0f, 1f })
            {
                using var away = RenderMini(name, f =>
                {
                    f.AddActivity("listening", 1);
                    f.AddExpression("surprised", surprised);
                    f.LookX = x;
                    f.LookY = y;
                });
                Assert.True(EyeCount(away) == 2, $"{EyeCount(away)} eyes looking ({x}, {y}), surprised {surprised}");
                Assert.True(EyePixels(away) > EyePixels(ahead) * 0.5, $"{EyePixels(away)} eye pixels looking ({x}, {y}) vs {EyePixels(ahead)} ahead");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Minis))]
    public void MinisPlayGesturesInsideTheirFrame(string name)
    {
        var package = AvatarArchiveTests.Load(name);
        var gestures = package.GetMotions(package.Manifest.Representations[0]).Gestures!;
        Assert.Equal(["dance", "hop", "jump", "lean", "nod", "shake", "wiggle"], gestures.Keys.Order());

        using var still = RenderMini(name, f => f.AddActivity("idle", 1));
        foreach (var gesture in gestures.Keys)
        {
            var seconds = AvatarShaderContract.GestureSeconds[gesture];
            var moved = false;
            for (var t = 0.1; t < 1; t += 0.1)
            {
                using var bitmap = RenderMini(name, f =>
                {
                    f.AddActivity("idle", 1);
                    f.AddClip(gesture, t * seconds, 1, AvatarClipLayer.Gesture);
                });
                for (var i = 0; i < 200; i++)
                    Assert.True(bitmap.GetPixel(i, 0).Alpha == 0 && bitmap.GetPixel(i, 199).Alpha == 0 && bitmap.GetPixel(0, i).Alpha == 0 && bitmap.GetPixel(199, i).Alpha == 0,
                        $"{gesture} at {t:0.0} reaches the frame's edge");
                moved |= Differs(bitmap, still);
            }
            Assert.True(moved, $"{gesture} does not move the figure");
        }

        static bool Differs(SKBitmap a, SKBitmap b) =>
            Enumerable.Range(0, 200 * 200).Count(i => Math.Abs(a.GetPixel(i % 200, i / 200).Alpha - b.GetPixel(i % 200, i / 200).Alpha) > 64) > 200;
    }

    private static SKBitmap RenderGesture(string name, string gesture, double progress) => RenderMini(name, f =>
    {
        f.AddActivity("idle", 1);
        f.AddClip(gesture, progress * AvatarShaderContract.GestureSeconds[gesture], 1, AvatarClipLayer.Gesture);
    });

    private static (double X, double Y) EyeCentre(SKBitmap b)
    {
        double x = 0, y = 0, n = 0;
        for (var j = 0; j < b.Height; j++)
            for (var i = 0; i < b.Width; i++)
                if (b.GetPixel(i, j) is { Alpha: > 200, Red: > 225, Green: > 225, Blue: > 225 }) { x += i; y += j; n++; }
        return (x / n, y / n);
    }

    // The body's top and bottom rows.
    private static (int Top, int Bottom) Extent(SKBitmap b)
    {
        var rows = Enumerable.Range(0, b.Height).Where(j => Enumerable.Range(0, b.Width).Any(i => b.GetPixel(i, j).Alpha > 128)).ToList();
        return (rows[0], rows[^1]);
    }

    [Theory]
    [MemberData(nameof(Minis))]
    public void MinisNodAndShakeWithTheirEyes(string name)
    {
        var nod = new[] { 0.125, 0.375, 0.625, 0.875 }.Select(t => { using var b = RenderGesture(name, "nod", t); return EyeCentre(b); }).ToList();
        var shake = new[] { 0.125, 0.375, 0.625, 0.875 }.Select(t => { using var b = RenderGesture(name, "shake", t); return EyeCentre(b); }).ToList();

        Assert.True(nod[0].Y - nod[1].Y > 12 && nod[2].Y - nod[3].Y > 8, $"a nod's eyes go down and up: y {string.Join(", ", nod.Select(c => c.Y.ToString("0")))}");
        Assert.True(Math.Abs(shake[0].X - shake[1].X) > 20 && Math.Abs(shake[2].X - shake[3].X) > 12, $"a shake's eyes go side to side: x {string.Join(", ", shake.Select(c => c.X.ToString("0")))}");
        Assert.True(Math.Abs(shake[0].X - shake[1].X) > Math.Abs(shake[0].Y - shake[1].Y) * 3, "a shake moves the eyes sideways");
    }

    [Theory]
    [MemberData(nameof(Minis))]
    public void MinisJumpCrouchesRisesAndSettles(string name)
    {
        using var rest = RenderMini(name, f => f.AddActivity("idle", 1));
        using var crouch = RenderGesture(name, "jump", 0.3);
        using var flight = RenderGesture(name, "jump", 0.57);
        using var end = RenderGesture(name, "jump", 0.999);
        var (top, bottom) = Extent(rest);

        Assert.True(Extent(crouch).Top - top > 10 && Math.Abs(Extent(crouch).Bottom - bottom) < 6, $"the crouch squashes onto its foot: {Extent(crouch)} vs {(top, bottom)}");
        Assert.True(bottom - Extent(flight).Bottom > 12, $"the flight leaves the ground: {Extent(flight)} vs {(top, bottom)}");
        Assert.True(Math.Abs(Extent(end).Top - top) <= 1 && Math.Abs(Extent(end).Bottom - bottom) <= 1, $"back at rest: {Extent(end)} vs {(top, bottom)}");
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
