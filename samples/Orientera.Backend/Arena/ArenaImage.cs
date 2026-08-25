using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Backend.Arena;

/// <summary>
/// Vilken bild en tävling ska ha. Nyckeln är hela identiteten — två tävlingar som ger samma
/// nyckel får samma bild, och det är meningen: samma arena i samma årstid ser likadan ut.
/// </summary>
/// <remarks>
/// Bilden själv — <see cref="ArenaImage"/> — och årstiden bor i domänens källkontrakt, så att
/// appen läser exakt den form backend serverar.
/// </remarks>
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
            competition.Sport == Sport.Indoor
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

    /// <summary>
    /// Tidpunkten bilden ljussätts för. Saknar tävlingen klockslag står <c>FirstStart</c> vid
    /// midnatt, solen under horisonten — och en dagtävling blev en nattbild. Då antas mitt på
    /// dagen, utom för nattävlingar som får 21:00: en gissning, men en betydligt bättre än
    /// 12:00 för ett lopp vars hela idé är att det är mörkt.
    /// </summary>
    internal static DateTime RenderTimeOf(Competition competition) =>
        competition.HasFirstStart
            ? competition.FirstStart.DateTime
            : competition.FirstStart.Date
                + TimeSpan.FromHours(competition.Discipline == Discipline.Night ? 21 : 12);
}
