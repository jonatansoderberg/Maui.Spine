using System.Text;
using Plugin.Maui.Spine.Svg;
using SkiaSharp;
using Xunit;

namespace Plugin.Maui.Spine.Svg.Tests;

/// <summary>How a tint changes an SVG's pixels (#281).</summary>
public class SvgRasterizerTests
{
    /// <summary>A red square on the left, a blue one on the right, nothing in between.</summary>
    private const string TwoColours = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40">
          <rect x="0" y="0" width="40" height="40" fill="#FF0000" />
          <rect x="60" y="0" width="40" height="40" fill="#0000FF" />
        </svg>
        """;

    /// <summary>A <c>currentColor</c> square on the left, a yellow one on the right.</summary>
    private const string InkAndSun = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40">
          <rect x="0" y="0" width="40" height="40" fill="currentColor" />
          <rect x="60" y="0" width="40" height="40" fill="#FFFF00" />
        </svg>
        """;

    private static SKBitmap Render(SKColor tint, string source = TwoColours)
    {
        using var svg = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return SKBitmap.Decode(SvgRasterizer.RenderPng(svg, 100, 40, tint));
    }

    [Fact]
    public void A_transparent_tint_keeps_the_svgs_own_colours()
    {
        using var bitmap = Render(SKColors.Transparent);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), bitmap.GetPixel(20, 20));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), bitmap.GetPixel(80, 20));
        Assert.Equal(0, bitmap.GetPixel(50, 20).Alpha);
    }

    [Fact]
    public void Any_colour_without_alpha_counts_as_no_tint()
    {
        // Transparent black, as XAML's #00000000 gives, rather than Colors.Transparent's transparent white.
        using var bitmap = Render(new SKColor(0, 0, 0, 0));

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00), bitmap.GetPixel(20, 20));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF), bitmap.GetPixel(80, 20));
    }

    [Fact]
    public void A_tint_recolours_what_is_drawn_and_leaves_the_rest_clear()
    {
        using var bitmap = Render(SKColors.White);

        Assert.Equal(SKColors.White, bitmap.GetPixel(20, 20));
        Assert.Equal(SKColors.White, bitmap.GetPixel(80, 20));
        Assert.Equal(0, bitmap.GetPixel(50, 20).Alpha);
    }

    [Fact]
    public void A_tint_recolours_only_the_current_colour_and_keeps_the_rest()
    {
        using var bitmap = Render(SKColors.White, InkAndSun);

        Assert.Equal(SKColors.White, bitmap.GetPixel(20, 20));
        Assert.Equal(new SKColor(0xFF, 0xFF, 0x00), bitmap.GetPixel(80, 20));
        Assert.Equal(0, bitmap.GetPixel(50, 20).Alpha);
    }

    [Fact]
    public void A_tints_alpha_reaches_the_current_colour()
    {
        using var bitmap = Render(SKColors.Red.WithAlpha(0x80), InkAndSun);

        Assert.InRange(bitmap.GetPixel(20, 20).Alpha, 0x7F, 0x81);
        Assert.Equal(new SKColor(0xFF, 0xFF, 0x00), bitmap.GetPixel(80, 20));
    }

    [Fact]
    public void Without_a_tint_the_current_colour_is_black()
    {
        using var bitmap = Render(SKColors.Transparent, InkAndSun);

        Assert.Equal(SKColors.Black, bitmap.GetPixel(20, 20));
        Assert.Equal(new SKColor(0xFF, 0xFF, 0x00), bitmap.GetPixel(80, 20));
    }

    [Fact]
    public void Only_a_tint_with_alpha_gets_a_paint()
    {
        Assert.Null(SvgRasterizer.TintPaint(SKColors.Transparent));
        using var paint = SvgRasterizer.TintPaint(SKColors.Black);
        Assert.NotNull(paint);
    }
}
