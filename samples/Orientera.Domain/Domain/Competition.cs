using System.Text.Json.Serialization;

namespace Orientera.Domain;

public enum Discipline
{
    Sprint,
    Middle,
    Long,
    Night,
    Relay,
}

/// <summary>
/// Competition level, ordered from most to least significant. Drives
/// <c>RelevanceEngine.ImportanceScore</c> and the "visa/dölj träningar" filter.
/// </summary>
public enum CompetitionLevel
{
    International,
    Championship,
    National,
    District,
    Local,
    Training,
    Recreational,
}

/// <summary>
/// When each piece of a competition becomes available. Every context state is derived from
/// these timestamps compared against "now", which is what makes the whole lifecycle
/// simulatable by moving the clock (see <c>TimeMachineClock</c>).
/// </summary>
public sealed record CompetitionSchedule
{
    public DateTimeOffset? RegistrationOpensAt { get; init; }
    public DateTimeOffset? EntryDeadline { get; init; }
    public DateTimeOffset? PmPublishedAt { get; init; }
    public DateTimeOffset? StartListPublishedAt { get; init; }
    public DateTimeOffset? ResultsPublishedAt { get; init; }
    public DateTimeOffset? SplitsPublishedAt { get; init; }
    public DateTimeOffset? MapPublishedAt { get; init; }
}

public enum DocumentKind
{
    Pm,
    Invitation,
    TerrainSample,
    OldMap,
    Accommodation,
}

public sealed record CompetitionDocument
{
    public required DocumentKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>A normalised event, independent of which system it came from.</summary>
public sealed record Competition
{
    public required CompetitionId Id { get; init; }
    public required string Name { get; init; }
    public required string Organiser { get; init; }
    public required string District { get; init; }
    public required string Place { get; init; }
    public required GeoPoint Location { get; init; }
    public required Discipline Discipline { get; init; }
    public required CompetitionLevel Level { get; init; }

    /// <summary>First start of the day. Also the competition's date.</summary>
    public required DateTimeOffset FirstStart { get; init; }

    /// <summary>When the arena closes — after this the competition is over even if results lag.</summary>
    public required DateTimeOffset LastFinish { get; init; }

    public CompetitionSchedule Schedule { get; init; } = new();
    public SeriesId? Series { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = [];
    public IReadOnlyList<CompetitionDocument> Documents { get; init; } = [];
    public CompetitionProfile? Profile { get; init; }

    [JsonIgnore]
    public DateOnly Date => DateOnly.FromDateTime(FirstStart.Date);

    [JsonIgnore]
    public bool IsLowPriority => Level is CompetitionLevel.Training or CompetitionLevel.Recreational;
}
