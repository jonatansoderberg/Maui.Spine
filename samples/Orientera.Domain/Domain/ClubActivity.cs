namespace Orientera.Domain;

/// <summary>
/// One entry on a club's activity list: a relay to put a team together for, a training, a district
/// gathering.
/// </summary>
/// <remarks>
/// Not a competition, and deliberately not modelled as one. An activity often has no start time at
/// all — "10-mila 2027, Västervik" is a sign-up that closes ten months before anyone runs — and
/// what it does have is a deadline and a number of people who have said yes.
/// </remarks>
public sealed record ClubActivity
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Whose activity it is: the club, its district, or the federation.</summary>
    public required string Organisation { get; init; }

    /// <summary>Absent on most of them — a relay sign-up is not an appointment.</summary>
    public DateTimeOffset? StartsAt { get; init; }

    public DateTimeOffset? EntryDeadline { get; init; }

    /// <summary>How many from the organisation have signed up so far.</summary>
    public required int EntryCount { get; init; }

    /// <summary>
    /// Whether Eventor still offers to sign up. Taken from the page having the link, not from
    /// comparing the deadline to a clock — the organiser can close or reopen it either way.
    /// </summary>
    public required bool IsOpen { get; init; }

    /// <summary>The activity in Eventor. Signing up happens there; the app only points at it.</summary>
    public required string Url { get; init; }
}
