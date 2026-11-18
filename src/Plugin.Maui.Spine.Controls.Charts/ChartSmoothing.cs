namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The light smoothing <see cref="LineChart"/> draws with, free of MAUI so other pictures of the same series (a widget's,
/// a Live Activity's) can draw the same line. Only the drawn line is smoothed: values shown as numbers, and the last
/// point, stay as measured.
/// </summary>
public static class ChartSmoothing
{
    /// <summary>
    /// Values eased by a Gaussian-weighted local linear fit, <paramref name="sigma"/> wide. A fit rather than a mean
    /// keeps a rising or falling stretch from bending towards the flat at the ends, so the full width applies all the
    /// way to them and the start and the end look like the middle. The last point stays as measured.
    /// </summary>
    /// <param name="points">One unbroken run, oldest first: X in any unit, Y the value.</param>
    /// <param name="sigma">The width of the weighting, in the unit of X.</param>
    /// <param name="keep">Points drawn as measured, such as those outside a target band: a deviation to see, not to smooth.</param>
    public static (float X, float Y)[] Gaussian(IReadOnlyList<(float X, float Y)> points, float sigma, Func<int, bool>? keep = null)
    {
        var n = points.Count;
        if (n < 3 || sigma <= 0)
            return [.. points];

        var eased = new (float X, float Y)[n];
        for (var i = 0; i < n; i++)
        {
            var x = points[i].X;
            double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0;
            for (var j = 0; j < n; j++)
            {
                var dx = (double)(points[j].X - x);
                if (Math.Abs(dx) > 3 * sigma)
                    continue;
                var w = Math.Exp(-0.5 * dx * dx / (sigma * sigma));
                var y = (double)points[j].Y;
                sw += w; swx += w * dx; swy += w * y; swxx += w * dx * dx; swxy += w * dx * y;
            }
            var det = sw * swxx - swx * swx;
            var value = det > 1e-9 ? (swy - (sw * swxy - swx * swy) / det * swx) / sw : swy / sw;
            eased[i] = keep?.Invoke(i) == true ? points[i] : (x, (float)value);
        }
        eased[^1] = points[^1];
        return eased;
    }

    /// <summary>
    /// The cubic Bézier segments of a Catmull-Rom curve through the points: rounded at peaks and troughs, where a
    /// monotone curve would flatten them. Each segment is the two control points and the end point.
    /// </summary>
    public static ((float X, float Y) C1, (float X, float Y) C2, (float X, float Y) End)[] CatmullRom(IReadOnlyList<(float X, float Y)> p)
    {
        var segments = new ((float, float), (float, float), (float, float))[Math.Max(0, p.Count - 1)];
        for (var i = 0; i < p.Count - 1; i++)
        {
            var before = p[Math.Max(0, i - 1)];
            var (from, to) = (p[i], p[i + 1]);
            var after = p[Math.Min(p.Count - 1, i + 2)];
            segments[i] = (
                (from.X + (to.X - before.X) / 6, from.Y + (to.Y - before.Y) / 6),
                (to.X - (after.X - from.X) / 6, to.Y - (after.Y - from.Y) / 6),
                to);
        }
        return segments;
    }
}
