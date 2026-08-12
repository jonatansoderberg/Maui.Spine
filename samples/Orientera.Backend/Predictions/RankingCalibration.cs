namespace Orientera.Backend.Predictions;

/// <summary>
/// Puts Sverigelistan on the same scale as a race result.
/// </summary>
/// <remarks>
/// The model compares runners by their time as a share of the winner's. Sverigelistan is a
/// different number — roughly how far behind a national standard a runner stands — so a ranking
/// cannot be dropped into a field beside a form ratio without being converted first.
///
/// The conversion is a straight line, fitted on the runners who have both a ranking and a form.
/// A line rather than something cleverer because the residual does not ask for more, and because
/// a line can be read off and argued with.
/// </remarks>
public sealed record RankingCalibration
{
    public required double Intercept { get; init; }
    public required double Slope { get; init; }

    /// <summary>
    /// How wrong the line usually is, as half the spread of its residuals. It is the spread a
    /// ranking-only runner is given: we have never watched them race, so what we do not know
    /// about them is exactly the scatter around this line.
    /// </summary>
    public required double Spread { get; init; }

    /// <summary>
    /// Fitted by <c>RankingPriorBacktest</c> on 244 competitions from 2026; see issue #113. Zero
    /// points lands at 1.046 — a runner at the national standard is about level with the winner —
    /// and every hundred points adds fifty-five percent to their time.
    /// </summary>
    public static readonly RankingCalibration Default = new()
    {
        Intercept = 1.0459,
        Slope = 0.00550,
        Spread = 0.1104,
    };

    public double RatioOf(double points) => Intercept + (Slope * points);

    /// <summary>Least squares, with the residual spread taken from the quartiles as elsewhere.</summary>
    public static RankingCalibration? Fit(IReadOnlyList<(double Points, double Ratio)> observations)
    {
        if (observations.Count < 20)
            return null;

        double meanPoints = observations.Average(o => o.Points);
        double meanRatio = observations.Average(o => o.Ratio);

        double covariance = observations.Sum(o => (o.Points - meanPoints) * (o.Ratio - meanRatio));
        double variance = observations.Sum(o => (o.Points - meanPoints) * (o.Points - meanPoints));

        if (variance <= 0)
            return null;

        double slope = covariance / variance;
        double intercept = meanRatio - (slope * meanPoints);

        var residuals = observations
            .Select(o => o.Ratio - (intercept + (slope * o.Points)))
            .Order()
            .ToList();

        return new RankingCalibration
        {
            Intercept = intercept,
            Slope = slope,
            Spread = Math.Max((Quantile(residuals, 0.75) - Quantile(residuals, 0.25)) / 2, 0.01),
        };
    }

    private static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        double position = q * (sorted.Count - 1);
        int index = (int)Math.Floor(position);
        double fraction = position - index;

        return index + 1 < sorted.Count
            ? sorted[index] + ((sorted[index + 1] - sorted[index]) * fraction)
            : sorted[index];
    }
}
