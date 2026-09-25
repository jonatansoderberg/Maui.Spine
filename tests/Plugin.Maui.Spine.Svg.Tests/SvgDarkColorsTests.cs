using System.Drawing;
using System.Text;
using Plugin.Maui.Spine.Svg;
using SkiaSharp;
using Xunit;

namespace Plugin.Maui.Spine.Svg.Tests;

/// <summary>Dark tones for an SVG's own colours, and stroke widths.</summary>
public class SvgDarkColorsTests
{
    private static readonly SvgDarkPalette Unmuted = new(new Dictionary<int, int>(), 0);

    /// <summary>Navy on the left, a currentColor square on the right.</summary>
    private const string NavyAndInk = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40">
          <rect x="0" y="0" width="40" height="40" fill="#1F3A93" />
          <rect x="60" y="0" width="40" height="40" fill="currentColor" />
        </svg>
        """;

    private static SKBitmap Render(string source, SKColor tint, SvgDarkPalette? palette, float lineWidthScale = 1)
    {
        using var svg = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return SKBitmap.Decode(SvgRasterizer.RenderPng(svg, 100, 40, tint, palette, lineWidthScale));
    }

    private static double Hue(Color c) => c.GetHue();

    [Fact]
    public void Black_becomes_white()
    {
        var dark = SvgDarkColors.ForDark(Color.Black, Unmuted);

        Assert.True(dark.R > 250 && dark.G > 250 && dark.B > 250, dark.ToString());
    }

    [Fact]
    public void A_dark_colour_is_lightened_and_keeps_its_hue()
    {
        var navy = Color.FromArgb(0x1F, 0x3A, 0x93);
        var dark = SvgDarkColors.ForDark(navy, Unmuted);

        Assert.True(dark.GetBrightness() > navy.GetBrightness() + 0.2, dark.ToString());
        // Lab keeps the perceived hue; HSL's hue moves a little along the way.
        Assert.InRange(Hue(dark), Hue(navy) - 15, Hue(navy) + 15);
    }

    [Fact]
    public void Muting_takes_chroma_from_a_light_colour_without_darkening_it()
    {
        var red = Color.FromArgb(0xFF, 0x45, 0x3A);
        var dark = SvgDarkColors.ForDark(red, SvgDarkPalette.Default);

        Assert.True(dark.GetSaturation() < red.GetSaturation(), dark.ToString());
        Assert.InRange(Hue(dark), Hue(red) - 5, Hue(red) + 5);
    }

    [Fact]
    public void Without_muting_a_light_colour_stays()
    {
        var yellow = Color.FromArgb(0xFC, 0xFF, 0x00);

        Assert.Equal(yellow.ToArgb(), SvgDarkColors.ForDark(yellow, Unmuted).ToArgb());
    }

    [Fact]
    public void A_mapped_colour_takes_its_pair_and_keeps_its_alpha()
    {
        var palette = new SvgDarkPalette(new Dictionary<int, int> { [0xE53935] = 0xC8625E }, 0.15);

        var dark = SvgDarkColors.ForDark(Color.FromArgb(0x80, 0xE5, 0x39, 0x35), palette);

        Assert.Equal(Color.FromArgb(0x80, 0xC8, 0x62, 0x5E).ToArgb(), dark.ToArgb());
    }

    [Fact]
    public void The_own_colours_change_and_the_tint_does_not()
    {
        using var bitmap = Render(NavyAndInk, SKColors.Red, Unmuted);

        var navy = bitmap.GetPixel(20, 20);
        Assert.True(navy.Red + navy.Green + navy.Blue > 0x1F + 0x3A + 0x93 + 150, navy.ToString());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(80, 20));
    }

    [Fact]
    public void Without_a_palette_the_own_colours_stay()
    {
        using var bitmap = Render(NavyAndInk, SKColors.Red, null);

        Assert.Equal(new SKColor(0x1F, 0x3A, 0x93), bitmap.GetPixel(20, 20));
    }

    [Fact]
    public void Without_a_tint_the_default_black_turns_white()
    {
        const string Unfilled = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><rect width="100" height="40" /></svg>""";

        using var bitmap = Render(Unfilled, SKColors.Transparent, Unmuted);

        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 20));
    }

    /// <summary>A horizontal line along y = 20, one set 4 wide and one at the default of 1.</summary>
    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><path d="M0 20H100" stroke="#000" stroke-width="4" /></svg>""", 4)]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><path d="M0 20H100" stroke="#000" /></svg>""", 1)]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><g style="stroke-width:4"><path d="M0 20H100" stroke="#000" /></g></svg>""", 4)]
    public void Line_width_scale_multiplies_every_stroke(string source, int width)
    {
        using var normal = Render(source, SKColors.Transparent, null);
        using var doubled = Render(source, SKColors.Transparent, null, 2);

        Assert.Equal(width, Thickness(normal));
        Assert.Equal(width * 2, Thickness(doubled));
    }

    // Summed coverage down one column, so a line that anti-aliases across two rows still counts once.
    private static int Thickness(SKBitmap bitmap) =>
        (int)Math.Round(Enumerable.Range(0, bitmap.Height).Sum(y => bitmap.GetPixel(50, y).Alpha) / 255.0);
}
