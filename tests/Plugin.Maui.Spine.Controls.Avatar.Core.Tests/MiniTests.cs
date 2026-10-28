using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Minis v2 (#701): one package, seven hand-drawn shapes with round or tall eyes, and the nine figures as presets.
[Collection(Measurements.Name)]
public class MiniTests
{
    private static readonly AvatarPackage Package = AvatarArchiveTests.Load("mini");

    public static TheoryData<string> Presets => ["brown", "red", "orange", "amber", "green", "teal", "blue", "purple", "pink"];
    public static TheoryData<string> Shapes => ["ball", "cube", "drop", "bowl", "bean", "inverted-drop", "marshmallow"];
    public static TheoryData<string, string> Faces => new()
    {
        { "ball", "round" }, { "ball", "tall" }, { "cube", "round" }, { "cube", "tall" }, { "drop", "round" }, { "drop", "tall" },
        { "bowl", "round" }, { "bowl", "tall" }, { "bean", "round" }, { "bean", "tall" }, { "inverted-drop", "round" },
        { "inverted-drop", "tall" }, { "marshmallow", "round" }, { "marshmallow", "tall" },
    };

    private static readonly Dictionary<string, string> NoChoices = new(StringComparer.Ordinal);

    // A figure drawn at 200 px: a preset (its options and colours), with options over it.
    private static SKBitmap Render(string? preset, Action<AvatarRenderFrame> setup, IReadOnlyDictionary<string, string>? options = null, bool dark = false)
    {
        using var renderer = SkiaAvatarModel.Compile(Package, Package.Manifest.Representations[0]).CreateRenderer();
        renderer.Options = AvatarChoices.Options(Package.Manifest, preset, options ?? NoChoices);
        renderer.ThemeColors = AvatarChoices.PresetColors(Package.Manifest, preset, dark).ToDictionary(p => p.Key, p => SKColor.Parse(p.Value));
        var frame = new AvatarRenderFrame { ElapsedSeconds = 2 };
        setup(frame);
        renderer.Evaluate(frame);
        var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Draw(canvas, new SKRect(0, 0, 200, 200), dark);
        return bitmap;
    }

    private static SKBitmap Shape(string shape, Action<AvatarRenderFrame> setup, string eyes = "round") =>
        Render(null, setup, new Dictionary<string, string> { ["shape"] = shape, ["eyes"] = eyes });

    private static void Idle(AvatarRenderFrame f) => f.AddActivity("idle", 1);

    private static void Gesture(AvatarRenderFrame f, string gesture, double progress)
    {
        f.AddActivity("idle", 1);
        f.AddClip(gesture, progress * AvatarShaderContract.GestureSeconds[gesture], 1, AvatarClipLayer.Gesture);
    }

    private static bool IsEye(SKColor c) => c is { Alpha: > 200, Red: > 225, Green: > 225, Blue: > 225 };

    private static List<(int X, int Y)> Pixels(SKBitmap b, Func<SKColor, bool> keep)
    {
        var pixels = new List<(int, int)>();
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
                if (keep(b.GetPixel(x, y))) pixels.Add((x, y));
        return pixels;
    }

    private static (double X, double Y) Centre(List<(int X, int Y)> pixels) => (pixels.Average(p => p.X), pixels.Average(p => p.Y));

    private static (int Top, int Bottom) Extent(SKBitmap b)
    {
        var body = Pixels(b, c => c.Alpha > 128);
        return (body.Min(p => p.Y), body.Max(p => p.Y));
    }

    // How far b is turned from reference, in degrees, positive clockwise (the top to the right): the rotation of the
    // reference's body about its centre that covers b's body best. A centroid line would mislead on a wide shape,
    // whose far end swings into the other half as it turns.
    private static double Turn(SKBitmap reference, SKBitmap b)
    {
        var from = Pixels(reference, c => c.Alpha > 128);
        var to = Pixels(b, c => c.Alpha > 128).ToHashSet();
        var (fx, fy) = Centre(from);
        var (tx, ty) = Centre([.. to]);
        var best = (Angle: 0.0, Hits: -1);
        for (var degrees = -12.0; degrees <= 12.0; degrees += 0.25)
        {
            var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
            var hits = from.Count(p => to.Contains(((int)Math.Round(tx + (p.X - fx) * cos - (p.Y - fy) * sin), (int)Math.Round(ty + (p.X - fx) * sin + (p.Y - fy) * cos))));
            if (hits > best.Hits)
                best = (degrees, hits);
        }
        return best.Angle;
    }

    // The outline's lean in degrees, positive when its top is to the right: the line from the lower half's centre to the upper half's.
    private static double Lean(SKBitmap b)
    {
        var body = Pixels(b, c => c.Alpha > 128);
        var middle = body.Average(p => p.Y);
        var (upper, lower) = (Centre(body.Where(p => p.Y < middle).ToList()), Centre(body.Where(p => p.Y >= middle).ToList()));
        return Math.Atan2(upper.X - lower.X, lower.Y - upper.Y) * 180 / Math.PI;
    }

    // The eyes as blobs, left to right: runs of columns with eye pixels.
    private static List<List<(int X, int Y)>> Eyes(SKBitmap b)
    {
        var eyes = new List<List<(int X, int Y)>>();
        foreach (var p in Pixels(b, IsEye).OrderBy(p => p.X))
        {
            if (eyes.Count == 0 || p.X - eyes[^1].Max(q => q.X) > 2)
                eyes.Add([]);
            eyes[^1].Add(p);
        }
        return eyes;
    }

    // An eye's long axis in degrees from vertical, positive when its top leans right.
    private static double Axis(List<(int X, int Y)> eye)
    {
        var (cx, cy) = Centre(eye);
        double xx = 0, yy = 0, xy = 0;
        foreach (var (x, y) in eye)
        {
            xx += (x - cx) * (x - cx);
            yy += (y - cy) * (y - cy);
            xy += (x - cx) * (y - cy);
        }
        return Math.Atan2(-2 * xy, yy - xx) / 2 * 180 / Math.PI;
    }

    [Fact]
    public void ThePackageIsOneSmallFigureWithItsChoices()
    {
        var m = Package.Manifest;

        Assert.Equal(["ball", "cube", "drop", "bowl", "bean", "inverted-drop", "marshmallow"], m.Options["shape"].Values);
        Assert.Equal(["round", "tall"], m.Options["eyes"].Values);
        Assert.Equal(["brown", "red", "orange", "amber", "green", "teal", "blue", "purple", "pink"], m.Presets.Keys);
        Assert.InRange(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Avatars", "mini.spineavatar")).Length, 1, 40 * 1024);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void EachFigureBlinksAndTalksWithItsEyes(string preset)
    {
        using var open = Render(preset, Idle);
        using var closed = Render(preset, f => { Idle(f); f.Blink = 1; });
        using var quiet = Render(preset, f => f.AddActivity("speaking", 1));
        using var talking = Render(preset, f =>
        {
            f.AddActivity("speaking", 1);
            f.SpeakingWeight = 1;
            f.AddSpeech("viseme_aa", 1);
        });

        var (openEyes, closedEyes) = (Pixels(open, IsEye).Count, Pixels(closed, IsEye).Count);
        var (quietEyes, talkingEyes) = (Pixels(quiet, IsEye), Pixels(talking, IsEye));
        Assert.True(open.GetPixel(100, 100).Alpha > 200 && open.GetPixel(1, 1).Alpha == 0, "a body in the middle, nothing in the corner");
        Assert.True(closedEyes < openEyes * 0.4, $"{closedEyes} eye pixels closed vs {openEyes} open");
        // v2: a syllable grows the eyes a little (v1 grew them by more than 25 %) and lifts them.
        Assert.InRange(talkingEyes.Count / (double)quietEyes.Count, 1.04, 1.3);
        Assert.True(Centre(quietEyes).Y - Centre(talkingEyes).Y > 3, $"a syllable lifts the eyes: y {Centre(quietEyes).Y:0.0} → {Centre(talkingEyes).Y:0.0}");
    }

    [Theory]
    [MemberData(nameof(Faces))]
    public void BothEyesStayOnTheBodyWhenItLooksFarAway(string shape, string eyes)
    {
        using var ahead = Shape(shape, f => f.AddActivity("listening", 1), eyes);
        foreach (var (x, y) in new[] { (-3f, 0f), (3f, 0f), (-3f, 0.5f), (3f, -0.5f) })
        {
            foreach (var surprised in new[] { 0f, 1f })
            {
                using var away = Shape(shape, f =>
                {
                    f.AddActivity("listening", 1);
                    f.AddExpression("surprised", surprised);
                    f.LookX = x;
                    f.LookY = y;
                }, eyes);
                Assert.True(Eyes(away).Count == 2, $"{Eyes(away).Count} eyes looking ({x}, {y}), surprised {surprised}");
                Assert.True(Pixels(away, IsEye).Count > Pixels(ahead, IsEye).Count * 0.5, $"looking ({x}, {y}) leaves too little eye");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EachShapeStandsUprightAndLeansIntoALook(string shape)
    {
        using var rest = Shape(shape, Idle);
        using var right = Shape(shape, f => { Idle(f); f.LookX = 0.5f; });
        using var left = Shape(shape, f => { Idle(f); f.LookX = -0.5f; });

        Assert.True(Math.Abs(Lean(rest)) < 1.5, $"leans {Lean(rest):0.0}° at rest");
        // A ball looks the same turned; its turn shows in its eyes and highlight, not in its outline.
        if (shape == "ball")
            return;
        Assert.InRange(Turn(rest, right), 2, 8);
        Assert.InRange(Turn(rest, left), -8, -2);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TallEyesLeanWithTheFigure(string shape)
    {
        using var rest = Shape(shape, Idle, "tall");
        using var ahead = Shape(shape, f => f.AddActivity("listening", 1), "tall");
        using var right = Shape(shape, f => { f.AddActivity("listening", 1); f.LookX = 3f; }, "tall");
        using var left = Shape(shape, f => { f.AddActivity("listening", 1); f.LookX = -3f; }, "tall");

        // Upright at rest, whichever way the face is turned; parallel, and leaning as the body does, when it looks.
        Assert.All(Eyes(rest).Select(Axis), a => Assert.True(Math.Abs(a) < 2.5, $"an eye leans {a:0.0}° at rest"));
        foreach (var (name, bitmap) in new[] { ("right", right), ("left", left) })
        {
            var axes = Eyes(bitmap).Select(Axis).ToList();
            Assert.True(Math.Abs(axes[0] - axes[1]) < 2.5, $"turned {name}, the eyes lean {axes[0]:0.0}° and {axes[1]:0.0}°");
            // A ball's own turn cannot be read from its outline.
            if (shape != "ball")
                Assert.True(Math.Abs(axes.Average() - Turn(ahead, bitmap)) < 2.5, $"turned {name}: eyes {axes.Average():0.0}°, body {Turn(ahead, bitmap):0.0}°");
        }
    }

    [Theory]
    [MemberData(nameof(Faces))]
    public void TheEyeTurnedAwayStaysInProportion(string shape, string eyes)
    {
        foreach (var x in new[] { 3f, -3f })
        {
            using var turned = Shape(shape, f => { f.AddActivity("listening", 1); f.LookX = x; }, eyes);
            var (left, right) = (Eyes(turned)[0].Count, Eyes(turned)[^1].Count);
            var (far, near) = x > 0 ? (right, left) : (left, right);
            Assert.True(far > near * 0.55, $"looking {(x > 0 ? "right" : "left")}, the far eye has {far} pixels, the near one {near}");
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void TheShadingIsSubtleAndDarkLiftsTheColour(string preset)
    {
        using var light = Render(preset, Idle);
        using var dark = Render(preset, Idle, dark: true);

        static List<double> Luma(SKBitmap b) => [.. Pixels(b, c => c.Alpha == 255 && !IsEye(c)).Select(p => b.GetPixel(p.X, p.Y)).Select(c => 0.3 * c.Red + 0.55 * c.Green + 0.15 * c.Blue).Order()];
        var luma = Luma(light);
        var (low, high, mean) = (luma[luma.Count / 20], luma[luma.Count * 19 / 20], luma.Average());
        Assert.InRange((high - low) / mean, 0.04, 0.3);
        Assert.True(Luma(dark).Average() > mean, "dark mode lifts the body's colour");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void GesturesStayInsideTheFrame(string shape)
    {
        foreach (var gesture in Package.GetMotions(Package.Manifest.Representations[0]).Gestures!.Keys)
        {
            for (var t = 0.05; t < 1; t += 0.05)
            {
                using var bitmap = Shape(shape, f => Gesture(f, gesture, t));
                for (var i = 0; i < 200; i++)
                    Assert.True(bitmap.GetPixel(i, 0).Alpha == 0 && bitmap.GetPixel(i, 199).Alpha == 0 && bitmap.GetPixel(0, i).Alpha == 0 && bitmap.GetPixel(199, i).Alpha == 0,
                        $"{gesture} at {t:0.00} reaches the frame's edge");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NodAndShakeMoveTheEyes(string shape)
    {
        var nod = new[] { 0.125, 0.375, 0.625, 0.875 }.Select(t => { using var b = Shape(shape, f => Gesture(f, "nod", t)); return Centre(Pixels(b, IsEye)); }).ToList();
        var shake = new[] { 0.125, 0.375, 0.625, 0.875 }.Select(t => { using var b = Shape(shape, f => Gesture(f, "shake", t)); return Centre(Pixels(b, IsEye)); }).ToList();

        Assert.True(nod[0].Y - nod[1].Y > 12 && nod[2].Y - nod[3].Y > 8, $"a nod's eyes go down and up: y {string.Join(", ", nod.Select(c => c.Y.ToString("0")))}");
        Assert.True(Math.Abs(shake[0].X - shake[1].X) > 20 && Math.Abs(shake[2].X - shake[3].X) > 12, $"a shake's eyes go side to side: x {string.Join(", ", shake.Select(c => c.X.ToString("0")))}");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheJumpCrouchesLaunchesAndSettles(string shape)
    {
        using var rest = Shape(shape, Idle);
        using var crouch = Shape(shape, f => Gesture(f, "jump", 0.28));
        using var launch = Shape(shape, f => Gesture(f, "jump", 0.34));
        using var flight = Shape(shape, f => Gesture(f, "jump", 0.51));
        using var end = Shape(shape, f => Gesture(f, "jump", 0.999));
        var (top, bottom) = Extent(rest);
        var height = bottom - top;

        Assert.True(Extent(crouch).Top - top > height * 0.18 && Math.Abs(Extent(crouch).Bottom - bottom) < 6, $"the crouch pulls together onto its foot: {Extent(crouch)} vs {(top, bottom)}");
        Assert.True(Extent(launch).Bottom - Extent(launch).Top > height * 1.05, $"the launch stretches: {Extent(launch)} vs {(top, bottom)}");
        Assert.True(bottom - Extent(flight).Bottom > 15, $"the flight leaves the ground: {Extent(flight)} vs {(top, bottom)}");
        Assert.True(Math.Abs(Extent(end).Top - top) <= 1 && Math.Abs(Extent(end).Bottom - bottom) <= 1, $"back at rest: {Extent(end)} vs {(top, bottom)}");
    }
}
