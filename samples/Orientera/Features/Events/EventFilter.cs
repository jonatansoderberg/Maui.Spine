using Orientera.Domain;

namespace Orientera.Features.Events;

/// <summary>
/// The quick filters from the spec. One at a time — they are presets, not a matrix; the
/// advanced combinations live in <see cref="EventFilter"/> behind the filter sheet.
/// </summary>
public enum QuickFilter
{
    ForYou,
    Near,
    District,
    Bigger,
    ThisWeek,
    Mine,
    Favourites,
}

/// <summary>The advanced filter, returned as a typed result from <see cref="EventFilterSheet"/>.</summary>
public sealed record EventFilter
{
    public static EventFilter Default { get; } = new();

    /// <summary>Competitions at this level or more significant. Null means any level.</summary>
    public CompetitionLevel? MinimumLevel { get; init; }

    public Discipline? Discipline { get; init; }

    public double? MaxDistanceKm { get; init; }

    /// <summary>Training and recreational events are noise for most users, so they hide by default.</summary>
    public bool ShowTraining { get; init; }

    public bool OnlyMyClass { get; init; }

    public bool OnlyRegisterable { get; init; }

    public bool IsActive =>
        MinimumLevel is not null
        || Discipline is not null
        || MaxDistanceKm is not null
        || ShowTraining
        || OnlyMyClass
        || OnlyRegisterable;

    public int ActiveCount =>
        (MinimumLevel is not null ? 1 : 0)
        + (Discipline is not null ? 1 : 0)
        + (MaxDistanceKm is not null ? 1 : 0)
        + (ShowTraining ? 1 : 0)
        + (OnlyMyClass ? 1 : 0)
        + (OnlyRegisterable ? 1 : 0);
}
