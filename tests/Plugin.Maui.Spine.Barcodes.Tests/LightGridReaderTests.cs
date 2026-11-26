using SkiaSharp;
using Xunit;

namespace Plugin.Maui.Spine.Barcodes.Tests;

/// <summary>
/// A 12 × 12 word clock drawn with SkiaSharp: lit letters bright with a glow, unlit letters dim, on black glass.
/// The reader has to find the grid and read the Data Matrix the lit letters make.
/// </summary>
public class LightGridReaderTests
{
    private const string Code = "4711081542";

    private static readonly string[] Letters =
    [
        "ITISXTENHALF", "QUARTERTWENT", "FIVEMINUTESX", "PASTTOEONETW",
        "THREEFOURFIV", "SIXSEVENEIGH", "NINETENELEVE", "TWELVEOCLOCK",
        "ABCDEFGHIJKL", "MNOPQRSTUVWX", "YZABCDEFGHIJ", "KLMNOPQRSTUV",
    ];

    [Fact]
    public void Reads_the_code_straight_on() => AssertReads(Render(Code));

    [Fact]
    public void Reads_the_code_at_an_angle() => AssertReads(Render(Code, perspective: true));

    [Fact]
    public void Reads_the_code_when_unlit_letters_are_invisible() => AssertReads(Render(Code, unlit: 0));

    [Fact]
    public void Reads_the_code_when_unlit_letters_are_nearly_as_bright() => AssertReads(Render(Code, unlit: 150));

    [Fact]
    public void Reads_the_code_through_a_reflection() => AssertReads(Render(Code, perspective: true, reflection: true));

    [Fact]
    public void Reads_a_rotated_display() => AssertReads(Render(Code, rotate: 90));

    [Fact]
    public void Finds_the_grid_but_no_code_when_the_clock_shows_the_time()
    {
        using var image = Render(null);

        var result = Read(image);

        Assert.Null(result.Text);
        Assert.Equal(4, result.Corners.Count);
    }

    [Fact]
    public void An_empty_frame_is_a_miss_not_an_error()
    {
        var result = new LightGridReader().Read(new byte[640 * 480], 640, 480, 640);

        Assert.False(result.IsDecoded);
        Assert.Empty(result.Corners);
    }

    [Fact]
    public void A_padded_stride_reads_the_same()
    {
        using var image = Render(Code);
        var (pixels, w, h) = Luminance(image);
        int stride = w + 64;
        var padded = new byte[stride * h];
        for (int y = 0; y < h; y++) pixels.AsSpan(y * w, w).CopyTo(padded.AsSpan(y * stride));

        Assert.Equal(Code, new LightGridReader().Read(padded, w, h, stride).Text);
    }

    [Fact]
    public void One_reader_reads_frame_after_frame()
    {
        var reader = new LightGridReader();
        using var a = Render("1111111111");
        using var b = Render("2222222222", perspective: true);
        var (pa, wa, ha) = Luminance(a);
        var (pb, wb, hb) = Luminance(b);

        Assert.Equal("1111111111", reader.Read(pa, wa, ha, wa).Text);
        Assert.Equal("2222222222", reader.Read(pb, wb, hb, wb).Text);
        Assert.Equal("1111111111", reader.Read(pa, wa, ha, wa).Text);
    }

    [Theory]
    [InlineData(12, 14)]
    [InlineData(11, 11)]
    [InlineData(40, 40)]
    public void Grids_that_are_not_a_square_data_matrix_are_refused(int columns, int rows)
    {
        Assert.Throws<NotSupportedException>(() => new LightGridReader(new LightGridOptions(columns, rows)));
    }

    private static void AssertReads(SKBitmap image)
    {
        using (image)
        {
            var result = Read(image);
            Assert.True(result.Text == Code, $"expected {Code}, got {result.Text ?? "nothing"}\n{result.Diagnostics}");
        }
    }

    private static LightGridResult Read(SKBitmap image)
    {
        var (pixels, w, h) = Luminance(image);
        return new LightGridReader().Read(pixels, w, h, w);
    }

    private static (byte[] Pixels, int Width, int Height) Luminance(SKBitmap image)
    {
        var lum = new byte[image.Width * image.Height];
        var px = image.Pixels;
        for (int i = 0; i < lum.Length; i++)
            lum[i] = (byte)((px[i].Red * 77 + px[i].Green * 150 + px[i].Blue * 29) >> 8);
        return (lum, image.Width, image.Height);
    }

    /// <summary>The clock at 60 px per letter in a 1280 × 960 frame; <paramref name="payload"/> null lights a few words instead.</summary>
    private static SKBitmap Render(string? payload, bool perspective = false, byte unlit = 90, bool reflection = false, float rotate = 0)
    {
        var lit = new bool[12, 12];
        if (payload is not null)
        {
            var m = Barcode.Encode(payload, new DataMatrixOptions { Size = (12, 12) });
            for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++) lit[x, y] = m[x, y];
        }
        else
        {
            foreach (var (row, from, to) in new[] { (0, 0, 2), (0, 3, 4), (1, 0, 7), (3, 0, 4), (7, 0, 6) })
                for (int x = from; x < to; x++) lit[x, row] = true;
        }

        const int cell = 60, size = cell * 12 + 160;
        using var face = new SKBitmap(size, size);
        using (var c = new SKCanvas(face))
        {
            c.Clear(new SKColor(10, 10, 12));
            using var font = new SKFont(SKFontManager.Default.MatchFamily(SKTypeface.Default.FamilyName, SKFontStyle.Bold) ?? SKTypeface.Default, cell * 0.5f);
            using var dim = new SKPaint { Color = new SKColor(unlit, unlit, unlit), IsAntialias = true };
            using var glow = new SKPaint { Color = new SKColor(255, 250, 230, 200), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) };
            using var bright = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    string ch = Letters[y][x].ToString();
                    float px = 80 + x * cell + cell / 2f, py = 80 + y * cell + cell * 0.68f;
                    if (lit[x, y])
                    {
                        c.DrawText(ch, px, py, SKTextAlign.Center, font, glow);
                        c.DrawText(ch, px, py, SKTextAlign.Center, font, bright);
                    }
                    else if (unlit > 0) c.DrawText(ch, px, py, SKTextAlign.Center, font, dim);
                }
        }

        var frame = new SKBitmap(1280, 960);
        using (var c = new SKCanvas(frame))
        {
            c.Clear(new SKColor(120, 110, 95));
            var m = SKMatrix.CreateTranslation((1280 - size) / 2f, (960 - size) / 2f);
            m = SKMatrix.Concat(SKMatrix.CreateScale(0.95f, 0.95f, 640, 480), m);
            if (rotate != 0) m = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(rotate, 640, 480), m);
            if (perspective)
            {
                var p = SKMatrix.CreateIdentity();
                p.Persp0 = 0.0003f;
                m = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(-5, 640, 480), SKMatrix.Concat(p, m));
            }
            c.SetMatrix(m);
            c.DrawBitmap(face, 0, 0);
            c.ResetMatrix();
            if (reflection)
            {
                using var glare = new SKPaint { Color = new SKColor(255, 245, 225, 210), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) };
                c.DrawOval(new SKRect(700, 420, 900, 470), glare);
            }
        }
        return frame;
    }

}
