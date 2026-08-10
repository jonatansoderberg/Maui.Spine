using Orientera.Domain;

namespace Orientera.Services.FakeData;

/// <summary>
/// One runner's complete race, seeded up front. Live and results are both projections of
/// this: the live snapshot asks "how far had this run got at time T", the result list asks
/// "how did this run end". That keeps the two views consistent by construction, and makes
/// live a pure function of the clock.
/// </summary>
public sealed record PlannedRun
{
    public required Person Person { get; init; }
    public required CompetitionId Competition { get; init; }
    public required string Class { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public required IReadOnlyList<Split> Splits { get; init; }
    public required ResultStatus Status { get; init; }

    public TimeSpan TotalTime => Splits.Count > 0 ? Splits[^1].ElapsedTime : TimeSpan.Zero;

    public DateTimeOffset FinishAt => StartTime + TotalTime;

    public bool HasFinishedBy(DateTimeOffset now) =>
        Status is not (ResultStatus.DidNotStart or ResultStatus.DidNotFinish) && now >= FinishAt;

    public bool HasStartedBy(DateTimeOffset now) =>
        Status != ResultStatus.DidNotStart && now >= StartTime;
}
