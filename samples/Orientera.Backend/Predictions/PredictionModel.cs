using System.Globalization;
using Orientera.Domain;

namespace Orientera.Backend.Predictions;

/// <summary>
/// A placement forecast for one runner in one field, from nothing but past results.
/// </summary>
/// <remarks>
/// Deterministic on purpose (SP-11): the same field and the same history always give the same
/// interval, so the model can be backtested and a bad interval can be traced to a rule rather
/// than to chance. The interval is produced by asking how the field would place if the runner
/// had a good day and if they had a bad one — the ends are two counts of opponents, not a
/// spread painted around a guess.
/// </remarks>
public static class PredictionModel
{
    public const string Version = "form-ratio-1";

    /// <summary>
    /// Below this share of the field having a known form, the interval is honest only if it is
    /// wide — the unknown runners are counted as if they could land anywhere.
    /// </summary>
    private const double MinimumKnownShare = 0.25;

    /// <summary>How many spreads wide the good-day-to-bad-day band is. Set by the backtest.</summary>
    private const double SpreadBand = 3.5;

    public static Prediction? Predict(
        CompetitionId competition,
        PersonId person,
        string className,
        RunnerForm runner,
        IReadOnlyList<RunnerForm> field,
        int fieldSize)
    {
        var opponents = field.Where(f => !f.Identity.Matches(runner.Identity)).ToList();

        if (fieldSize < 2)
            return null;

        int known = opponents.Count;
        double knownShare = (double)known / Math.Max(fieldSize - 1, 1);

        // On a good day the runner's ratio is at the bottom of their spread; on a bad day at the
        // top. Each end is simply how many opponents are expected to be ahead of them there.
        // The band is two spreads wide: at one, the backtest put the real placing inside the
        // interval less than half the time, which is a forecast that is wrong more often than
        // it is right.
        double band = runner.Spread * SpreadBand;

        int best = opponents.Count(o => o.Ratio < runner.Ratio - band) + 1;
        int worst = opponents.Count(o => o.Ratio < runner.Ratio + band) + 1;

        // The runners nobody has history for still take places. Rather than padding the
        // interval by their number — which swamps it early in a season, when almost nobody has
        // three races yet — the counted opponents are scaled up to the whole field, on the
        // assumption that the unknown runners are spread like the known ones.
        double scale = (double)fieldSize / (known + 1);

        int low = Math.Clamp((int)Math.Floor(best * scale), 1, fieldSize);
        int high = Math.Clamp((int)Math.Ceiling(worst * scale), low, fieldSize);

        // A single place is more than the model can know; the narrowest honest answer is two.
        if (high == low)
            high = Math.Min(low + 1, fieldSize);

        return new Prediction
        {
            Competition = competition,
            Person = person,
            Class = className,
            LowPlace = low,
            HighPlace = high,
            FieldSize = fieldSize,
            Confidence = ConfidenceOf(runner, knownShare),
            Drivers = DriversOf(runner, known, Math.Max(fieldSize - 1 - known, 0)),
            ModelVersion = Version,
        };
    }

    /// <summary>
    /// How much evidence the interval rests on — the runner's own history and how much of the
    /// field is known. Deliberately not the width.
    /// </summary>
    /// <remarks>
    /// An earlier version rewarded a narrow interval, and the backtest showed the confident
    /// predictions hitting <em>less</em> often than the hedged ones: narrowness is the model
    /// committing, not the model knowing. Confidence now says how much is known, which is the
    /// only thing the number can honestly mean.
    /// </remarks>
    private static double ConfidenceOf(RunnerForm runner, double knownShare)
    {
        double history = Math.Min(runner.Races / 8.0, 1.0);
        double consistency = 1.0 - Math.Min(runner.Spread / 0.2, 1.0);

        return Math.Round(
            Math.Clamp((history * 0.4) + (Math.Max(knownShare, MinimumKnownShare) * 0.4) + (consistency * 0.2), 0.05, 0.95),
            2);
    }

    /// <summary>
    /// Why the interval looks the way it does, in the words the sheet shows. A number without a
    /// reason is the thing the product principles forbid.
    /// </summary>
    private static IReadOnlyList<string> DriversOf(RunnerForm runner, int known, int unknown)
    {
        var drivers = new List<string>(3)
        {
            $"{runner.Races} lopp i din historik",
            runner.Spread <= 0.05
                ? "jämna lopp — smalt intervall"
                : "ojämna lopp — bredare intervall",
        };

        drivers.Add(unknown == 0
            ? $"hela fältet har känd form ({known} löpare)"
            : $"{known} av {known + unknown} i fältet har känd form");

        return drivers;
    }

    /// <summary>The ratio a result represents: the runner's time as a share of the winner's.</summary>
    public static double RatioOf(TimeSpan time, TimeSpan winner) =>
        winner <= TimeSpan.Zero ? 1.0 : Math.Round(time / winner, 3);

    public static string Describe(Prediction prediction) =>
        string.Create(CultureInfo.InvariantCulture, $"{prediction.Range} av {prediction.FieldSize}");
}
