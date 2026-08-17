namespace Orientera.Presentation;

/// <summary>
/// The two marks a row can end in: onwards inside the app, or out of it.
/// </summary>
/// <remarks>
/// Drawn rather than shipped as glyphs for the same reason as <see cref="DisciplineGlyph"/> — a
/// rasterised asset carries whichever theme colour it was baked with, and these two sit in every
/// list. Both are drawn in the same 24-point box as the discipline marks so a row can put them
/// side by side without measuring.
/// </remarks>
public static class RowGlyph
{
    /// <summary>The side of the square the paths are drawn in.</summary>
    public const double ViewBox = 24.0;

    /// <summary>Onwards, inside the app.</summary>
    public const string Chevron = "M9,5 L17,12 L9,19";

    /// <summary>
    /// Out of the app: a box with an arrow leaving it. The arrow starts inside the box and crosses
    /// its corner, because an arrow that merely sits beside a box reads as a direction rather than
    /// as a departure.
    /// </summary>
    public const string External = "M13,4 L20,4 L20,11 M20,4 L11.5,12.5 M18,14 L18,20 L4,20 L4,6 L10,6";
}
