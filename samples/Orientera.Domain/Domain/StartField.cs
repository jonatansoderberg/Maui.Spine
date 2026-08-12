namespace Orientera.Domain;

/// <summary>
/// One runner in a start field, with where Sverigelistan puts them.
/// </summary>
/// <remarks>
/// Deliberately not a forecast. Three measurements (#40, #113, #117) said the same thing about
/// predicting a placement: in a field of forty the honest interval covers half of it, which is
/// formally right and says nothing. This is the same information without the pretence — who is
/// running, and how the list ranks them.
/// </remarks>
public sealed record StartFieldRunner
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Sverigelistan's average. Absent for anyone the list does not carry.</summary>
    public double? Points { get; init; }

    /// <summary>Their place on the national list, as the club page states it.</summary>
    public int? NationalRank { get; init; }

    /// <summary>Whether this is the runner reading the app.</summary>
    public bool IsMe { get; init; }
}
