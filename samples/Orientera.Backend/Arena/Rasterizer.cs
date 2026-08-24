namespace Orientera.Backend.Arena;

/// <summary>En RGBA-färg i bytes, som prototypens PIL-tupler.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// Skanlinjerasterizer för överlagringarna: fyllda polygoner och tjocka linjer med
/// alfablandning, utan kantutjämning — samma semantik som PIL:s ImageDraw, som facitbilden
/// är ritad med.
/// </summary>
public static class Rasterizer
{
    /// <summary>Alfablandar färgen över bilden.</summary>
    public static void FillPolygon(ColorGrid image, IReadOnlyList<(double X, double Y)> points, Rgba color)
    {
        var alpha = color.A / 255f;
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        Scan(points, image.Width, image.Height, (x, y) =>
        {
            var i = image.IndexOf(x, y);
            image.Values[i] += (r - image.Values[i]) * alpha;
            image.Values[i + 1] += (g - image.Values[i + 1]) * alpha;
            image.Values[i + 2] += (b - image.Values[i + 2]) * alpha;
        });
    }

    /// <summary>Sätter täckningen rakt av — så PIL ritar i en masks eller ett lagers alfakanal.</summary>
    public static void FillPolygon(ScalarGrid mask, IReadOnlyList<(double X, double Y)> points, float value)
    {
        Scan(points, mask.Width, mask.Height, (x, y) => mask[x, y] = value);
    }

    /// <summary>Ett tjockt linjesegment som fylld rektangel med raka ändar, som PIL ritar dem.</summary>
    public static void DrawLine(
        ColorGrid image, (double X, double Y) from, (double X, double Y) to, double width, Rgba color)
    {
        if (Widen(from, to, width) is { } quad)
            FillPolygon(image, quad, color);
    }

    public static void DrawLine(
        ScalarGrid mask, (double X, double Y) from, (double X, double Y) to, double width, float value)
    {
        if (Widen(from, to, width) is { } quad)
            FillPolygon(mask, quad, value);
    }

    private static (double, double)[]? Widen((double X, double Y) from, (double X, double Y) to, double width)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9)
            return null;
        var nx = -dy / length * width / 2;
        var ny = dx / length * width / 2;
        return
        [
            (from.X + nx, from.Y + ny),
            (to.X + nx, to.Y + ny),
            (to.X - nx, to.Y - ny),
            (from.X - nx, from.Y - ny),
        ];
    }

    private static void Scan(
        IReadOnlyList<(double X, double Y)> points, int width, int height, Action<int, int> plot)
    {
        if (points.Count < 3)
            return;
        var minY = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(points.Max(p => p.Y)));

        var crossings = new List<double>();
        for (var y = minY; y <= maxY; y++)
        {
            var centerY = y + 0.5;
            crossings.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                var (x0, y0) = points[i];
                var (x1, y1) = points[(i + 1) % points.Count];
                if (y0 <= centerY == y1 <= centerY)
                    continue;
                crossings.Add(x0 + (centerY - y0) / (y1 - y0) * (x1 - x0));
            }
            crossings.Sort();
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var from = Math.Max(0, (int)Math.Ceiling(crossings[i] - 0.5));
                var to = Math.Min(width - 1, (int)Math.Floor(crossings[i + 1] - 0.5));
                for (var x = from; x <= to; x++)
                    plot(x, y);
            }
        }
    }
}
