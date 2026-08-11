using System.Text.Json.Serialization;

namespace Orientera.Domain;

/// <summary>One result inside the Sverigelistan average.</summary>
public sealed record RankingResult
{
    public required CompetitionId Competition { get; init; }
    public required string CompetitionName { get; init; }
    public required DateOnly Date { get; init; }
    public required double Points { get; init; }

    /// <summary>Whether this result is one of the six that make up the current average.</summary>
    public required bool IsCounting { get; init; }

    /// <summary>Sverigelistan counts exactly one year back — this is when the result drops out.</summary>
    public required DateOnly ExpiresOn { get; init; }

    public bool ExpiresSoon(DateOnly today) =>
        IsCounting && ExpiresOn <= today.AddDays(45);
}

/// <summary>Sverigelistan at a point in time. One signal into prediction, never the whole truth.</summary>
public sealed record RankingSnapshot
{
    public required PersonId Person { get; init; }
    public required DateOnly Date { get; init; }
    public required double Points { get; init; }
    public required int NationalPlace { get; init; }

    /// <summary>Points change since the previous snapshot.</summary>
    public required double Trend { get; init; }

    /// <summary>
    /// Sverigelistan is published to two decimals and the differences are small — a runner can sit
    /// a few hundredths from the place above. Rounding to whole points threw that away.
    /// </summary>
    public IReadOnlyDictionary<Discipline, double> DisciplinePoints { get; init; } =
        new Dictionary<Discipline, double>();

    public required IReadOnlyList<RankingResult> Results { get; init; }

    [JsonIgnore]
    public IEnumerable<RankingResult> Counting => Results.Where(r => r.IsCounting);
}
