using System.Text.Json.Serialization;
using Orientera.Domain;

namespace Orientera.Services.Sources;

/// <summary>
/// Every data source sits behind an interface from day one, so the FakeData implementation
/// survives as a demo/test mode for the product's whole life and M1 only swaps in a BFF-backed
/// implementation behind the same contracts.
/// </summary>
public interface IEventSource
{
    Task<IReadOnlyList<Competition>> GetCompetitionsAsync(CancellationToken cancellationToken = default);

    Task<Competition?> GetCompetitionAsync(CompetitionId id, CancellationToken cancellationToken = default);

    Task<Course?> GetCourseAsync(CompetitionId id, string className, CancellationToken cancellationToken = default);

    Task<Series?> GetSeriesAsync(SeriesId id, CancellationToken cancellationToken = default);

    /// <summary>Locally starred competitions — works without an account, per the product principles.</summary>
    Task<IReadOnlySet<CompetitionId>> GetFavouritesAsync(CancellationToken cancellationToken = default);

    Task<bool> ToggleFavouriteAsync(CompetitionId competition, CancellationToken cancellationToken = default);
}

public interface IPeopleSource
{
    Task<Person> GetMeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowedPerson>> GetMyGroupAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Person>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task FollowAsync(PersonId person, FollowReason reason, CancellationToken cancellationToken = default);

    Task UnfollowAsync(PersonId person, CancellationToken cancellationToken = default);
}

public interface IParticipationSource
{
    Task<IReadOnlyList<CompetitionEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Start>> GetStartsAsync(CompetitionId competition, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompetitionResult>> GetResultsAsync(CompetitionId competition, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompetitionResult>> GetResultsForPersonAsync(PersonId person, CancellationToken cancellationToken = default);

    Task<Prediction?> GetPredictionAsync(CompetitionId competition, PersonId person, CancellationToken cancellationToken = default);
}

public enum LiveStatus
{
    NotStarted,
    Running,
    Finished,
    Mispunch,
    DidNotFinish,
}

/// <summary>A radio control on a class' course, as the organiser numbers it.</summary>
public sealed record LiveControl
{
    public required int Code { get; init; }

    /// <summary>The printed control code — "79" for the control the timing system calls 1079.</summary>
    public required string Name { get; init; }
}

/// <summary>One runner past one radio control.</summary>
public sealed record LivePassing
{
    public required int Control { get; init; }

    /// <summary>Time from this runner's own start, not a clock reading.</summary>
    public required TimeSpan Elapsed { get; init; }

    /// <summary>Standing in the class at this control among everyone who has passed it.</summary>
    public int? Place { get; init; }

    public TimeSpan? Behind { get; init; }
}

public sealed record LiveEntry
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public string? ClubLogo { get; init; }
    public required string Class { get; init; }

    /// <summary>
    /// Null when the source has no start time for this runner. It is what separates a runner who
    /// has not started yet from one who never will, and both arrive as
    /// <see cref="LiveStatus.NotStarted"/>.
    /// </summary>
    /// <remarks>
    /// Not <c>required</c>: the BFF omits null members (<see cref="OrienteraJson"/>), so a
    /// required property that is allowed to be null is one the app cannot deserialise.
    /// </remarks>
    public DateTimeOffset? StartTime { get; init; }

    public required LiveStatus Status { get; init; }

    /// <summary>Every radio control passed so far, in course order.</summary>
    public IReadOnlyList<LivePassing> Passings { get; init; } = [];

    /// <summary>Where the runner was last seen, or null before the first radio.</summary>
    [JsonIgnore]
    public LivePassing? LastPassing => Passings.Count > 0 ? Passings[^1] : null;

    /// <summary>Provisional position in the class at the last common control.</summary>
    public int? Position { get; init; }

    public TimeSpan? FinishTime { get; init; }
    public int? FinalPlace { get; init; }

    /// <summary>Behind the class winner at the finish.</summary>
    public TimeSpan? FinishBehind { get; init; }
}

public sealed record LiveSnapshot
{
    public required CompetitionId Competition { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required IReadOnlyList<LiveEntry> Entries { get; init; }

    /// <summary>
    /// The radio controls each class passes, in course order. Classes run different courses, so
    /// the columns of a split table belong to the class and not to the competition.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<LiveControl>> Controls { get; init; } =
        new Dictionary<string, IReadOnlyList<LiveControl>>();

    public IReadOnlyList<LiveControl> ControlsFor(string className) =>
        Controls.TryGetValue(className, out var controls) ? controls : [];
}

public interface ILiveSource
{
    /// <summary>Competitions with something happening right now.</summary>
    Task<IReadOnlyList<Competition>> GetLiveCompetitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The live field. <paramref name="className"/> narrows it to one class, which is what a
    /// runner watching their own race needs — the live source is only searchable by class, so
    /// everything else costs one request per class in the competition.
    /// </summary>
    Task<LiveSnapshot> GetSnapshotAsync(
        CompetitionId competition,
        string? className = null,
        CancellationToken cancellationToken = default);
}

public interface IProgressSource
{
    Task<RankingSnapshot?> GetRankingAsync(PersonId person, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeriesStanding>> GetSeriesStandingsAsync(PersonId person, CancellationToken cancellationToken = default);
}
