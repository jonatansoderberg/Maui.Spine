using System.Text.Json.Serialization;

namespace Orientera.Domain;

public enum Discipline
{
    Sprint,
    Middle,
    Long,

    /// <summary>
    /// Longer than long, and its own thing to a runner deciding whether to go.
    /// </summary>
    /// <remarks>
    /// Eventor states it as <c>UltraLong</c> and it used to be folded into <see cref="Long"/>,
    /// which made a DM ultralång look like any other forest race in a list where the distance is
    /// the first thing read.
    /// </remarks>
    UltraLong,

    Night,
    Relay,

    /// <summary>
    /// Indoor orienteering, which the federation does not classify at all.
    /// </summary>
    /// <remarks>
    /// There is no <c>raceDistance</c> for it: Eventor calls Karlstad Indoor a sprint like any
    /// other. The name is the only thing that says otherwise, so this is read from it and is
    /// marked as unofficial wherever it is shown.
    /// </remarks>
    Indoor,
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

    /// <summary>The organising club's badge, where the federation has one.</summary>
    public string? OrganiserLogo { get; init; }
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

    /// <summary>
    /// Whether the first start is an actual time of day rather than a date with no time on it.
    /// </summary>
    /// <remarks>
    /// A calendar entry without a start time arrives as midnight, and midnight rendered as
    /// "första start 00:00" — a time the app appeared to know and did not. The ambiguity is
    /// theoretical: nothing starts at midnight, night races included, since those set off in the
    /// evening. Treating 00:00 as unset is therefore reading the encoding the source already uses.
    /// </remarks>
    [JsonIgnore]
    public bool HasFirstStart => FirstStart.TimeOfDay != TimeSpan.Zero;

    [JsonIgnore]
    public bool IsLowPriority => Level is CompetitionLevel.Training or CompetitionLevel.Recreational;
}
