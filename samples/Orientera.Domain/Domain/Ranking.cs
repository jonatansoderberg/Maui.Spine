using System.Text.Json.Serialization;

namespace Orientera.Domain;

/// <summary>One result inside the Sverigelistan average.</summary>
public sealed record RankingResult
{
    public required CompetitionId Competition { get; init; }
    public required string CompetitionName { get; init; }
    public required DateOnly Date { get; init; }
    public required int Points { get; init; }

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
    public required int Points { get; init; }
    public required int NationalPlace { get; init; }

    /// <summary>Points change since the previous snapshot.</summary>
    public required int Trend { get; init; }

    public IReadOnlyDictionary<Discipline, int> DisciplinePoints { get; init; } =
        new Dictionary<Discipline, int>();

    public required IReadOnlyList<RankingResult> Results { get; init; }

    [JsonIgnore]
    public IEnumerable<RankingResult> Counting => Results.Where(r => r.IsCounting);
}
