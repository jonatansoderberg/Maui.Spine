using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The presets' colours. Each preset is a 3 × 3 design per theme, row by row; a mesh with another
/// number of points samples the design, so a 5 × 4 mesh looks like the 3 × 3 one with more freedom
/// to move.
/// </summary>
internal static class MeshPalettes
{
    /// <summary>Apple's system blue, light and dark: the accent when the app declares none.</summary>
    public static readonly SKColor DefaultAccentLight = new(0x00, 0x7A, 0xFF);
    public static readonly SKColor DefaultAccentDark = new(0x0A, 0x84, 0xFF);

    private static readonly SKColor[] AuroraLight =
    [
        new(0xE4, 0xDD, 0xFF), new(0xB4, 0xF0, 0xDC), new(0xD9, 0xF1, 0xFF),
        new(0x93, 0xE3, 0xCB), new(0xF5, 0xF3, 0xFF), new(0xC8, 0xB4, 0xFF),
        new(0xE2, 0xF9, 0xF2), new(0xA4, 0xD8, 0xF2), new(0xF2, 0xE2, 0xFF),
    ];

    private static readonly SKColor[] AuroraDark =
    [
        new(0x1A, 0x13, 0x45), new(0x5B, 0x3F, 0xE0), new(0x08, 0x0E, 0x2C),
        new(0x14, 0xC0, 0x8E), new(0x0E, 0x83, 0x9A), new(0x9A, 0x3F, 0xC4),
        new(0x06, 0x0D, 0x26), new(0x0C, 0x5E, 0x58), new(0x0A, 0x0D, 0x28),
    ];

    private static readonly SKColor[] SunsetLight =
    [
        new(0xFF, 0xE3, 0xD0), new(0xFF, 0xC9, 0xAE), new(0xFD, 0xE4, 0xF0),
        new(0xFF, 0xAA, 0x92), new(0xFF, 0xF2, 0xE4), new(0xF6, 0xB0, 0xCC),
        new(0xFF, 0xE6, 0x9E), new(0xFF, 0xC0, 0x96), new(0xF1, 0xD0, 0xFB),
    ];

    private static readonly SKColor[] SunsetDark =
    [
        new(0x1E, 0x0B, 0x33), new(0x3C, 0x12, 0x52), new(0x16, 0x0A, 0x2B),
        new(0x7A, 0x1F, 0x5C), new(0xB0, 0x3C, 0x5E), new(0x58, 0x1A, 0x52),
        new(0xF0, 0x8E, 0x3A), new(0xDC, 0x5C, 0x48), new(0xC0, 0x43, 0x5A),
    ];

    /// <summary>The 3 × 3 design of <paramref name="preset"/>, row by row.</summary>
    public static SKColor[] Design(MeshPreset preset, bool dark, SKColor accent) => preset switch
    {
        MeshPreset.Aurora => dark ? AuroraDark : AuroraLight,
        MeshPreset.Sunset => dark ? SunsetDark : SunsetLight,
        _ => FromAccent(accent, dark),
    };

    /// <summary>
    /// A design around one colour: in light mode a pale wash of its hue with two lighter glows of it
    /// and its neighbours; in dark mode a deep ground of its hue with the accent itself glowing in it.
    /// </summary>
    public static SKColor[] FromAccent(SKColor accent, bool dark)
    {
        var source = Oklab.FromColor(accent);
        var hue = source.Hue;

        // A grey accent has no hue to build on; keep its own lightness and a neutral ground.
        var chroma = Math.Min(source.Chroma, 0.2f);

        SKColor C(float lightness, float chromaShare, float hueShift) =>
            Oklab.FromLch(lightness, chroma * chromaShare, hue + hueShift).ToColor();

        if (dark)
        {
            var glow = Math.Clamp(source.L, 0.5f, 0.68f);
            return
            [
                C(0.17f, 0.15f, 30), C(glow - 0.12f, 0.8f, -35), C(0.21f, 0.2f, 0),
                C(glow, 1f, 0), C(0.24f, 0.25f, 15), C(glow - 0.06f, 0.85f, 40),
                C(0.19f, 0.2f, -20), C(0.16f, 0.15f, 0), C(glow - 0.16f, 0.7f, 20),
            ];
        }

        return
        [
            C(0.91f, 0.35f, 35), C(0.97f, 0.1f, 0), C(0.84f, 0.55f, 0),
            C(0.97f, 0.08f, 15), C(0.80f, 0.65f, -15), C(0.95f, 0.2f, 40),
            C(0.88f, 0.4f, -40), C(0.96f, 0.12f, 0), C(0.90f, 0.35f, 20),
        ];
    }

    /// <summary>
    /// One colour per point of a <paramref name="columns"/> × <paramref name="rows"/> mesh, sampled
    /// bilinearly (in Oklab) from a 3 × 3 <paramref name="design"/>.
    /// </summary>
    public static SKColor[] Sample(SKColor[] design, int columns, int rows)
    {
        var lab = Array.ConvertAll(design, Oklab.FromColor);
        var result = new SKColor[columns * rows];

        for (var j = 0; j < rows; j++)
        {
            var v = j * 2f / (rows - 1);
            var row = Math.Min((int)v, 1);
            var fv = v - row;

            for (var i = 0; i < columns; i++)
            {
                var u = i * 2f / (columns - 1);
                var column = Math.Min((int)u, 1);
                var fu = u - column;

                var top = lab[row * 3 + column] * (1 - fu) + lab[row * 3 + column + 1] * fu;
                var bottom = lab[(row + 1) * 3 + column] * (1 - fu) + lab[(row + 1) * 3 + column + 1] * fu;
                result[j * columns + i] = (top * (1 - fv) + bottom * fv).ToColor();
            }
        }

        return result;
    }

    /// <summary>One colour per point, row by row, repeating <paramref name="colors"/> when it is shorter.</summary>
    public static SKColor[] Repeat(IReadOnlyList<SKColor> colors, int columns, int rows)
    {
        var result = new SKColor[columns * rows];
        for (var k = 0; k < result.Length; k++)
            result[k] = colors[k % colors.Count];

        return result;
    }
}
