using Orientera.Domain;

namespace Orientera.Presentation;

/// <summary>
/// The drawn mark for a discipline: a control ring with the route that leads to it.
/// </summary>
/// <remarks>
/// Drawn rather than shipped as an image, because the colour has to follow the theme and a
/// rasterised asset carries whichever one it was baked with. The path data is the same string the
/// design files under <c>Resources/Design/Disciplines</c> hold, and a test pins the two together
/// so the drawing in the app cannot quietly drift from the drawing that was approved.
/// <para>
/// The route stops short of the ring. Without that gap the two read as one closed shape — a
/// balloon on a string rather than a leg arriving at a control.
/// </para>
/// <para>
/// The four distances differ by degree, and degree is what a shape is worst at showing: at sixteen
/// points sprint's corners and ultralång's extra fold are close to indistinguishable. They are not
/// asked to carry the meaning alone — the word stands next to the mark everywhere it is used, and
/// the colour separates them at a glance. The mark is what makes a list scannable, not what makes
/// it readable.
/// </para>
/// </remarks>
public static class DisciplineGlyph
{
    /// <summary>The side of the square the paths are drawn in.</summary>
    public const double ViewBox = 24.0;

    /// <summary>The control at the end of the route, in the same place in every mark.</summary>
    private const string Control = "M17,6.8 m-3.4,0 a3.4,3.4 0 1,0 6.8,0 a3.4,3.4 0 1,0 -6.8,0";

    /// <summary>
    /// The route into the control, one per discipline. They fold more as the distance grows:
    /// sprint turns in corners because a sprint does, lång is a plain S, and ultralång is the
    /// same S folded twice more.
    /// </summary>
    public static string Path(Discipline discipline) => discipline switch
    {
        Discipline.Sprint => $"M3,18.8 L6.4,14.2 L9.2,17.4 L12.6,11.2 {Control}",
        Discipline.Middle => $"M3.2,19.6 C8.4,20 8.4,12.8 12.6,11.2 {Control}",
        Discipline.Long =>
            $"M6,20.2 C10.6,20.6 12.6,17.4 9.4,15.8 C6.2,14.2 8.8,11.4 12.6,11.2 {Control}",
        Discipline.UltraLong =>
            "M2.6,20.8 L6,20.8 C11.4,20.8 12.8,18.4 10,16.6 C7.2,14.8 7,13.2 9.8,12.5 "
            + "C11.3,12.15 12,11.7 12.6,11.2 " + Control,

        // Night and relay are not lengths, so they are not routes. A moon and a handover between
        // two runners — two different kinds of thing, which is what a shape can actually show.
        // Relay's two rings differ in size because the legs are not interchangeable.
        Discipline.Night => $"M10.4,12.6 A4.6,4.6 0 1,1 5.2,19.2 A3.6,3.6 0 0,0 10.4,12.6 {Control}",
        Discipline.Relay =>
            $"M6,17.6 m-2.3,0 a2.3,2.3 0 1,0 4.6,0 a2.3,2.3 0 1,0 -4.6,0 M8.6,15.1 L13.6,10.1 {Control}",
        _ => string.Empty,
    };

    /// <summary>
    /// The mark for what a competition counts as. Only a championship has one.
    /// </summary>
    /// <remarks>
    /// A trophy, in gold, because that is what a mästerskap is and no other level is. The rest of
    /// the ladder — nationell, distrikt, närtävling — differs by degree the way the distances do,
    /// and seven marks in one line of caption text is a row nobody can read. The word carries them.
    /// <para>
    /// Drawn here rather than lifted from an icon set that has one: a tapered bowl and open
    /// handles is the same idiom the distance marks are drawn in, and the cup has to sit at
    /// fourteen points beside caption text without turning into a blob.
    /// </para>
    /// </remarks>
    public static string LevelPath(CompetitionLevel level) => level switch
    {
        CompetitionLevel.Championship =>
            "M8.4,4.4 L15.6,4.4 C15.6,9.4 14.4,12 12,12 C9.6,12 8.4,9.4 8.4,4.4 Z "
            + "M8.4,5.6 C5.6,5.6 5.6,9.8 8.7,10.4 M15.6,5.6 C18.4,5.6 18.4,9.8 15.3,10.4 "
            + "M12,12 L12,16.6 M9,19.4 L15,19.4",

        _ => string.Empty,
    };
}
