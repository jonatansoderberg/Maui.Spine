using Orientera.Domain;

namespace Orientera.Backend.Arena;

/// <summary>Årstiden en arenabild visar. Den följer tävlingens datum, inte betraktarens.</summary>
public enum ArenaSeason
{
    Var,
    Sommar,
    Host,
    Vinter,

    /// <summary>Inomhus: ingen terräng att visa, utan en bild ur ett generiskt bibliotek.</summary>
    Inomhus,
}

/// <summary>
/// Vilken bild en tävling ska ha. Nyckeln är hela identiteten — två tävlingar som ger samma
/// nyckel får samma bild, och det är meningen: samma arena i samma årstid ser likadan ut.
/// </summary>
/// <param name="EventId">Eventors id. Arena och tävlingsområde hämtas därifrån vid generering.</param>
/// <param name="Season">Följer tävlingsdatumet, så en februaritävling visas i snö.</param>
/// <param name="Night">Nattävlingar renderas i månljus, med arenan upplyst.</param>
/// <param name="Version">Renderarens generation, ur <see cref="Configuration.ArenaImageOptions"/>.</param>
public readonly record struct ArenaImageKey(
    string EventId,
    ArenaSeason Season,
    bool Night,
    int Version)
{
    /// <summary>
    /// Blobnamnet. Årstid och natt står i klartext i namnet snarare än i metadata, för det gör
    /// felsökning möjlig med enbart en filbläddrare: visas fel bild syns orsaken direkt.
    /// </summary>
    public string BlobName =>
        $"v{Version}/{EventId}-{Season.ToString().ToLowerInvariant()}{(Night ? "-natt" : string.Empty)}.png";

    public static ArenaImageKey For(Competition competition, int version) =>
        new(competition.Id.Value,
            competition.Discipline == Discipline.Indoor
                ? ArenaSeason.Inomhus
                : SeasonOf(competition.FirstStart),
            competition.Discipline == Discipline.Night,
            version);

    /// <summary>
    /// Månaden räcker. Mars i Skåne och mars i Jämtland är inte samma sak, men datumet är allt
    /// tävlingen ger oss — och en snöbild har oftare fel i söder än rätt i norr.
    /// </summary>
    internal static ArenaSeason SeasonOf(DateTimeOffset when) => when.Month switch
    {
        12 or 1 or 2 or 3 => ArenaSeason.Vinter,
        4 => ArenaSeason.Var,
        5 or 6 or 7 or 8 => ArenaSeason.Sommar,
        _ => ArenaSeason.Host,
    };
}

/// <summary>En färdig arenabild, som appen hämtar direkt från lagringen.</summary>
public sealed record ArenaImage
{
    public required string Url { get; init; }
    public required ArenaSeason Season { get; init; }
    public required bool Night { get; init; }

    /// <summary>
    /// Ortofoto och höjdmodell är CC BY 4.0. Bilden bär ingen text alls, så attributionen kan
    /// inte följa med i den — den måste följa med hit och visas bredvid bilden i appen. Utan
    /// den är visningen inte licensenlig, och det är inget appen kan gissa sig till.
    /// </summary>
    public required string Attribution { get; init; }

    /// <summary>
    /// Sant för inomhusbilder, som föreställer en skola i allmänhet och ingen i synnerhet.
    /// Appen måste kunna säga det, annars läser betraktaren den som en bild av just sin skola.
    /// </summary>
    public required bool IsGeneric { get; init; }
}
