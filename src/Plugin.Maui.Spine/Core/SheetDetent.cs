namespace Plugin.Maui.Spine.Core;


/// <summary>
/// Describes a snap point (detent) for a bottom sheet.
/// Use the string constants (<see cref="Compact"/>, <see cref="Medium"/>, <see cref="Expanded"/>,
/// <see cref="FullScreen"/>) when specifying detents in a
/// <see cref="NavigableSheetAttribute"/>, or the static factory methods
/// (<see cref="FromPercentage"/>, <see cref="FromHeight"/>) at runtime.
/// </summary>
public sealed record SheetDetent
{
    /// <summary>Proportional height as a fraction of the container (0.0–1.0). <see langword="null"/> for absolute detents.</summary>
    public double? Percentage { get; }

    /// <summary>Absolute height in device-independent units. <see langword="null"/> for percentage detents.</summary>
    public double? AbsoluteHeight { get; }

    private SheetDetent(double? percentage = null, double? absoluteHeight = null)
    {
        Percentage = percentage;
        AbsoluteHeight = absoluteHeight;
    }

    // ── Named Keys (const — valid as attribute arguments) ──────────────────────

    /// <summary>Use in <c>[NavigableSheet(AllowedDetents = …)]</c> or <c>InitialDetent</c>.
    /// For absolute heights use the <c>"&lt;value&gt;px"</c> format, e.g. <c>"50px"</c>.</summary>
    public const string Compact    = "Compact";
    /// <summary>Medium snap height — approximately 50% of the container. Usable as an attribute string argument.</summary>
    public const string Medium     = "Medium";
    /// <summary>Expanded snap height — approximately 85% of the container. Usable as an attribute string argument.</summary>
    public const string Expanded   = "Expanded";
    /// <summary>Full-screen snap height — 100% of the container. Usable as an attribute string argument.</summary>
    public const string FullScreen = "FullScreen";

    // ── Static Presets ─────────────────────────────────────────────────────────

    /// <summary>Preset for <see cref="Compact"/> — 25% of the container height.</summary>
    public static readonly SheetDetent CompactDetent    = new(percentage: 0.25);
    /// <summary>Preset for <see cref="Medium"/> — 50% of the container height.</summary>
    public static readonly SheetDetent MediumDetent     = new(percentage: 0.50);
    /// <summary>Preset for <see cref="Expanded"/> — 85% of the container height.</summary>
    public static readonly SheetDetent ExpandedDetent   = new(percentage: 0.85);
    /// <summary>Preset for <see cref="FullScreen"/> — 100% of the container height.</summary>
    public static readonly SheetDetent FullScreenDetent = new(percentage: 1.00);

    // ── Factory Methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Create a detent relative to the container height (0.0–1.0).
    /// </summary>
    public static SheetDetent FromPercentage(double percentage)
    {
        if (percentage <= 0 || percentage > 1)
            throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be > 0 and <= 1.");

        return new(percentage: percentage);
    }

    /// <summary>
    /// Create a detent with absolute height in device units (pixels/DP depending on platform).
    /// </summary>
    public static SheetDetent FromHeight(double height)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        return new(absoluteHeight: height);
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a detent string from a <see cref="NavigableSheetAttribute"/>.
    /// <list type="bullet">
    ///   <item>Named presets: <c>"Compact"</c>, <c>"Medium"</c>, <c>"Expanded"</c>, <c>"FullScreen"</c></item>
    ///   <item>Percentage: <c>"50%"</c> (value 1–100)</item>
    ///   <item>Absolute height: <c>"200px"</c></item>
    /// </list>
    /// </summary>
    public static bool TryParse(string? spec, out SheetDetent? detent)
    {
        detent = null;

        if (string.IsNullOrWhiteSpace(spec))
            return false;

        var s = spec.Trim();

        detent = s switch
        {
            Compact    => CompactDetent,
            Medium     => MediumDetent,
            Expanded   => ExpandedDetent,
            FullScreen => FullScreenDetent,
            _ => null
        };

        if (detent is not null)
            return true;

        if (s.EndsWith('%') &&
            double.TryParse(s[..^1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct) &&
            pct > 0 && pct <= 100)
        {
            detent = FromPercentage(pct / 100.0);
            return true;
        }

        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(s[..^2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var px) &&
            px > 0)
        {
            detent = FromHeight(px);
            return true;
        }

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary><see langword="true"/> when the detent is defined as a proportional height.</summary>
    public bool IsPercentage => Percentage.HasValue;
    /// <summary><see langword="true"/> when the detent is defined as an absolute height.</summary>
    public bool IsAbsolute => AbsoluteHeight.HasValue;

    /// <summary>Returns a human-readable representation of this detent (e.g. <c>"Percent(50%)"</c> or <c>"Height(300px)"</c>).</summary>
    public override string ToString()
    {
        if (IsPercentage) return $"Percent({Percentage:P0})";
        if (IsAbsolute) return $"Height({AbsoluteHeight}px)";
        return "Invalid";
    }
}