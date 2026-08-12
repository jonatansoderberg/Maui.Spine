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

    /// <summary>What has already been run. Off to one side, because this tab is for planning.</summary>
    Past,
}

/// <summary>How far ahead to look, in the words the sheet offers.</summary>
public enum EventPeriod
{
    Any,
    ThisMonth,
    NextMonth,
    ThreeMonths,
    RestOfYear,
}

/// <summary>The advanced filter, returned as a typed result from <see cref="EventFilterSheet"/>.</summary>
public sealed record EventFilter
{
    public static EventFilter Default { get; } = new();

    /// <summary>
    /// Districts to keep. Empty means every district — a filter nobody has set must not hide
    /// anything.
    /// </summary>
    public IReadOnlySet<string> Districts { get; init; } = new HashSet<string>();

    /// <summary>Free text over name, organiser and place. Empty means no search.</summary>
    public string Query { get; init; } = string.Empty;

    public EventPeriod Period { get; init; }

    /// <summary>Competitions at this level or more significant. Null means any level.</summary>
    public CompetitionLevel? MinimumLevel { get; init; }

    public Discipline? Discipline { get; init; }

    public double? MaxDistanceKm { get; init; }

    /// <summary>Training and recreational events are noise for most users, so they hide by default.</summary>
    public bool ShowTraining { get; init; }

    public bool OnlyMyClass { get; init; }

    public bool OnlyRegisterable { get; init; }

    public bool IsActive =>
        Districts.Count > 0
        || Query.Length > 0
        || Period != EventPeriod.Any
        || MinimumLevel is not null
        || Discipline is not null
        || MaxDistanceKm is not null
        || ShowTraining
        || OnlyMyClass
        || OnlyRegisterable;

    /// <summary>The window the period asks for, or null for any date.</summary>
    public (DateOnly From, DateOnly To)? Window(DateOnly today) => Period switch
    {
        EventPeriod.ThisMonth => (today, new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
        EventPeriod.NextMonth => (
            new DateOnly(today.Year, today.Month, 1).AddMonths(1),
            new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1)),
        EventPeriod.ThreeMonths => (today, today.AddMonths(3)),
        EventPeriod.RestOfYear => (today, new DateOnly(today.Year, 12, 31)),
        _ => null,
    };

    /// <summary>
    /// Whether the text matches. Matching is on what a person would type — the competition's
    /// name, who arranges it and where it is — and never on ids.
    /// </summary>
    public bool Matches(Competition competition) =>
        Query.Length == 0
        || competition.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)
        || competition.Organiser.Contains(Query, StringComparison.OrdinalIgnoreCase)
        || competition.Place.Contains(Query, StringComparison.OrdinalIgnoreCase)
        || competition.District.Contains(Query, StringComparison.OrdinalIgnoreCase);

    public int ActiveCount =>
        (Districts.Count > 0 ? 1 : 0)
        + (Query.Length > 0 ? 1 : 0)
        + (Period != EventPeriod.Any ? 1 : 0)
        + (MinimumLevel is not null ? 1 : 0)
        + (Discipline is not null ? 1 : 0)
        + (MaxDistanceKm is not null ? 1 : 0)
        + (ShowTraining ? 1 : 0)
        + (OnlyMyClass ? 1 : 0)
        + (OnlyRegisterable ? 1 : 0);
}
