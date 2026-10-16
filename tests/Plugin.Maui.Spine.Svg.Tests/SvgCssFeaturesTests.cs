using System.Text;
using SkiaSharp;
using Xunit;

namespace Plugin.Maui.Spine.Svg.Tests;

/// <summary>What browsers draw that Svg.Skia does not: a CSS conic gradient and an alpha mask, as Figma exports them.</summary>
public class SvgCssFeaturesTests
{
    private static string Conic(string gradient) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" fill="none">
          <foreignObject x="0" y="0" width="100" height="100">
            <div xmlns="http://www.w3.org/1999/xhtml" style="background:{gradient};height:100%;width:100%"></div>
          </foreignObject>
        </svg>
        """;

    private static SKBitmap Render(string source)
    {
        using var svg = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return SKBitmap.Decode(SvgRasterizer.RenderPng(svg, 100, 100, SKColors.Transparent));
    }

    private static void Near(SKColor expected, SKColor actual, int tolerance = 8)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }

    [Fact]
    public void A_conic_gradient_turns_clockwise_from_the_top()
    {
        using var bitmap = Render(Conic("conic-gradient(rgb(255, 0, 0) 0deg, rgb(255, 0, 0) 90deg, rgb(0, 0, 255) 90deg, rgb(0, 0, 255) 360deg)"));

        Near(SKColors.Red, bitmap.GetPixel(75, 25));
        Near(SKColors.Blue, bitmap.GetPixel(75, 75));
        Near(SKColors.Blue, bitmap.GetPixel(25, 75));
        Near(SKColors.Blue, bitmap.GetPixel(25, 25));
    }

    [Fact]
    public void From_turns_the_start_and_stops_can_be_percentages_hex_or_without_a_position()
    {
        using var bitmap = Render(Conic("conic-gradient(from 90deg, #FF0000, #FF0000 25%, rgba(0, 0, 255, 1) 25%, #0000FF)"));

        Near(SKColors.Red, bitmap.GetPixel(75, 75));
        Near(SKColors.Blue, bitmap.GetPixel(75, 25));
        Near(SKColors.Blue, bitmap.GetPixel(25, 75));
    }

    [Fact]
    public void A_conic_gradient_keeps_the_clip_around_its_box()
    {
        using var bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" fill="none">
              <defs><clipPath id="c"><rect x="0" y="0" width="50" height="100" /></clipPath></defs>
              <g clip-path="url(#c)">
                <foreignObject x="0" y="0" width="100" height="100">
                  <div xmlns="http://www.w3.org/1999/xhtml" style="background:conic-gradient(rgb(0, 128, 0) 0deg, rgb(0, 128, 0) 360deg)"></div>
                </foreignObject>
              </g>
            </svg>
            """);

        Near(new SKColor(0, 128, 0), bitmap.GetPixel(25, 50));
        Assert.Equal(0, bitmap.GetPixel(75, 50).Alpha);
    }

    [Fact]
    public void A_foreign_object_without_a_conic_gradient_is_left_alone()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div xmlns="http://www.w3.org/1999/xhtml" style="background:red"></div></foreignObject></svg>""";

        Assert.Null(SvgCssFeatures.Rewrite(svg));
    }

    [Fact]
    public void An_alpha_mask_uses_the_alpha_of_a_dark_colour_not_its_luminance()
    {
        // #E94FFF has about half the luminance of white, so a luminance mask would let half the red through.
        using var bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" fill="none">
              <mask id="m" style="mask-type:alpha" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                <rect x="0" y="0" width="50" height="100" fill="#E94FFF" />
              </mask>
              <g mask="url(#m)"><rect x="0" y="0" width="100" height="100" fill="#FF0000" /></g>
            </svg>
            """);

        Near(SKColors.Red, bitmap.GetPixel(25, 50));
        Assert.Equal(0, bitmap.GetPixel(75, 50).Alpha);
    }

    [Fact]
    public void A_luminance_mask_is_left_as_it_is()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg"><mask id="m"><rect width="1" height="1" fill="#808080" /></mask></svg>""";

        Assert.Null(SvgCssFeatures.Rewrite(svg));
    }

    [Fact]
    public void An_alpha_mask_keeps_its_colours_alpha_and_shows_shapes_without_a_fill()
    {
        using var bitmap = Render("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" fill="none">
              <mask id="m" style="mask-type:alpha" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                <rect x="0" y="0" width="50" height="100" style="fill:rgba(233, 79, 255, 0.5)" />
                <rect x="50" y="0" width="50" height="100" />
              </mask>
              <g mask="url(#m)"><rect x="0" y="0" width="100" height="100" fill="#FF0000" /></g>
            </svg>
            """);

        Assert.InRange(bitmap.GetPixel(25, 50).Alpha, 118, 138);
        Near(SKColors.Red, bitmap.GetPixel(75, 50));
    }

    [Theory]
    [InlineData("conic-gradient(rgb(var(--x)) 0deg, red 1turn)")]
    [InlineData("conic-gradient(rgb(none 0 0) 0deg, #00F 10deg)")]
    [InlineData("conic-gradient(#F00 sideways, #00F)")]
    public void A_conic_gradient_spine_cannot_read_is_left_alone(string gradient) =>
        Assert.Null(SvgCssFeatures.Rewrite(Conic(gradient)));

    [Fact]
    public void A_file_that_is_not_xml_is_left_alone() =>
        Assert.Null(SvgCssFeatures.Rewrite("<svg><foreignObject style='background:conic-gradient(red, blue)'>"));

    [Fact]
    public void Rgb_percentages_are_of_255_and_a_bom_is_skipped()
    {
        Assert.Equal(new SKColor(255, 0, 0, 128), ConicGradient.Colour("rgb(100% 0% 0% / 50%)"));
        Assert.NotNull(SvgCssFeatures.Rewrite("\uFEFF" + Conic("conic-gradient(#F00, #00F)")));
    }
}
