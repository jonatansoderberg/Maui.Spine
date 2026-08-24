namespace Orientera.Backend.Arena;

/// <summary>
/// En RGB-bild som flyttal 0–1 på ett radordnat rutnät, tre kanaler i följd per pixel.
/// Det är formen ortofotot och texturerna räknas i innan de blir en PNG.
/// </summary>
public sealed class ColorGrid(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float[] Values { get; } = new float[checked(width * height * 3)];

    public int IndexOf(int x, int y) => (y * Width + x) * 3;

    /// <summary>Bilinjär avläsning av alla tre kanalerna, med samma klämning som <see cref="ScalarGrid.Sample"/>.</summary>
    public (float R, float G, float B) Sample(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001);
        y = Math.Clamp(y, 0, Height - 1.001);
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);
        var w00 = (1 - fx) * (1 - fy);
        var w10 = fx * (1 - fy);
        var w01 = (1 - fx) * fy;
        var w11 = fx * fy;
        var i00 = IndexOf(x0, y0);
        var i10 = i00 + 3;
        var i01 = i00 + Width * 3;
        var i11 = i01 + 3;
        return (
            Values[i00] * w00 + Values[i10] * w10 + Values[i01] * w01 + Values[i11] * w11,
            Values[i00 + 1] * w00 + Values[i10 + 1] * w10 + Values[i01 + 1] * w01 + Values[i11 + 1] * w11,
            Values[i00 + 2] * w00 + Values[i10 + 2] * w10 + Values[i01 + 2] * w01 + Values[i11 + 2] * w11);
    }
}
