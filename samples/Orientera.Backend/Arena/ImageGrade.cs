namespace Orientera.Backend.Arena;

/// <summary>
/// Slutgradering: lokalkontrast, filmisk S-kurva, delad toning och mättnad.
/// </summary>
/// <remarks>
/// Lokalkontrasten är det som läser som HDR — en oskarp mask på luminansen lyfter struktur i
/// mellantonerna utan att röra den globala exponeringen. Delad toning lägger värme i
/// högdagrarna och kyla i skuggorna, vilket är det som skiljer en solbelyst bild från en
/// gråmulen. Graderingen sker på plats i den bild som skickas in.
/// </remarks>
public static class ImageGrade
{
    public static ColorGrid Apply(ColorGrid image, GradeSettings settings)
    {
        var width = image.Width;
        var height = image.Height;
        var values = image.Values;

        for (var i = 0; i < values.Length; i++)
            values[i] = float.IsNaN(values[i]) ? 0f : Math.Clamp(values[i], 0f, 1f);

        var luminance = new ScalarGrid(width, height);
        for (int p = 0, i = 0; p < luminance.Values.Length; p++, i += 3)
            luminance.Values[p] = TerrainTexture.Luminance(values[i], values[i + 1], values[i + 2]);
        var blurred = GridMath.Smooth(luminance, Math.Max(2.0, height / 26.0));

        var coolR = 1 - settings.Warmth * 0.7f;
        var coolB = 1 + settings.Warmth * 1.2f;
        var warmR = 1 + settings.Warmth;
        var warmB = 1 - settings.Warmth * 1.5f;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                var i = p * 3;

                // Additivt, inte som kvot: i helsvarta pixlar är luminansen noll och kvoten spricker.
                var lift = settings.Local * (luminance.Values[p] - blurred.Values[p]);
                var r = Math.Clamp(values[i] + lift, 0f, 1f);
                var g = Math.Clamp(values[i + 1] + lift, 0f, 1f);
                var b = Math.Clamp(values[i + 2] + lift, 0f, 1f);

                r = SCurve(r, settings.Contrast);
                g = SCurve(g, settings.Contrast);
                b = SCurve(b, settings.Contrast);

                var tone = Math.Clamp(TerrainTexture.Luminance(r, g, b), 0f, 1f);
                r *= coolR + (warmR - coolR) * tone;
                b *= coolB + (warmB - coolB) * tone;

                var grey = TerrainTexture.Luminance(r, g, b);
                r = Math.Clamp(grey + (r - grey) * settings.Saturation, 0f, 1f);
                g = Math.Clamp(grey + (g - grey) * settings.Saturation, 0f, 1f);
                b = Math.Clamp(grey + (b - grey) * settings.Saturation, 0f, 1f);

                // Vegetationslyft: varmt ljus multiplicerat på grönt drar ur mättnaden, så den
                // läggs tillbaka selektivt där grönt dominerar — ett HSL-grepp, inte en global
                // mättnadshöjning som skulle göra sanden neonorange.
                if (settings.Vegetation != 0)
                {
                    var mask = Math.Clamp((g - Math.Max(r, b)) / 0.06f, 0f, 1f) * settings.Vegetation;
                    var lum = TerrainTexture.Luminance(r, g, b);
                    r = Math.Clamp(r * (1 - mask) + (lum + (r - lum) * 1.9f) * mask, 0f, 1f);
                    g = Math.Clamp(g * (1 - mask) + (lum + (g - lum) * 1.9f) * mask, 0f, 1f);
                    b = Math.Clamp(b * (1 - mask) + (lum + (b - lum) * 1.9f) * mask, 0f, 1f);
                }

                values[i] = r;
                values[i + 1] = g;
                values[i + 2] = b;
            }
        });
        return image;
    }

    private static float SCurve(float x, float contrast) =>
        x * (1 - contrast) + x * x * (3 - 2 * x) * contrast;
}
