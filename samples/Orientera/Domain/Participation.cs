namespace Orientera.Domain;

/// <summary>An entry. <see cref="RegisteredAt"/> is what makes "am I registered?" a
/// function of the current time, so the time machine can rewind past the entry.</summary>
public sealed record CompetitionEntry
{
    public required CompetitionId Competition { get; init; }
    public required PersonId Person { get; init; }
    public required string Class { get; init; }
    public required DateTimeOffset RegisteredAt { get; init; }
}

public sealed record Start
{
    public required CompetitionId Competition { get; init; }
    public required PersonId Person { get; init; }
    public required string Class { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public int? BibNumber { get; init; }
}

public enum ResultStatus
{
    Ok,
    Preliminary,
    Mispunch,
    DidNotFinish,
    DidNotStart,
}

public sealed record CompetitionResult
{
    public required ResultId Id { get; init; }
    public required CompetitionId Competition { get; init; }
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string Class { get; init; }
    public required ResultStatus Status { get; init; }
    public TimeSpan? Time { get; init; }
    public int? Place { get; init; }
    public TimeSpan? BehindWinner { get; init; }
    public int Starters { get; init; }
    public IReadOnlyList<Split> Splits { get; init; } = [];
}
