namespace Orientera.Domain;

/// <summary>
/// A placement forecast, always an interval — "Förväntad placering: 8–15", never false
/// precision. Missing data, very technical terrain or a small field widens the interval.
/// </summary>
public sealed record Prediction
{
    public required CompetitionId Competition { get; init; }
    public required PersonId Person { get; init; }
    public required string Class { get; init; }
    public required int LowPlace { get; init; }
    public required int HighPlace { get; init; }
    public required int FieldSize { get; init; }

    /// <summary>0–1. Drives how strongly the UI commits to the interval.</summary>
    public required double Confidence { get; init; }

    /// <summary>Plain-language reasons, shown in PredictionInfoSheet so the number is explainable.</summary>
    public required IReadOnlyList<string> Drivers { get; init; }

    public required string ModelVersion { get; init; }

    public string Range => $"{LowPlace}–{HighPlace}";
}
