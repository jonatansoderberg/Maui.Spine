using Orientera.Domain;

namespace Orientera.Services.Sources;

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

public interface IArenaImageSource
{
    /// <summary>
    /// Tävlingens arenabild, eller <c>null</c> när den inte hunnit bli till. Ett <c>null</c>
    /// är samtidigt beställningen: nästa uppslag efter genereringen hittar bilden.
    /// </summary>
    Task<ArenaImage?> GetArenaImageAsync(CompetitionId competition, CancellationToken cancellationToken = default);
}
