namespace Orientera.Backend.Arena;

/// <summary>
/// Ett fält av flyttal på ett radordnat rutnät — höjdmodellen, skuggningen, djupbufferten.
/// Motsvarigheten till prototypens tvådimensionella numpy-arrayer.
/// </summary>
public sealed class ScalarGrid(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float[] Values { get; } = new float[(long)width * height <= int.MaxValue
        ? width * height
        : throw new ArgumentOutOfRangeException(nameof(width), "gridet ryms inte i en array")];

    public float this[int x, int y]
    {
        get => Values[y * Width + x];
        set => Values[y * Width + x] = value;
    }

    /// <summary>
    /// Bilinjär avläsning i pixelkoordinater, med samma kantklämning som prototypen:
    /// koordinaten kläms strax innanför sista pixeln så att grannpixeln alltid finns.
    /// </summary>
    public float Sample(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1.001);
        y = Math.Clamp(y, 0, Height - 1.001);
        var x0 = (int)x;
        var y0 = (int)y;
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);
        var row = y0 * Width + x0;
        var a00 = Values[row];
        var a10 = Values[row + 1];
        var a01 = Values[row + Width];
        var a11 = Values[row + Width + 1];
        return a00 * (1 - fx) * (1 - fy) + a10 * fx * (1 - fy)
             + a01 * (1 - fx) * fy + a11 * fx * fy;
    }

    public ScalarGrid Fill(float value)
    {
        Array.Fill(Values, value);
        return this;
    }

    /// <summary>Medianen av de ändliga värdena — numpys <c>nanmedian</c>.</summary>
    public float MedianOfFinite()
    {
        var finite = Array.FindAll(Values, float.IsFinite);
        if (finite.Length == 0)
            return float.NaN;
        Array.Sort(finite);
        var mid = finite.Length / 2;
        return finite.Length % 2 == 1 ? finite[mid] : (finite[mid - 1] + finite[mid]) / 2f;
    }
}
