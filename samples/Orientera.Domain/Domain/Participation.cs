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
    public string? ClubLogo { get; init; }
    public required string Class { get; init; }
    public required ResultStatus Status { get; init; }
    public TimeSpan? Time { get; init; }
    public int? Place { get; init; }
    public TimeSpan? BehindWinner { get; init; }
    public int Starters { get; init; }
    public IReadOnlyList<Split> Splits { get; init; } = [];

    /// <summary>
    /// What the competition was called and when it was, for results that travel without it.
    /// </summary>
    /// <remarks>
    /// A result read from a competition's own list needs neither — the caller already has the
    /// competition in hand. A season read off the runner's own page does: the calendar reaches a
    /// few months back and their January races are outside it, so a result that could not name
    /// itself would be dropped on the floor by the page that exists to show it.
    /// </remarks>
    public string? CompetitionName { get; init; }

    public DateOnly? CompetitionDate { get; init; }

    /// <summary>
    /// The distance, when the result knows it better than the calendar does.
    /// </summary>
    /// <remarks>
    /// Set for a stage of a multi-day event, where the calendar has one entry for the whole week
    /// and only the stage's own name says which distance was run.
    /// </remarks>
    public Discipline? CompetitionDiscipline { get; init; }
}
