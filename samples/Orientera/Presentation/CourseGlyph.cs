namespace Orientera.Presentation;

/// <summary>
/// The course as a card-sized mark: a start, a leg, and the control it arrives at.
/// </summary>
/// <remarks>
/// The same vocabulary as <see cref="DisciplineGlyph"/> — route into a control ring — but drawn
/// for a surface rather than a row. It is not that mark scaled up: the small one is proportioned
/// to read at sixteen points, and at ten times the size its stroke turns into a rope and the
/// control into a bicycle wheel.
/// <para>
/// Drawn rather than shipped as an image, for the reason the other marks are: a raster carries
/// whichever theme colour it was baked with, and this one lies behind text in both.
/// </para>
/// <para>
/// The leg stops short of the ring, as it does in the small mark. Without the gap the two read as
/// one closed shape — a balloon on a string rather than a leg arriving at a control.
/// </para>
/// </remarks>
public static class CourseGlyph
{
    /// <summary>The side of the square the path is drawn in.</summary>
    public const double ViewBox = 100.0;

    /// <summary>Start triangle, leg, and control — one geometry, because a Path takes one.</summary>
    /// <remarks>
    /// The triangle points the way the runner leaves it, as it does on a real map. Drawn the other
    /// way up it reads as an arrowhead dropped on the card rather than as a start.
    /// </remarks>
    public const string Course =
        "M10,66 L18,81 L2,81 Z " +
        "M10,62 C10,42 34,44 46,36 C58,28 52,14 66,14 " +
        "M84,14 m-13,0 a13,13 0 1,0 26,0 a13,13 0 1,0 -26,0";
}
