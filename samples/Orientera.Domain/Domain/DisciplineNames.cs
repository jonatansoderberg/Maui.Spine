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

        // Longest first: "ultralång" contains "lång".
        if (text.Contains("ultralång")) return Discipline.UltraLong;
        if (text.Contains("stafett")) return Discipline.Relay;
        if (text.Contains("natt")) return Discipline.Night;
        if (text.Contains("sprint")) return Discipline.Sprint;
        if (text.Contains("medel")) return Discipline.Middle;
        if (text.Contains("lång")) return Discipline.Long;

        return null;
    }
}

/// <summary>
/// The sport a competition's own name states, for the calendar rows where the source does not.
/// </summary>
/// <remarks>
/// Swedish organisers put the sport in the title whenever it is not foot orienteering, because
/// their entrants need to know before they read anything else: "MTBO-träning Källviken",
/// "Skid-O SM", "Hallsberg Indoor sprint". Foot races almost never say "OL" — the absence is the
/// statement, which is why <see cref="In"/> answers null rather than <c>Foot</c> and leaves the
/// default to the caller.
/// </remarks>
public static class SportNames
{
    public static Sport? In(string name)
    {
        var text = name.ToLowerInvariant();

        if (text.Contains("indoor") || text.Contains("inomhus")) return Sport.Indoor;

        // "mtb-o", "mtbo" and the full word. Not bare "mtb": a foot race across a bike trail
        // centre is not mountain bike orienteering.
        if (text.Contains("mtbo") || text.Contains("mtb-o") || text.Contains("mountainbike"))
            return Sport.MountainBike;

        if (text.Contains("skid-o") || text.Contains("skido") || text.Contains("skidorientering"))
            return Sport.Ski;

        if (text.Contains("prego") || text.Contains("pre-o") || text.Contains("preo")
            || text.Contains("tempo-o") || text.Contains("trail-o"))
        {
            return Sport.PreO;
        }

        if (text.Contains("orienteringsskytte") || text.Contains("skytteorientering"))
            return Sport.Shooting;

        return null;
    }
}
