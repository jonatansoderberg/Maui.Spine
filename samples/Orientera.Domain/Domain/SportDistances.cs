namespace Orientera.Domain;

/// <summary>
/// Which distances each sport actually races.
/// </summary>
/// <remarks>
/// The two axes do not multiply out. There is no indoor ultralong and no ski relay over a
/// marathon; offering all six distances under all six sports made thirty-six choices of which
/// most describe nothing.
/// <para>
/// From the international federation's own discipline lists: mountain bike and ski orienteering
/// race sprint, middle, long and relay. Indoor is not a distance at all — Eventor classifies an
/// indoor race as a sprint, and the sport is the whole answer. Trail-O and orienteering shooting
/// have distances of their own (PreO, TempO) that this app does not model, so they too are
/// answered by the sport alone.
/// </para>
/// <para>
/// Deliberately conservative, and it governs only what can be <em>chosen</em> as a favourite —
/// never what the calendar may contain. A night MTBO race is rare in Sweden and is not offered
/// here; if one is published it still appears in the list, it just cannot be anybody's
/// favourite kind.
/// </para>
/// </remarks>
public static class SportDistances
{
    public static IReadOnlyList<Discipline> For(Sport sport) => sport switch
    {
        Sport.Foot =>
        [
            Discipline.Sprint,
            Discipline.Middle,
            Discipline.Long,
            Discipline.UltraLong,
            Discipline.Night,
            Discipline.Relay,
        ],

        Sport.MountainBike or Sport.Ski =>
        [
            Discipline.Sprint,
            Discipline.Middle,
            Discipline.Long,
            Discipline.Relay,
        ],

        _ => [],
    };

    /// <summary>Whether the distance says anything about a race in this sport.</summary>
    public static bool HasDistances(Sport sport) => For(sport).Count > 0;
}
