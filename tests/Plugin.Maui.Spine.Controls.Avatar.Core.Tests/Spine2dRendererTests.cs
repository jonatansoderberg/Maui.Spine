using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class Spine2dRendererTests
{
    private static Spine2dRenderer Renderer(string avatar = "dotling")
    {
        var package = AvatarArchiveTests.Load(avatar);
        return new Spine2dRenderer(Spine2dModel.Compile(package, package.Manifest.Representations[0]));
    }

    private static AvatarRenderFrame Speaking(string expression, string viseme, float scale = 0.45f)
    {
        var frame = new AvatarRenderFrame { ExpressionMouthScale = scale, SpeakingWeight = 1 };
        frame.AddActivity("speaking", 1);
        frame.AddExpression(expression, 1);
        frame.AddSpeech(viseme, 1);
        return frame;
    }

    [Theory]
    [InlineData("expr_neutral")]
    [InlineData("expr_happy")]
    [InlineData("expr_surprised")]
    [InlineData("expr_confident")]
    [InlineData("expr_concerned")]
    [InlineData("expr_thinking")]
    public void ClosedConsonantStaysClosedUnderEveryExpression(string expression)
    {
        using var renderer = Renderer();
        renderer.Evaluate(Speaking(expression, "viseme_PP"));

        // PP is a 2.4-unit-high sliver in the reference model; the open vowels are well over 10.
        Assert.InRange(renderer.PathBounds("mouth").Height, 0, 3);
    }

    [Fact]
    public void OpenVowelOpensTheMouth()
    {
        using var renderer = Renderer();
        renderer.Evaluate(Speaking("expr_neutral", "viseme_aa"));
        Assert.True(renderer.PathBounds("mouth").Height > 10);
    }

    [Fact]
    public void ExpressionMouthIsDampedWhileSpeaking()
    {
        using var renderer = Renderer();
        var frame = new AvatarRenderFrame { ExpressionMouthScale = 0.45f };
        frame.AddActivity("idle", 1);
        frame.AddExpression("expr_surprised", 1);
        renderer.Evaluate(frame);
        var silent = renderer.PathBounds("mouth").Height;

        frame.SpeakingWeight = 1;
        renderer.Evaluate(frame);
        var speaking = renderer.PathBounds("mouth").Height;

        Assert.True(speaking < silent * 0.8f, $"silent {silent}, speaking {speaking}");
    }

    [Fact]
    public void BlinkMultipliesTheExpressionInsteadOfOverwritingIt()
    {
        using var renderer = Renderer();
        var frame = new AvatarRenderFrame();
        frame.AddActivity("idle", 1);
        frame.AddExpression("expr_happy", 1);
        renderer.Evaluate(frame);
        var open = renderer.Value("eyeLeft", "scaleY");

        frame.AddClip("blink", 0.115, 1, AvatarClipLayer.Reflex);
        renderer.Evaluate(frame);

        Assert.Equal(0.72f, open, 3);
        Assert.Equal(0.72f * 0.04f, renderer.Value("eyeLeft", "scaleY"), 3);
    }

    [Theory]
    [InlineData("pip", 0.0)]
    [InlineData("pip", 1.3)]
    [InlineData("aurora", 2.0)]
    [InlineData("dotling", 1.3)]
    public void IdleClipKeepsTheRootNearRest(string avatar, double time)
    {
        // 1.1 clips are offsets and 1.0 clips absolute values; read the wrong way the root leaves the view.
        using var renderer = Renderer(avatar);
        var frame = new AvatarRenderFrame();
        frame.AddActivity("idle", 1);
        renderer.Evaluate(frame);
        var rest = renderer.Value("root", "y");

        frame.AddClip("idle_a", time, 1, AvatarClipLayer.Idle);
        renderer.Evaluate(frame);

        Assert.InRange(renderer.Value("root", "y") - rest, -6, 6);
    }

    [Fact]
    public void EvaluatingAgainGivesTheSameFrame()
    {
        using var renderer = Renderer();
        var frame = Speaking("expr_happy", "viseme_O");
        frame.AddClip("idle_a", 1.3, 1, AvatarClipLayer.Idle);
        renderer.Evaluate(frame);
        var first = renderer.Value("root", "y");
        for (var i = 0; i < 100; i++)
            renderer.Evaluate(frame);
        Assert.Equal(first, renderer.Value("root", "y"));
    }

    [Fact]
    public void EvaluateAndDrawDoNotAllocate()
    {
        using var renderer = Renderer();
        using var surface = SKSurface.Create(new SKImageInfo(320, 320));
        var frame = Speaking("expr_happy", "viseme_aa");
        frame.AddClip("idle_a", 1.3, 1, AvatarClipLayer.Idle);
        frame.AddClip("blink", 0.1, 1, AvatarClipLayer.Reflex);
        renderer.Evaluate(frame);
        renderer.Draw(surface.Canvas, new SKRect(0, 0, 320, 320), dark: false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            renderer.Evaluate(frame);
            renderer.Draw(surface.Canvas, new SKRect(0, 0, 320, 320), dark: i % 2 == 0);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Spine2d11SheetRendersWithoutAllocatingPerFrame()
    {
        var package = AvatarArchive.Read(AvatarArchiveTests.Upgraded());
        var directory = Path.Combine(AppContext.BaseDirectory, "sheets");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "dotling-1.1-expressions-light.png"),
            AvatarSheet.RenderPng(package, package.Manifest.Representations[0], AvatarSheetKind.Expressions, dark: false));

        using var renderer = new Spine2dRenderer(Spine2dModel.Compile(package, package.Manifest.Representations[0]));
        using var surface = SKSurface.Create(new SKImageInfo(320, 320));
        var frame = Speaking("expr_happy", "viseme_aa");
        renderer.Evaluate(frame);
        renderer.Draw(surface.Canvas, new SKRect(0, 0, 320, 320), dark: false);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            renderer.Evaluate(frame);
            renderer.Draw(surface.Canvas, new SKRect(0, 0, 320, 320), dark: false);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    public static TheoryData<string, AvatarSheetKind> Sheets()
    {
        var data = new TheoryData<string, AvatarSheetKind>();
        foreach (var avatar in new[] { "dotling", "voice-totem", "pip", "aurora" })
            foreach (var kind in Enum.GetValues<AvatarSheetKind>())
                data.Add(avatar, kind);
        return data;
    }

    // The sheets land in the test output for review next to the bundle's own previews.
    [Theory]
    [MemberData(nameof(Sheets))]
    public void SheetsRender(string avatar, AvatarSheetKind kind)
    {
        var package = AvatarArchiveTests.Load(avatar);
        var directory = Path.Combine(AppContext.BaseDirectory, "sheets");
        Directory.CreateDirectory(directory);

        foreach (var dark in new[] { false, true })
        {
            var png = AvatarSheet.RenderPng(package, package.Manifest.Representations[0], kind, dark);
            using var bitmap = SKBitmap.Decode(png);
            Assert.NotNull(bitmap);
            File.WriteAllBytes(Path.Combine(directory, $"{avatar}-{kind}-{(dark ? "dark" : "light")}.png".ToLowerInvariant()), png);
        }
    }
}
