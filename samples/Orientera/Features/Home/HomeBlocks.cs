using Orientera.Domain;

namespace Orientera.Features.Home;

/// <summary>
/// A block on Hem. Few large blocks, never a dense dashboard — the order comes from the
/// Context Engine, never from user configuration in v1.
/// </summary>
public abstract record HomeBlock
{
    public required string SectionLabel { get; init; }
}

public sealed record LiveNowBlock : HomeBlock
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string MyStatus { get; init; }
    public required string ActionText { get; init; }
}

public sealed record NextForMeBlock : HomeBlock
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }
    public required string WhenText { get; init; }
    public required string PlaceText { get; init; }
    public required string StartText { get; init; }
    public required bool HasStart { get; init; }
    public required string StateText { get; init; }
    public required string ActionText { get; init; }
}

public sealed record LatestResultBlock : HomeBlock
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }
    public required string PlaceText { get; init; }
    public required string TimeText { get; init; }
    public required string BehindText { get; init; }
    public required string ActionText { get; init; }
    public required bool HasSplits { get; init; }
}

public sealed record GroupBlock : HomeBlock
{
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Lines { get; init; }
}

public sealed record DiscoveryBlock : HomeBlock
{
    public required CompetitionId Competition { get; init; }
    public required string Title { get; init; }
    public required string WhenText { get; init; }
    public required string ReasonText { get; init; }
}

public sealed record DevelopmentBlock : HomeBlock
{
    public required string PointsText { get; init; }
    public required string PlaceText { get; init; }
    public required string TrendText { get; init; }
    public required bool IsImproving { get; init; }
}
