namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Where a spotlight's hole and tip go, in window coordinates. No MAUI types beyond plain numbers, so the
/// tests can run it.
/// </summary>
internal static class SpotlightLayout
{
    /// <summary>The room kept between the tip and the window's edges, and between the tip and the hole.</summary>
    public const double Margin = 16;

    /// <summary>The hole round a target's frame: the frame grown by <paramref name="padding"/>, kept in the window.</summary>
    public static (double X, double Y, double Width, double Height) Hole(
        (double X, double Y, double Width, double Height) target, double padding, double windowWidth, double windowHeight)
    {
        var x = Math.Max(0, target.X - padding);
        var y = Math.Max(0, target.Y - padding);
        var right = Math.Min(windowWidth, target.X + target.Width + padding);
        var bottom = Math.Min(windowHeight, target.Y + target.Height + padding);
        return (x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>
    /// The hole's corner radius: the target's own, grown by the padding round it, so the hole has the target's
    /// shape. At most half the shorter side, which makes a capsule or a circle.
    /// </summary>
    public static double HoleRadius(double width, double height, double targetRadius, double padding) =>
        Math.Clamp(targetRadius + padding, 0, Math.Min(width, height) / 2);

    /// <summary>
    /// The tip's top left corner: below the hole when it fits there, otherwise above it, otherwise at the
    /// bottom of the window; centred on the hole and kept a margin inside the window's edges. The tip stays
    /// below <c>safeTop</c> (the status bar or notch) and above <c>safeBottom</c> (the home indicator).
    /// </summary>
    public static (double X, double Y, bool Below) Tip(
        (double X, double Y, double Width, double Height) hole, double tipWidth, double tipHeight,
        double windowWidth, double windowHeight, double safeTop = 0, double safeBottom = 0)
    {
        var x = Math.Clamp(hole.X + hole.Width / 2 - tipWidth / 2, Margin, Math.Max(Margin, windowWidth - Margin - tipWidth));

        var below = hole.Y + hole.Height + Margin;
        if (below + tipHeight <= windowHeight - safeBottom - Margin)
            return (x, below, true);

        var above = hole.Y - Margin - tipHeight;
        if (above >= safeTop + Margin)
            return (x, above, false);

        return (x, Math.Max(safeTop + Margin, windowHeight - safeBottom - Margin - tipHeight), true);
    }

    /// <summary>A hole part of the way, 0 to 1, from one place to the next, eased in and out.</summary>
    public static (double X, double Y, double Width, double Height) Between(
        (double X, double Y, double Width, double Height) from, (double X, double Y, double Width, double Height) to, double progress)
    {
        var eased = Ease(progress);
        return (Lerp(from.X, to.X), Lerp(from.Y, to.Y), Lerp(from.Width, to.Width), Lerp(from.Height, to.Height));

        double Lerp(double a, double b) => a + (b - a) * eased;
    }

    /// <summary>Progress 0 to 1, eased in and out (cubic).</summary>
    public static double Ease(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
}
