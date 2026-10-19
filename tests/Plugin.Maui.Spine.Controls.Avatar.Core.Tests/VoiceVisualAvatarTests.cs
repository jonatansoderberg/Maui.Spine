using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

/// <summary>VoiceView's bars and ring as avatars (#630): one node per band, each on its own band parameter.</summary>
[Collection(Measurements.Name)]
public class VoiceVisualAvatarTests
{
    private const int Size = 300;
    private static readonly SKColor Output = SKColor.Parse("#FF7A45");
    private static readonly SKColor Input = SKColor.Parse("#1E88E5");

    private static Spine2dRenderer Renderer(string avatar)
    {
        var package = AvatarArchiveTests.Load(avatar);
        return new Spine2dRenderer(Spine2dModel.Compile(package, package.Manifest.Representations[0]));
    }

    /// <summary>Speaking with the answer's bands rising from low to high, and the microphone's falling.</summary>
    private static AvatarRenderFrame Ramp()
    {
        var frame = new AvatarRenderFrame { InputReactiveWeight = 1, OutputReactiveWeight = 1, InputLevel = 0.5f, OutputLevel = 0.5f };
        frame.AddActivity("speaking", 1);
        for (var b = 0; b < AvatarRenderFrame.BandCount; b++)
        {
            frame.OutputBands[b] = b / (AvatarRenderFrame.BandCount - 1f);
            frame.InputBands[b] = 1 - frame.OutputBands[b];
        }
        return frame;
    }

    private static SKBitmap Draw(string avatar, AvatarRenderFrame frame, string name, IReadOnlyDictionary<string, SKColor>? colors = null)
    {
        using var renderer = Renderer(avatar);
        renderer.ThemeColors = colors;
        renderer.Evaluate(frame);
        var bitmap = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            renderer.Draw(canvas, SKRect.Create(Size, Size), dark: false);
        }

        // For review beside the sheets.
        var directory = Path.Combine(AppContext.BaseDirectory, "sheets");
        Directory.CreateDirectory(directory);
        using (var image = SKImage.FromBitmap(bitmap))
        using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(Path.Combine(directory, $"{avatar}-{name}.png"), png.ToArray());
        return bitmap;
    }

    private static int Count(SKBitmap bitmap, SKColor color, SKRectI area)
    {
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - color.Red) < 40 && Math.Abs(p.Green - color.Green) < 40 && Math.Abs(p.Blue - color.Blue) < 40)
                    count++;
            }
        return count;
    }

    [Fact]
    public void EachBarFollowsItsOwnBand()
    {
        using var bitmap = Draw("bars", Ramp(), "ramp");

        // The answer's bars rise to the right, the microphone's to the left.
        var middle = Size / 2;
        var leftUp = Count(bitmap, Output, new SKRectI(0, 0, middle, middle));
        var rightUp = Count(bitmap, Output, new SKRectI(middle, 0, Size, middle));
        var leftDown = Count(bitmap, Input, new SKRectI(0, middle, middle, Size));
        var rightDown = Count(bitmap, Input, new SKRectI(middle, middle, Size, Size));
        Assert.True(rightUp > 2 * leftUp, $"answer: left {leftUp}, right {rightUp}");
        Assert.True(leftDown > 2 * rightDown, $"microphone: left {leftDown}, right {rightDown}");
    }

    [Fact]
    public void RingTicksFollowTheirBands()
    {
        var frame = Ramp();
        frame.InputBands.Clear();
        using var bitmap = Draw("ring", frame, "ramp");

        // Low bands meet at the top and high ones at the bottom: the answer reaches further out below.
        var top = Count(bitmap, Output, new SKRectI(0, 0, Size, Size / 4));
        var bottom = Count(bitmap, Output, new SKRectI(0, Size * 3 / 4, Size, Size));
        Assert.True(bottom > 2 * top, $"top {top}, bottom {bottom}");
    }

    [Theory]
    [InlineData("bars")]
    [InlineData("ring")]
    public void TheHostsColoursReplaceTheSlots(string avatar)
    {
        var green = SKColor.Parse("#2E7D32");
        using var bitmap = Draw(avatar, Ramp(), "colours", new Dictionary<string, SKColor> { ["output"] = green });

        Assert.True(Count(bitmap, green, new SKRectI(0, 0, Size, Size)) > 200);
        Assert.Equal(0, Count(bitmap, Output, new SKRectI(0, 0, Size, Size)));
    }

    [Theory]
    [InlineData("bars")]
    [InlineData("ring")]
    public void ReduceMotionShowsTheLevelNotTheSpectrum(string avatar)
    {
        var frame = Ramp();
        frame.AddActivity("reduced_motion", 1);
        using var reduced = Draw(avatar, frame, "reduced");
        using var full = Draw(avatar, Ramp(), "full");

        // The same on both halves: no ramp left in it.
        var left = Count(reduced, Output, new SKRectI(0, 0, Size / 2, Size));
        var right = Count(reduced, Output, new SKRectI(Size / 2, 0, Size, Size));
        Assert.True(left > 50 && Math.Abs(left - right) < Math.Max(left, right) / 4, $"left {left}, right {right}");
    }
}
