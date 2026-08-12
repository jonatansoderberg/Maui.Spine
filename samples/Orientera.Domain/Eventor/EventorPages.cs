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
