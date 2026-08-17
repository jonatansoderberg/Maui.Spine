namespace Orientera.Domain;

/// <summary>
/// The distance a race's own name states, when it states one.
/// </summary>
/// <remarks>
/// Eventor writes the distance into the name whenever the calendar entry cannot carry it: a
/// multi-day event is one id and many races — O-Ringen's stages all share <c>eventId=50594</c> —
/// so the id is asked once and answers the same thing five times, while the row says
/// "O-Ringen Göteborg, etapp 3, medel" and means it.
/// <para>
/// Lives here rather than inside one parser because three readers need the same answer: the
/// results page, Sverigelistan's counting races, and the entries. Each had its own copy, or none.
/// </para>
/// <para>
/// Reads the <em>name</em>, never the class. Eventor's class column carries course difficulty —
/// "Lätt", "Medel", "Svår" — and a runner on the medium course at a sprint has not run a middle
/// distance.
/// </para>
/// </remarks>
public static class DisciplineNames
{
    public static Discipline? In(string name)
    {
        var text = name.ToLowerInvariant();

        // Longest first: "ultralång" contains "lång", and indoor is a sprint that is not one.
        if (text.Contains("indoor")) return Discipline.Indoor;
        if (text.Contains("ultralång")) return Discipline.UltraLong;
        if (text.Contains("stafett")) return Discipline.Relay;
        if (text.Contains("natt")) return Discipline.Night;
        if (text.Contains("sprint")) return Discipline.Sprint;
        if (text.Contains("medel")) return Discipline.Middle;
        if (text.Contains("lång")) return Discipline.Long;

        return null;
    }
}
