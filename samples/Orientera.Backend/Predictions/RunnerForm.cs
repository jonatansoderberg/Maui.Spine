using Orientera.Domain;

namespace Orientera.Backend.Predictions;

/// <summary>
/// What a runner's recent races say about their speed, expressed the only way that compares
/// across terrains and courses: their time as a share of the winner's time in the same class.
/// </summary>
/// <remarks>
/// The median rather than the mean, and a spread rather than a single number. One disastrous
/// race should not redefine a runner, and the spread is what an honest interval is made of —
/// a runner who is always within a few percent of the same ratio can be predicted far more
/// tightly than one who alternates between winning and getting lost.
/// </remarks>
public sealed record RunnerForm
{
    public required RunnerIdentity Identity { get; init; }

    /// <summary>Median time ratio to the winner. 1.0 is a winner; 1.2 is twenty percent behind.</summary>
    public required double Ratio { get; init; }

    /// <summary>Half the spread of the runner's own ratios — how much they vary race to race.</summary>
    public required double Spread { get; init; }

    public required int Races { get; init; }

    /// <summary>
    /// Whether this form is a ranking read through <see cref="RankingCalibration"/> rather than
    /// races we have watched. It changes what the forecast may claim, not how it is counted.
    /// </summary>
    public bool FromRanking { get; init; }

    /// <summary>
    /// A form for a runner nobody has watched race, out of their Sverigelistan alone.
    /// </summary>
    /// <remarks>
    /// This is what the ranking buys. Under three results a runner used to be unknown, and early
    /// in a season that was most of the field — SP-11 measured the interval swelling to more than
    /// half the field because of it. A ranking exists for four in five, all season.
    /// </remarks>
    public static RunnerForm Ranked(RunnerIdentity identity, double points, RankingCalibration calibration) =>
        new()
        {
            Identity = identity,
            Ratio = calibration.RatioOf(points),
            Spread = calibration.Spread,
            Races = 0,
            FromRanking = true,
        };

    /// <summary>
    /// A form that leans on the ranking while there are few races of our own, and on the races as
    /// they accumulate. <paramref name="rankingWeight"/> is the ranking's share of it.
    /// </summary>
    /// <remarks>
    /// Neither source is better everywhere. A ranking is six results across a whole year, steady
    /// but blind to the last month; our own races are current but few. Choosing between them
    /// throws away whichever is not chosen, and the backtest says the mixture beats both.
    /// </remarks>
    public static RunnerForm Blend(RunnerForm watched, RunnerForm ranked, double rankingWeight)
    {
        double weight = Math.Clamp(rankingWeight, 0, 1);

        return watched with
        {
            Ratio = ((1 - weight) * watched.Ratio) + (weight * ranked.Ratio),
            Spread = ((1 - weight) * watched.Spread) + (weight * ranked.Spread),
        };
    }

    public static RunnerForm? From(RunnerIdentity identity, IReadOnlyList<double> ratios)
    {
        // Two races say almost nothing about a spread, and a spread is what the interval is
        // made of. Below three, the runner is better treated as unknown.
        if (ratios.Count < 3)
            return null;

        var sorted = ratios.Order().ToList();

        return new RunnerForm
        {
            Identity = identity,
            Ratio = Median(sorted),
            Spread = Spread95(sorted),
            Races = sorted.Count,
        };
    }

    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;

    /// <summary>
    /// Half the distance between the runner's better and worse days, from the quartiles rather
    /// than the extremes: one race in the wrong marsh should widen the interval, not own it.
    /// </summary>
    private static double Spread95(IReadOnlyList<double> sorted)
    {
        double low = Quantile(sorted, 0.25);
        double high = Quantile(sorted, 0.75);

        return Math.Max((high - low) / 2, 0.01);
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
