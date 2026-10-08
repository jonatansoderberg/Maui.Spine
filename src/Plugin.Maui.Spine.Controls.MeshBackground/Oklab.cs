using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A colour in Oklab (Björn Ottosson, 2020), where a straight line between two colours looks like an
/// even blend: blue to yellow passes through no grey, and lightness stays where the eye expects it.
/// </summary>
internal readonly record struct Oklab(float L, float A, float B, float Alpha)
{
    public float Chroma => MathF.Sqrt(A * A + B * B);

    /// <summary>The hue angle in degrees.</summary>
    public float Hue => MathF.Atan2(B, A) * 180f / MathF.PI;

    /// <summary>A colour from lightness (0–1), chroma (0–about 0.37) and hue in degrees.</summary>
    public static Oklab FromLch(float lightness, float chroma, float hue, float alpha = 1f)
    {
        var radians = hue * MathF.PI / 180f;
        return new(lightness, chroma * MathF.Cos(radians), chroma * MathF.Sin(radians), alpha);
    }

    public static Oklab FromColor(SKColor color)
    {
        var r = ToLinear(color.Red / 255f);
        var g = ToLinear(color.Green / 255f);
        var b = ToLinear(color.Blue / 255f);

        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

        return new(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s,
            color.Alpha / 255f);
    }

    /// <summary>The nearest sRGB colour; channels outside the gamut are clipped.</summary>
    public SKColor ToColor()
    {
        var l = L + 0.3963377774f * A + 0.2158037573f * B;
        var m = L - 0.1055613458f * A - 0.0638541728f * B;
        var s = L - 0.0894841775f * A - 1.2914855480f * B;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;

        return new SKColor(
            ToByte(FromLinear(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s)),
            ToByte(FromLinear(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s)),
            ToByte(FromLinear(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s)),
            ToByte(Alpha));
    }

    public static Oklab operator +(Oklab x, Oklab y) => new(x.L + y.L, x.A + y.A, x.B + y.B, x.Alpha + y.Alpha);

    public static Oklab operator *(Oklab x, float w) => new(x.L * w, x.A * w, x.B * w, x.Alpha * w);

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float FromLinear(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    private static byte ToByte(float c) => (byte)MathF.Round(Math.Clamp(c, 0f, 1f) * 255f);
}
