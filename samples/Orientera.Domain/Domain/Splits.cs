namespace Orientera.Domain;

/// <summary>Observed data: what the punching system recorded.</summary>
public sealed record Split
{
    public required int ControlNumber { get; init; }
    public required string ControlCode { get; init; }
    public required TimeSpan LegTime { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
}

/// <summary>
/// Modelled data: what Orientera computed on top of the splits. Everything here except
/// <see cref="LegTime"/> and <see cref="LegPlace"/> is an estimate and must be presented as
/// one (<c>EstimateInk</c>) — the explainability principle, operationalised.
/// </summary>
public sealed record LegAnalysis
{
    public required int ControlNumber { get; init; }
    public required string ControlCode { get; init; }
    public required TimeSpan LegTime { get; init; }
    public required TimeSpan BestLegTime { get; init; }
    public required TimeSpan LossToBest { get; init; }
    public required int LegPlace { get; init; }
    public required int PositionAfter { get; init; }

    /// <summary>True when the leg deviates from this runner's own pace, not merely from the best.</summary>
    public required bool IsLikelyMistake { get; init; }

    /// <summary>0–1 for a likely mistake, 0 otherwise.</summary>
    public required double MistakeConfidence { get; init; }

    /// <summary>The part of <see cref="LossToBest"/> attributed to a mistake rather than to pace.</summary>
    public required TimeSpan EstimatedMistakeTime { get; init; }
}
