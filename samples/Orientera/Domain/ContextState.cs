namespace Orientera.Domain;

/// <summary>
/// Where a competition sits on the journey Upptäck → Anmälan → Förbered → Tävlingsdag →
/// Live → Resultat → Analys → Utvecklas. Declared in lifecycle order; the engine returns the
/// most advanced state whose conditions hold.
/// </summary>
public enum ContextState
{
    Discovered,
    RegistrationOpen,
    Registered,
    PmPublished,
    StartListPublished,
    RaceDay,
    Live,
    Finished,
    ResultsPublished,
    SplitsAvailable,
    MapAndAnalysisAvailable,
}

/// <summary>The one thing the user most likely wants to do next.</summary>
public enum ContextAction
{
    ShowCompetition,
    Register,
    Prepare,
    ReadPm,
    ShowMyStart,
    Navigate,
    FollowLive,
    ShowPreliminary,
    ShowMyResult,
    Analyse,
    ShowRouteChoice,
}

public sealed record ContextDecision
{
    public required ContextState State { get; init; }
    public required ContextAction PrimaryAction { get; init; }

    /// <summary>Swedish label for the primary CTA, straight out of the spec's state table.</summary>
    public required string PrimaryActionText { get; init; }

    /// <summary>Swedish label for the state itself, for badges and the time machine.</summary>
    public required string StateText { get; init; }
}

/// <summary>
/// Everything the context engine needs. Only the personal facts are passed in — every
/// availability signal is derived from the competition's schedule against
/// <see cref="Now"/>, which is what makes the lifecycle replayable by moving the clock.
/// </summary>
public sealed record ContextInput
{
    public required DateTimeOffset Now { get; init; }
    public required Competition Competition { get; init; }

    /// <summary>When I entered, or null if I never did.</summary>
    public DateTimeOffset? MyEntryRegisteredAt { get; init; }

    /// <summary>When the first person in Min grupp entered, or null.</summary>
    public DateTimeOffset? GroupEntryRegisteredAt { get; init; }

    /// <summary>My start time once the start list is out.</summary>
    public DateTimeOffset? MyStartTime { get; init; }
}
