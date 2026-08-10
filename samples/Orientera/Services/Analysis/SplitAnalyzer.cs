using Orientera.Domain;

namespace Orientera.Services.Analysis;

/// <summary>
/// Turns raw splits into leg analysis — WinSplits++ in domain form.
/// </summary>
/// <remarks>
/// Loss to the best leg time is observed. Whether that loss is a *mistake* is modelled: a
/// runner who is 20% slower than the winner everywhere has not made eleven mistakes. So the
/// baseline is the runner's own median ratio to the best leg, and a leg counts as a likely
/// mistake only when it deviates from that personal pace. Everything derived here must be
/// presented as an estimate.
/// </remarks>
public static class SplitAnalyzer
{
    /// <summary>How much worse than the runner's own pace a leg must be to look like a mistake.</summary>
    public const double MistakeRatioThreshold = 1.30;

    /// <summary>Legs losing less than this are noise, however bad the ratio looks.</summary>
    public static readonly TimeSpan MinimumMistakeLoss = TimeSpan.FromSeconds(20);

    public static IReadOnlyList<LegAnalysis> Analyse(
        CompetitionResult result,
        IReadOnlyList<CompetitionResult> field)
    {
        if (result.Splits.Count == 0)
            return [];

        var comparable = field
            .Where(r => r.Class == result.Class && r.Splits.Count > 0)
            .ToList();

        var bestLegTimes = BestLegTimes(comparable);
        var ratios = new double[result.Splits.Count];

        for (int i = 0; i < result.Splits.Count; i++)
        {
            var best = bestLegTimes.GetValueOrDefault(result.Splits[i].ControlNumber, result.Splits[i].LegTime);
            ratios[i] = best > TimeSpan.Zero
                ? result.Splits[i].LegTime.TotalSeconds / best.TotalSeconds
                : 1.0;
        }

        double baseline = Median(ratios);
        var analysis = new List<LegAnalysis>(result.Splits.Count);

        foreach (var split in result.Splits)
        {
            var best = bestLegTimes.GetValueOrDefault(split.ControlNumber, split.LegTime);
            var loss = split.LegTime - best;
            double ratio = best > TimeSpan.Zero ? split.LegTime.TotalSeconds / best.TotalSeconds : 1.0;

            // What the leg would have cost at the runner's own pace; the rest looks like a mistake.
            var expected = TimeSpan.FromSeconds(best.TotalSeconds * baseline);
            var excess = split.LegTime - expected;

            bool isMistake = ratio >= baseline * MistakeRatioThreshold && excess >= MinimumMistakeLoss;

            analysis.Add(new LegAnalysis
            {
                ControlNumber = split.ControlNumber,
                ControlCode = split.ControlCode,
                LegTime = split.LegTime,
                BestLegTime = best,
                LossToBest = loss > TimeSpan.Zero ? loss : TimeSpan.Zero,
                LegPlace = LegPlace(comparable, split.ControlNumber, split.LegTime),
                PositionAfter = PositionAfter(comparable, split.ControlNumber, split.ElapsedTime),
                IsLikelyMistake = isMistake,
                MistakeConfidence = isMistake ? MistakeConfidence(ratio, baseline) : 0.0,
                EstimatedMistakeTime = isMistake && excess > TimeSpan.Zero ? excess : TimeSpan.Zero,
            });
        }

        return analysis;
    }

    /// <summary>
    /// The finish time without the legs flagged as mistakes. An estimate, and labelled as one
    /// wherever it is shown — it assumes the runner's own pace on those legs, nothing more.
    /// </summary>
    public static TimeSpan TheoreticalTime(TimeSpan actualTime, IReadOnlyList<LegAnalysis> legs)
    {
        var recovered = legs.Aggregate(TimeSpan.Zero, (sum, leg) => sum + leg.EstimatedMistakeTime);
        var theoretical = actualTime - recovered;
        return theoretical > TimeSpan.Zero ? theoretical : actualTime;
    }

    public static TimeSpan TotalMistakeTime(IReadOnlyList<LegAnalysis> legs) =>
        legs.Aggregate(TimeSpan.Zero, (sum, leg) => sum + leg.EstimatedMistakeTime);

    /// <summary>
    /// Spread of the runner's leg ratios: low means an even race, high means it swung.
    /// 1.0 is perfectly even.
    /// </summary>
    public static double StabilityIndex(IReadOnlyList<LegAnalysis> legs)
    {
        if (legs.Count == 0)
            return 1.0;

        var ratios = legs
            .Where(l => l.BestLegTime > TimeSpan.Zero)
            .Select(l => l.LegTime.TotalSeconds / l.BestLegTime.TotalSeconds)
            .ToArray();

        if (ratios.Length == 0)
            return 1.0;

        double mean = ratios.Average();
        double variance = ratios.Sum(r => (r - mean) * (r - mean)) / ratios.Length;

        return Math.Round(1.0 / (1.0 + Math.Sqrt(variance)), 3);
    }

    private static Dictionary<int, TimeSpan> BestLegTimes(IReadOnlyList<CompetitionResult> field)
    {
        var best = new Dictionary<int, TimeSpan>();

        foreach (var split in field.SelectMany(r => r.Splits))
        {
            if (!best.TryGetValue(split.ControlNumber, out var current) || split.LegTime < current)
                best[split.ControlNumber] = split.LegTime;
        }

        return best;
    }

    private static int LegPlace(IReadOnlyList<CompetitionResult> field, int control, TimeSpan legTime) =>
        field
            .SelectMany(r => r.Splits)
            .Count(s => s.ControlNumber == control && s.LegTime < legTime) + 1;

    private static int PositionAfter(IReadOnlyList<CompetitionResult> field, int control, TimeSpan elapsed) =>
        field
            .SelectMany(r => r.Splits)
            .Count(s => s.ControlNumber == control && s.ElapsedTime < elapsed) + 1;

    private static double MistakeConfidence(double ratio, double baseline)
    {
        double excessRatio = (ratio / baseline) - MistakeRatioThreshold;
        return Math.Round(Math.Clamp(0.55 + excessRatio, 0.0, 0.95), 2);
    }

    private static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        int mid = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
