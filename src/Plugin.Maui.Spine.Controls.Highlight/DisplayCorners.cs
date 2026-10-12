namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The corner radius of an iPhone or iPad screen, in points. iOS has no public API for it, so it is a table by
/// screen size (owner's answer Q18), kept up to date by hand. No MAUI types, so the tests can run it.
/// </summary>
internal static class DisplayCorners
{
    // Portrait width × height in points → radius. Sizes shared by models with different radii take the newer.
    static readonly Dictionary<(int Width, int Height), double> IPhones = new()
    {
        [(320, 568)] = 0,      // SE (1st)
        [(375, 667)] = 0,      // 6–8, SE (2nd, 3rd)
        [(414, 736)] = 0,      // 6–8 Plus
        [(375, 812)] = 44,     // 12 mini, 13 mini (X, XS, 11 Pro: 39)
        [(414, 896)] = 41.5,   // XR, 11 (XS Max, 11 Pro Max: 39)
        [(390, 844)] = 47.33,  // 12, 12 Pro, 13, 13 Pro, 14
        [(428, 926)] = 53.33,  // 12 Pro Max, 13 Pro Max, 14 Plus
        [(393, 852)] = 55,     // 14 Pro, 15, 15 Pro, 16
        [(430, 932)] = 55,     // 14 Pro Max, 15 Plus, 15 Pro Max, 16 Plus
        [(402, 874)] = 62,     // 16 Pro, 17, 17 Pro
        [(440, 956)] = 62,     // 16 Pro Max, 17 Pro Max
        [(420, 912)] = 62,     // Air
    };

    /// <summary>
    /// The radius for a screen of <paramref name="width"/> × <paramref name="height"/> points, either way up.
    /// An unknown size with a home indicator (no home button) gets 55, the commonest; without one, 0.
    /// </summary>
    public static double Radius(double width, double height, bool hasHomeIndicator, bool isPad)
    {
        if (isPad)
            return hasHomeIndicator ? 18 : 0;

        var key = ((int)Math.Round(Math.Min(width, height)), (int)Math.Round(Math.Max(width, height)));
        return IPhones.TryGetValue(key, out var radius) ? radius
            : hasHomeIndicator ? 55
            : 0;
    }
}

/// <summary>
/// The window edge's level, eased towards what the caller's function reads on each frame, so a level read
/// from audio at 20 to 60 Hz does not flicker.
/// </summary>
internal sealed class EdgeLevel
{
    /// <summary>How far the level goes towards the target on each frame.</summary>
    public const double Rate = 0.25;

    public double Value { get; private set; }

    /// <summary>Moves a step towards <paramref name="target"/>, clamped to 0–1; a value that is not a number counts as 0.</summary>
    public double Step(double target)
    {
        var clamped = double.IsFinite(target) ? Math.Clamp(target, 0, 1) : 0;
        Value += (clamped - Value) * Rate;
        return Value;
    }
}
