namespace Orientera.Domain;

public sealed record Control
{
    public required int Number { get; init; }
    public required string Code { get; init; }
    public GeoPoint Location { get; init; }
}

public sealed record Course
{
    public required CompetitionId Competition { get; init; }
    public required string Class { get; init; }
    public required double LengthKm { get; init; }
    public required int ClimbMeters { get; init; }
    public required IReadOnlyList<Control> Controls { get; init; }
}

/// <summary>A recorded GPS track. Populated from GPX/FIT import in M4.</summary>
public sealed record Route
{
    public required PersonId Person { get; init; }
    public required CompetitionId Competition { get; init; }
    public required IReadOnlyList<GeoPoint> Points { get; init; }
}
