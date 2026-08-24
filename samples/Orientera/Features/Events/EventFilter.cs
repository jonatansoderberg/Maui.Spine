using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Features.Events;

/// <summary>
/// The quick filters from the spec. One at a time — they are presets, not a matrix; the
/// advanced combinations live in <see cref="EventFilter"/> behind the filter sheet.
/// </summary>
public enum QuickFilter
{
    ForYou,
    Near,

    /// <summary>The user's own district, whichever that is — the chip is labelled from it.</summary>
    District,
    Bigger,
    ThisWeek,
    Mine,
    Interested,

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

/// <summary>
/// One choice the user has made, and the filter with that choice taken back out.
/// </summary>
/// <remarks>
/// The chip row above the list removes one facet at a time, and carrying the result rather than a
/// discriminator keeps every "how do I unset this" in one place — the same place that decided how
/// to set it.
/// </remarks>
public sealed record FilterFacet(string Label, EventFilter Without)
{
    /// <summary>The chip is a remove button, and has to say so rather than read as a word.</summary>
    public string Accessibility => $"Ta bort filtret {Label}";
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

    /// <summary>
    /// The levels to keep, empty for all of them.
    /// </summary>
    /// <remarks>
    /// A set rather than the ladder cut this used to be ("this level and above"). A ladder cannot
    /// say "only närtävlingar" — the one thing a runner looking for something small and close on a
    /// Tuesday is asking for — and it could only ever reach four of the seven rungs.
    /// </remarks>
    public IReadOnlySet<CompetitionLevel> Levels { get; init; } = new HashSet<CompetitionLevel>();

    /// <summary>The disciplines to keep, empty for all of them.</summary>
    public IReadOnlySet<Discipline> Disciplines { get; init; } = new HashSet<Discipline>();

    public double? MaxDistanceKm { get; init; }

    /// <summary>Training and recreational events are noise for most users, so they hide by default.</summary>
    public bool ShowTraining { get; init; }

    public bool OnlyMyClass { get; init; }

    public bool OnlyRegisterable { get; init; }

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

    /// <summary>
    /// Whether the competition survives every rule in the filter. The reader is needed because
    /// three of the rules are about them: where they live, what class they run, and whether the
    /// entry is open now.
    /// </summary>
    public bool Includes(Competition competition, Person me, DateTimeOffset now)
    {
        if (Districts.Count > 0 && !Districts.Contains(competition.District))
            return false;

        if (!Matches(competition))
            return false;

        if (Window(DateOnly.FromDateTime(now.Date)) is { } window
            && (competition.Date < window.From || competition.Date > window.To))
        {
            return false;
        }

        // Training and recreational events are hidden unless explicitly asked for — the spec's
        // "minska Eventor-bruset" applied at its most common source. Asking for those levels by
        // name is asking for them, so the switch does not get to overrule the choice.
        if (competition.IsLowPriority && !ShowTraining && !Levels.Contains(competition.Level))
            return false;

        if (Levels.Count > 0 && !Levels.Contains(competition.Level))
            return false;

        if (Disciplines.Count > 0 && !Disciplines.Contains(competition.Discipline))
            return false;

        if (MaxDistanceKm is { } maxDistance && me.Home.DistanceKmTo(competition.Location) > maxDistance)
            return false;

        if (OnlyMyClass && competition.Classes.Count > 0 && !competition.Classes.Contains(me.DefaultClass))
            return false;

        if (OnlyRegisterable && !IsRegisterable(competition, now))
            return false;

        return true;
    }

    private static bool IsRegisterable(Competition competition, DateTimeOffset now) =>
        competition.Schedule is { RegistrationOpensAt: { } opens, EntryDeadline: { } deadline }
        && opens <= now
        && now <= deadline;

    /// <summary>
    /// Every set choice as its own removable chip, in the order the sheet asks for them.
    /// </summary>
    /// <remarks>
    /// The query is deliberately not among them. It is already visible in the search box on the
    /// page, with its own clear button, and a chip that says "DM" beside a field that says "DM"
    /// is two controls for one fact.
    /// </remarks>
    public IReadOnlyList<FilterFacet> Facets
    {
        get
        {
            var facets = new List<FilterFacet>();

            foreach (var district in Districts.OrderBy(d => d, StringComparer.CurrentCulture))
            {
                facets.Add(new FilterFacet(
                    district,
                    this with { Districts = Districts.Where(d => d != district).ToHashSet() }));
            }

            if (Period != EventPeriod.Any)
                facets.Add(new FilterFacet(PeriodLabel(Period), this with { Period = EventPeriod.Any }));

            foreach (var level in Levels.OrderBy(l => l))
            {
                facets.Add(new FilterFacet(
                    Format.Level(level),
                    this with { Levels = Levels.Where(l => l != level).ToHashSet() }));
            }

            foreach (var discipline in Disciplines.OrderBy(d => d))
            {
                facets.Add(new FilterFacet(
                    Format.Discipline(discipline),
                    this with { Disciplines = Disciplines.Where(d => d != discipline).ToHashSet() }));
            }

            if (MaxDistanceKm is { } distance)
                facets.Add(new FilterFacet($"Inom {distance:0} km", this with { MaxDistanceKm = null }));

            if (ShowTraining)
                facets.Add(new FilterFacet("Med träningar", this with { ShowTraining = false }));

            if (OnlyMyClass)
                facets.Add(new FilterFacet("Min klass", this with { OnlyMyClass = false }));

            if (OnlyRegisterable)
                facets.Add(new FilterFacet("Anmälningsbara", this with { OnlyRegisterable = false }));

            return facets;
        }
    }

    public static string PeriodLabel(EventPeriod period) => period switch
    {
        EventPeriod.ThisMonth => "Denna månad",
        EventPeriod.NextMonth => "Nästa månad",
        EventPeriod.ThreeMonths => "Inom tre månader",
        EventPeriod.RestOfYear => "Resten av året",
        _ => "Valfri tid",
    };
}
