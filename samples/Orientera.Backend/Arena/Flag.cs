using SkiaSharp;

namespace Orientera.Backend.Arena;

/// <summary>
/// Beachflaggan på arenan, läst ur <c>Arena/Assets/vimpel.png</c> och skalad på plats.
/// </summary>
/// <remarks>
/// Vimpeln är en bildfil, inte kod — den som vill byta utseende byter fil, inte
/// renderingskod. Filens konvention: transparent bakgrund, mastfoten i bildens vågräta
/// mitt med nederkanten vid foten, och bilden skalas så att dess hela höjd blir den
/// begärda vimpelhöjden. Flaggan ritas alltid efter bildmodellens pass, aldrig före:
/// bokstäver är det diffusion är sämst på.
/// </remarks>
public static class Flag
{
    private static readonly Lazy<RgbaLayer> Asset = new(LoadAsset);

    /// <summary>Vimpeln på marken vid en bildpunkt, med markskugga så den står i bilden i stället för på den.</summary>
    public static void Draw(ColorGrid image, (double X, double Y) at, double height, double sunDx = -0.9)
    {
        var x = (int)at.X;
        var y = (int)at.Y;

        var shadow = new RgbaLayer(image.Width, image.Height);
        var reach = height * 0.42;
        (double, double)[] shadowShape =
        [
            (x - 3.0, y), (x + 3.0, y),
            (x + sunDx * reach + 5, y + reach * 0.30), (x + sunDx * reach - 3, y + reach * 0.30),
        ];
        Rasterizer.FillPolygon(shadow.Color, shadowShape, new Rgba(18, 22, 28));
        Rasterizer.FillPolygon(shadow.Alpha, shadowShape, 120 / 255f);
        shadow.Blur(Math.Max(1, height * 0.02));
        shadow.CompositeOver(image, 0, 0);

        var asset = Asset.Value;
        var scale = height / asset.Height;
        var tile = asset.Resize(
            Math.Max(1, (int)Math.Round(asset.Width * scale)),
            Math.Max(1, (int)Math.Round(height)));
        tile.CompositeOver(image, x - tile.Width / 2, y - tile.Height + 1);
    }

    private static RgbaLayer LoadAsset()
    {
        using var stream = typeof(Flag).Assembly.GetManifestResourceStream(
            "Orientera.Backend.Arena.Assets.vimpel.png")
            ?? throw new InvalidOperationException("vimpel.png saknas som inbäddad resurs");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var bitmap = ColorGridImage.DecodeBitmap(memory.ToArray());

        var layer = new RgbaLayer(bitmap.Width, bitmap.Height);
        var pixels = bitmap.GetPixelSpan();
        for (int p = 0, i = 0; p < layer.Width * layer.Height; p++, i += 3)
        {
            layer.Color.Values[i] = pixels[p * 4] / 255f;
            layer.Color.Values[i + 1] = pixels[p * 4 + 1] / 255f;
            layer.Color.Values[i + 2] = pixels[p * 4 + 2] / 255f;
            layer.Alpha.Values[p] = pixels[p * 4 + 3] / 255f;
        }
        return layer;
    }
}
