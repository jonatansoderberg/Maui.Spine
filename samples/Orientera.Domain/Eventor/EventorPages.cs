namespace Orientera.Domain.Eventor;

/// <summary>
/// What <c>/Home/Index</c> says about whoever asked for it.
/// </summary>
/// <remarks>
/// One request answers three questions, which is why this page and not <c>/MyPages</c> is the one
/// the app asks: whether the session is still alive, who it belongs to, and whether that person's
/// club has Sverigelistan. Measured on #123 — logged out the page carries neither the name nor the
/// ranking box, and the ranking box is the only place a runner link appears.
/// </remarks>
public sealed record EventorStartPage
{
    /// <summary>The name Eventor greets the reader by. Absent means the session is not logged in.</summary>
    public string? Name { get; init; }

    /// <summary>The reader's own Eventor id, which only the ranking box carries.</summary>
    public string? PersonId { get; init; }

    /// <summary>The club, as the user menu states it — present with or without Sverigelistan.</summary>
    public string? Club { get; init; }

    /// <summary>The club's Eventor id, which the ranking box links to.</summary>
    public string? ClubId { get; init; }

    public bool IsLoggedIn => Name is { Length: > 0 };

    /// <summary>
    /// Whether Sverigelistan is readable at all. A logged-in reader without this is a member of a
    /// club that has not paid the fee — the page is the same one either way, minus the box.
    /// </summary>
    public bool HasRanking => PersonId is { Length: > 0 };
}

/// <summary>One runner on a competition's public entry list, before the draw.</summary>
/// <remarks>
/// A name and a club and nothing else, because that is all Eventor's page states. No person id, so
/// the reader is recognised in the list by <see cref="RunnerIdentity"/> — the same way the live
/// lists recognise them.
/// </remarks>
public sealed record EventorEntrant
{
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string Class { get; init; }
}

/// <summary>One race the reader has already run, as their own results page states it.</summary>
/// <remarks>
/// Carries the competition's name and date itself. The app's calendar only reaches a few months
/// back, so a result from January has no competition to borrow them from.
/// </remarks>
public sealed record EventorResult
{
    public required string EventId { get; init; }
    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required string Class { get; init; }

    /// <summary>The placement, when there was one. Null for a race that was not classified.</summary>
    public int? Place { get; init; }

    /// <summary>What the cell said — "28", or "ej godkänd", which is not a placement but is a fact.</summary>
    public required string PlaceText { get; init; }

    /// <summary>The distance the race's own name states, or null when it says nothing.</summary>
    public Discipline? Discipline { get; init; }

    public TimeSpan? Time { get; init; }
    public TimeSpan? Behind { get; init; }
}

/// <summary>One competition the reader is entered in and has not run yet.</summary>
/// <remarks>
/// No registration time, because <c>/MyPages/Events</c> does not carry one. The app needs to know
/// <em>that</em> the entry exists, not when it was made, and inventing a moment would put a
/// fabricated fact into the offline package where it would outlive the guess.
/// </remarks>
public sealed record EventorEntry
{
    /// <summary>Eventor's <c>eventId</c> — the same number the calendar knows the competition by.</summary>
    public required string EventId { get; init; }

    public required string Class { get; init; }
}

/// <summary>What <c>/MyPages/Settings</c> says about the reader's own preferences.</summary>
/// <remarks>
/// The club is here too, and here it does not depend on the ranking fee, which is what a club
/// without Sverigelistan needs for its activity list to still be readable.
/// </remarks>
public sealed record EventorSettings
{
    public string? Club { get; init; }

    public string? ClubId { get; init; }

    /// <summary>
    /// "Förvald klass 1" — the class the runner says they enter, not the one Sverigelistan ranks
    /// them in. Measured on #123: the same account entered H21 and was ranked in H45.
    /// </summary>
    public string? DefaultClass { get; init; }
}
