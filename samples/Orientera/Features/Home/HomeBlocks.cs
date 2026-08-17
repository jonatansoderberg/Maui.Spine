using Microsoft.Maui.Controls.Shapes;
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

/// <summary>
/// A block about one competition, and therefore one that can wear its marks.
/// </summary>
/// <remarks>
/// The distance and the level are the same two facts the list in Tävlingar shows, drawn the same
/// way. Hem was the one place that named a competition without saying what kind it was.
/// </remarks>
public abstract record CompetitionBlock : HomeBlock
{
    public required CompetitionId Competition { get; init; }

    /// <summary>The distance's mark, or null when the calendar does not say.</summary>
    public Geometry? DisciplineShape { get; init; }

    /// <summary>The name the glyph style picks its colour by.</summary>
    public string DisciplineKey { get; init; } = string.Empty;

    public string DisciplineLabel { get; init; } = string.Empty;

    public bool HasDisciplineShape => DisciplineShape is not null;

    /// <summary>The gold cup, for a championship. Null for every other level.</summary>
    public Geometry? LevelShape { get; init; }

    public string LevelLabel { get; init; } = string.Empty;

    public bool HasLevelShape => LevelShape is not null;
}

public sealed record LiveNowBlock : CompetitionBlock
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string MyStatus { get; init; }
    public required string ActionText { get; init; }
}

public sealed record NextForMeBlock : CompetitionBlock
{
    public required string Title { get; init; }
    public required string WhenText { get; init; }
    public required string PlaceText { get; init; }
    public required string StartText { get; init; }
    public required bool HasStart { get; init; }
    public required string StateText { get; init; }
    public required string ActionText { get; init; }
}

public sealed record LatestResultBlock : CompetitionBlock
{
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

public sealed record DiscoveryBlock : CompetitionBlock
{
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
