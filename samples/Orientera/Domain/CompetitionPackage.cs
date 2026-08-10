namespace Orientera.Domain;

/// <summary>
/// Everything about one competition that has to survive a dead mobile signal at the arena:
/// the PM, my start time, where to park, who in my group is running.
/// </summary>
/// <remarks>
/// Assembled while there is coverage and stored locally, per
/// <c>docs/krav/09-offline-notiser-resa.md</c>. Map data is deliberately absent — caching it
/// is rights-governed and belongs to M4.
/// </remarks>
public sealed record CompetitionPackage
{
    public required Competition Competition { get; init; }

    /// <summary>When the package was assembled. Shown to the user, never hidden.</summary>
    public required DateTimeOffset CachedAt { get; init; }

    public Start? MyStart { get; init; }

    /// <summary>When I entered — what the context engine needs to know I am registered.</summary>
    public DateTimeOffset? MyEntryRegisteredAt { get; init; }

    /// <summary>When the first person in Min grupp entered.</summary>
    public DateTimeOffset? GroupEntryRegisteredAt { get; init; }

    /// <summary>Starts for the people in Min grupp, for the shared family view before the race.</summary>
    public IReadOnlyList<Start> GroupStarts { get; init; } = [];

    /// <summary>The last results seen — present only once they had been published.</summary>
    public IReadOnlyList<CompetitionResult> Results { get; init; } = [];

    public Prediction? Prediction { get; init; }

    /// <summary>How stale the package is at <paramref name="now"/>.</summary>
    public TimeSpan AgeAt(DateTimeOffset now) => now - CachedAt;
}
