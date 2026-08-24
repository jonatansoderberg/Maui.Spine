using System.Numerics;

namespace Orientera.Backend.Arena;

/// <summary>
/// Marktexturen: ortofoto som albedo, terrängskuggning som form, plus en egen ljussättning.
/// Ortofotot ensamt över svensk skog är en grön filt — skuggningen bär den riktiga markformen.
/// </summary>
public static class TerrainTexture
{
    private static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);

    public static float Luminance(float r, float g, float b) =>
        r * LuminanceWeights.X + g * LuminanceWeights.Y + b * LuminanceWeights.Z;

    /// <summary>Lambertsk lutningsskuggning ur höjdfältet, med numpys gradientkonvention.</summary>
    public static ScalarGrid Hillshade(ScalarGrid elevation, double resolution, double azimuth, double altitude)
    {
        var width = elevation.Width;
        var height = elevation.Height;
        var result = new ScalarGrid(width, height);
        var sinAz = (float)(Math.Cos(altitude) * Math.Sin(azimuth));
        var cosAz = (float)(Math.Cos(altitude) * Math.Cos(azimuth));
        var sinAlt = (float)Math.Sin(altitude);

        Parallel.For(0, height, y =>
        {
            var up = Math.Max(y - 1, 0) * width;
            var down = Math.Min(y + 1, height - 1) * width;
            var ySpacing = (float)((Math.Min(y + 1, height - 1) - Math.Max(y - 1, 0)) * resolution);
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(x - 1, 0);
                var right = Math.Min(x + 1, width - 1);
                var gx = (elevation.Values[row + right] - elevation.Values[row + left])
                    / (float)((right - left) * resolution);
                var gy = (elevation.Values[down + x] - elevation.Values[up + x]) / ySpacing;
                var nz = 1f / MathF.Sqrt(gx * gx + gy * gy + 1f);
                result.Values[row + x] = Math.Clamp(-gx * nz * sinAz - gy * nz * cosAz + nz * sinAlt, 0f, 1f);
            }
        });
        return result;
    }

    /// <summary>
    /// Snötäcker ortofotot utifrån vad det visar.
    /// </summary>
    /// <remarks>
    /// Barrskog skiljs från öppen mark på att den är både grön och mörk — en enda av
    /// egenskaperna räcker inte, åkrar är gröna och asfalt är mörk. Öppen mark får snö som
    /// behåller fotots ljushetsvariation, så vägar och diken syns igenom; skogen blir mörk
    /// och rimfrostad. Detta är en syntes, inte en mätning: det finns inget vinterortofoto
    /// bakom, och bilden ska märkas därefter.
    /// </remarks>
    public static ColorGrid Winterize(ColorGrid orthophoto)
    {
        var result = new ColorGrid(orthophoto.Width, orthophoto.Height);
        var snowTint = new Vector3(0.90f, 0.93f, 0.99f);
        var tree = new Vector3(0.17f, 0.23f, 0.21f) + 0.30f * new Vector3(0.55f, 0.60f, 0.66f);
        Parallel.For(0, orthophoto.Height, y =>
        {
            for (var x = 0; x < orthophoto.Width; x++)
            {
                var i = orthophoto.IndexOf(x, y);
                var r = orthophoto.Values[i];
                var g = orthophoto.Values[i + 1];
                var b = orthophoto.Values[i + 2];
                var lum = Luminance(r, g, b);
                var green = g - 0.5f * (r + b);
                // Trösklarna är kalibrerade mot ortofotots faktiska fördelning, inte gissade.
                var forest = Math.Clamp((green - 0.005f) / 0.045f, 0f, 1f)
                           * Math.Clamp((0.50f - lum) / 0.30f, 0f, 1f);
                var snow = snowTint * (0.80f + 0.30f * lum);
                var color = snow * (1 - forest) + tree * forest;
                result.Values[i] = Math.Clamp(color.X, 0f, 1f);
                result.Values[i + 1] = Math.Clamp(color.Y, 0f, 1f);
                result.Values[i + 2] = Math.Clamp(color.Z, 0f, 1f);
            }
        });
        return result;
    }

    /// <summary>Ljussatt marktextur: albedo gånger direkt och ambient ljus, med mikroform ur skuggningen.</summary>
    public static ColorGrid ShadeTexture(
        ColorGrid orthophoto, ScalarGrid shade, ScalarGrid elevation, double resolution,
        SeasonLook season, Lighting light)
    {
        var lambert = Hillshade(elevation, resolution,
            light.Azimuth * Math.PI / 180.0, light.Altitude * Math.PI / 180.0);
        var shadeMean = GridMath.Mean(shade.Values);

        var albedo = season.Snow ? Winterize(orthophoto) : orthophoto;
        var result = new ColorGrid(orthophoto.Width, orthophoto.Height);
        Parallel.For(0, orthophoto.Height, y =>
        {
            for (var x = 0; x < orthophoto.Width; x++)
            {
                var micro = Math.Clamp((shade[x, y] - shadeMean) * 1.5f + 0.5f, 0.05f, 1.0f);
                var direct = lambert[x, y] * (0.62f + 0.38f * micro);
                var ambient = season.Ambient * light.Ambient * (0.55f + 0.45f * micro);

                var i = result.IndexOf(x, y);
                var color = new Vector3(albedo.Values[i], albedo.Values[i + 1], albedo.Values[i + 2]);
                // Lyft mättnaden lite — ortofoton är sammansatta av många flygpass och blir platta.
                var grey = (color.X + color.Y + color.Z) / 3f;
                color = Vector3.Clamp(new Vector3(grey) + (color - new Vector3(grey)) * 1.35f,
                    Vector3.Zero, Vector3.One);

                var lit = light.Sun * (0.78f * direct) + light.Sky * ambient;
                var value = color * lit * season.Gain * light.Gain;
                result.Values[i] = MathF.Pow(Math.Clamp(value.X, 0f, 1f), 0.90f);
                result.Values[i + 1] = MathF.Pow(Math.Clamp(value.Y, 0f, 1f), 0.90f);
                result.Values[i + 2] = MathF.Pow(Math.Clamp(value.Z, 0f, 1f), 0.90f);
            }
        });
        return result;
    }
}
