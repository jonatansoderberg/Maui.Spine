namespace Orientera.Domain;

public sealed record Person
{
    public required PersonId Id { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string District { get; init; }
    public required string DefaultClass { get; init; }
    public GeoPoint Home { get; init; }

    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[^1][0]}"
                : Name[..Math.Min(2, Name.Length)];
        }
    }
}

public enum FollowReason
{
    Family,
    Clubmate,
    Favourite,
}

/// <summary>
/// Min grupp is a personal list, not a social graph — no mutual consent, no profiles,
/// just the people whose orienteering this user wants to keep an eye on.
/// </summary>
public sealed record FollowedPerson
{
    public required Person Person { get; init; }
    public required FollowReason Reason { get; init; }
    public bool NotificationsEnabled { get; init; }
}
