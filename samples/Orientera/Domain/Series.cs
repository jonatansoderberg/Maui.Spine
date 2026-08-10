namespace Orientera.Domain;

public sealed record Series
{
    public required SeriesId Id { get; init; }
    public required string Name { get; init; }

    /// <summary>How many rounds count towards the total; the rest are dropped.</summary>
    public required int CountingRounds { get; init; }
}

public sealed record SeriesRoundResult
{
    public required CompetitionId Competition { get; init; }
    public required string CompetitionName { get; init; }
    public required DateOnly Date { get; init; }
    public int? Place { get; init; }
    public required int Points { get; init; }

    /// <summary>False for a dropped (struket) result.</summary>
    public required bool IsCounting { get; init; }
}

public sealed record SeriesStanding
{
    public required SeriesId Series { get; init; }
    public required PersonId Person { get; init; }
    public required string Class { get; init; }
    public required int Place { get; init; }
    public required int TotalPoints { get; init; }
    public required IReadOnlyList<SeriesRoundResult> Rounds { get; init; }

    public SeriesRoundResult? NextRound(DateOnly today) =>
        Rounds.Where(r => r.Date > today).OrderBy(r => r.Date).FirstOrDefault();
}
