using System.Globalization;
using System.Text.Json;
using Orientera.Backend.Predictions;

namespace Orientera.Tests;

/// <summary>
/// Spike SP-11c. SP-11 measured that the model was not good enough and named the ranking as the
/// first thing that would move it; SP-11b measured that the ranking really does predict placement.
/// This measures what it is worth inside the model, against races that have already been run.
/// </summary>
/// <remarks>
/// The data is Eventor's own, not LiveResults': 244 competitions from 2026 read through
/// <c>/api/results/event</c>, which carries a <c>personId</c> on every row. Sverigelistan hangs on
/// that id, so the join is exact and no name matching leaks noise into the measurement.
///
/// Every prior is rebuilt as it stood the day before the race, from the runner's own dated
/// results. The race being predicted is never inside what predicts it.
/// </remarks>
public class RankingPriorBacktest
{
    /// <summary>
    /// Predictions are only made in the last stretch, because the runner pages were only fetched
    /// for the people who ran in it. Everything before it is history for those runners.
    /// </summary>
    private static readonly DateOnly WindowStart = new(2026, 8, 1);

    /// <summary>Sverigelistan is the average of the six best results in the year behind you.</summary>
    private const int Counting = 6;

    private readonly Backtest _data = Backtest.Load();

    /// <summary>
    /// The bar SP-11 set and did not clear: the interval must hold the placing about four times
    /// in five while covering well under half the field.
    /// </summary>
    private const double RequiredCoverage = 0.80;

    private const double RequiredWidth = 0.40;

    [Fact]
    public void The_ranking_is_read_onto_the_ratio_scale()
    {
        var calibration = Calibrate();

        Assert.NotNull(calibration);

        // Sverigelistan counts downwards, a race ratio upwards: more points must mean slower.
        Assert.True(calibration.Slope > 0, $"Slope {calibration.Slope:F5} does not rise with points.");
        Assert.InRange(calibration.RatioOf(0), 0.8, 1.2);

        // The fit that is baked into the production default, so the two cannot drift apart.
        Assert.Equal(RankingCalibration.Default.Intercept, calibration.Intercept, 3);
        Assert.Equal(RankingCalibration.Default.Slope, calibration.Slope, 4);
    }

    /// <summary>What the ranking is worth: how much of a field can be given a form at all.</summary>
    [Fact]
    public void The_ranking_gives_a_form_to_runners_we_have_never_watched()
    {
        var withRanking = Run(useRanking: true);
        var without = Run(useRanking: false);

        // 86,6 % against 75,2 %, and 3 264 forecasts where there were 2 876.
        Assert.True(
            withRanking.KnownShare > without.KnownShare + 0.10,
            $"Known share {withRanking.KnownShare:P1} against {without.KnownShare:P1}.");

        Assert.True(
            withRanking.Predictions > without.Predictions * 1.10,
            $"{withRanking.Predictions} forecasts against {without.Predictions}.");
    }

    /// <summary>
    /// The whole question, in one comparison: held to the same standard of being right, does the
    /// ranking let the interval say more? Width at matched coverage is the only fair measure —
    /// any model can be made to hit more often by widening, and narrower by hitting less.
    /// </summary>
    [Fact]
    public void At_the_same_coverage_the_ranking_narrows_the_interval()
    {
        var baseline = Tightest(Use.None, RequiredCoverage);
        var fallback = Tightest(Use.WhenUnwatched, RequiredCoverage);
        var preferred = Tightest(Use.OverThinForm, RequiredCoverage);

        // 52,8 % of the field against 57,8 %.
        Assert.True(
            fallback.Width < baseline.Width,
            $"At {RequiredCoverage:P0} coverage: baseline {baseline}, fallback {fallback}.");

        // And a ranking is not a substitute for races we watched: replacing a thin form with one
        // gives back more than it takes, 58,8 % against 52,8 %. It fills gaps; it does not improve
        // on evidence.
        Assert.True(
            preferred.Width > fallback.Width,
            $"Preferring the ranking over a thin form: {preferred} against {fallback}.");
    }

    /// <summary>The narrowest interval that still holds the placing as often as required.</summary>
    private Sweep Tightest(Use use, double coverage)
    {
        Sweep? best = null;

        for (double scale = 0.2; scale <= 3.01; scale += 0.1)
        {
            var outcome = Run(use, scale);

            if (outcome.Coverage >= coverage && (best is null || outcome.RelativeWidth < best.Width))
                best = new Sweep(scale, outcome.Coverage, outcome.RelativeWidth, outcome.Predictions);
        }

        return best ?? new Sweep(0, 0, 1, 0);
    }

    private sealed record Sweep(double Scale, double Coverage, double Width, int Predictions)
    {
        public override string ToString() =>
            $"band ×{Scale:F1} → {Coverage:P1} av {Width:P1} bredd ({Predictions} prognoser)";
    }

    /// <summary>
    /// What the model achieves as it ships, pinned so a change has to be a deliberate one:
    /// 93,2 % coverage across 3 264 forecasts, at a band tuned on other data.
    /// </summary>
    [Fact]
    public void The_interval_holds_the_real_placing()
    {
        var outcome = Run(useRanking: true);

        Assert.True(outcome.Predictions >= 500, $"Too few predictions to judge: {outcome.Predictions}.");
        Assert.True(
            outcome.Coverage >= 0.90,
            $"Coverage {outcome.Coverage:P1} over {outcome.Predictions} predictions.");
    }

    /// <summary>
    /// What the chosen variant achieves, pinned so a change has to be deliberate: at the coverage
    /// the product asks for, the interval covers about half the field and its middle lands two and
    /// a half places from the truth.
    /// </summary>
    /// <remarks>
    /// The bar moved (#117). The forecast no longer has to be authoritative — it may be an
    /// approximation as long as the app says so — which is why this pins numbers instead of
    /// asserting a failure the way issues/113-ranking-prior.md did.
    /// </remarks>
    [Fact]
    public void The_blend_is_the_variant_that_ships()
    {
        var blended = Tightest(Use.Blended, RequiredCoverage);

        Assert.InRange(blended.Width, 0.45, 0.55);
        Assert.True(
            Run(Use.Blended).MedianError <= 2.5,
            $"The middle of the interval lands {Run(Use.Blended).MedianError:F1} places out.");
    }

    /// <summary>
    /// Every variant lands within a few points of every other. The ordering below is real but
    /// small, and smaller than the spread between races — so this pins the ordering rather than
    /// the gaps, and nothing downstream should lean on the gaps being large.
    /// </summary>
    [Fact]
    public void The_variants_are_close_and_the_blend_is_the_best_of_them()
    {
        var blended = Tightest(Use.Blended, RequiredCoverage);
        var fallback = Tightest(Use.WhenUnwatched, RequiredCoverage);
        var rankingFirst = Tightest(Use.RankingFirst, RequiredCoverage);

        // Sorting the field on Sverigelistan alone gives the narrowest interval of all — 50,9 %
        // against 51,4 % — and pays for it by landing a whole place further from the truth.
        Assert.True(
            rankingFirst.Width < fallback.Width,
            $"Ranking first {rankingFirst} against fallback {fallback}.");

        Assert.True(
            Run(Use.RankingFirst).MedianError > Run(Use.Blended).MedianError,
            $"Ranking first is off by {Run(Use.RankingFirst).MedianError:F1}, the blend by "
            + $"{Run(Use.Blended).MedianError:F1}.");

        Assert.True(
            blended.Width < fallback.Width,
            $"Blend {blended} against fallback {fallback}.");
    }

    [Fact]
    public void No_prediction_uses_its_own_race()
    {
        var early = _data.Competitions.First(c => Date(c) < WindowStart);

        Assert.DoesNotContain(early.Id, Run(useRanking: true).PerCompetition.Keys);
    }

    // ---------------------------------------------------------------- the run

    private RankingCalibration Calibrate()
    {
        var history = HistoryBefore(WindowStart);
        var observations = new List<(double, double)>();

        foreach (var (person, ratios) in history)
        {
            if (ratios.Count < 3 || RankingBefore(person, WindowStart) is not { } points)
                continue;

            var sorted = ratios.Order().ToList();
            observations.Add((points, sorted[sorted.Count / 2]));
        }

        return RankingCalibration.Fit(observations)!;
    }

    /// <summary>How the ranking is allowed to stand in for races we watched.</summary>
    private enum Use
    {
        /// <summary>Not at all — the model as SP-11 left it.</summary>
        None,

        /// <summary>Only for runners we have too few races for to say anything.</summary>
        WhenUnwatched,

        /// <summary>
        /// Also instead of a thin form. Three races is enough to be counted but not enough to be
        /// trusted; six ranking results across a year may say more than three of our own.
        /// </summary>
        OverThinForm,

        /// <summary>
        /// Sort the field on Sverigelistan and let our own races matter only for the people the
        /// list has nothing to say about. The simplest thing that could work.
        /// </summary>
        RankingFirst,

        /// <summary>
        /// Both, mixed: the ranking weighs <c>k / (k + races)</c> and our own races the rest, so a
        /// runner we have watched once is mostly their ranking and one we have watched twenty
        /// times is mostly themselves.
        /// </summary>
        Blended,
    }

    /// <summary>Below this many races of our own, a ranking is the better estimate.</summary>
    private const int ThinForm = 6;

    /// <summary>How many races of our own it takes to outweigh the ranking. Swept; see #117.</summary>
    private const double BlendPivot = 2.0;

    private Outcome Run(bool useRanking) =>
        Run(useRanking ? Use.WhenUnwatched : Use.None);

    private Outcome Run(Use use, double spreadScale = 1.0, double pivot = BlendPivot)
    {
        var calibration = Calibrate();
        var history = new Dictionary<string, List<double>>();
        var perCompetition = new Dictionary<int, int>();

        int hits = 0, total = 0, confidentHits = 0, confidentTotal = 0;
        double width = 0, knownShare = 0;

        // How far the middle of the interval lands from the real place. For a forecast that is
        // allowed to be approximate, this is the number that says whether it is any good.
        var errors = new List<double>();

        foreach (var competition in _data.Competitions.OrderBy(c => c.Date, StringComparer.Ordinal))
        {
            var day = Date(competition);
            var races = _data.Results.Where(r => r.Competition == competition.Id).GroupBy(r => r.Class);
            var afterwards = new List<Result>();

            foreach (var race in races)
            {
                var starters = race.ToList();
                afterwards.AddRange(starters);

                if (day < WindowStart)
                    continue;

                var forms = starters
                    .Select(r => (Result: r, Form: Widen(FormOf(r, history, use, calibration, day, pivot), spreadScale)))
                    .ToList();

                var known = forms.Where(f => f.Form is not null).Select(f => f.Form!).ToList();
                knownShare += (double)known.Count / starters.Count;

                foreach (var (result, form) in forms)
                {
                    if (form is null)
                        continue;

                    var prediction = PredictionModel.Predict(
                        new CompetitionId(competition.Id.ToString(CultureInfo.InvariantCulture)),
                        new PersonId(result.Person),
                        race.Key,
                        form,
                        known,
                        starters.Count);

                    if (prediction is null)
                        continue;

                    total++;
                    perCompetition[competition.Id] = perCompetition.GetValueOrDefault(competition.Id) + 1;

                    bool hit = result.Place >= prediction.LowPlace && result.Place <= prediction.HighPlace;

                    if (hit)
                        hits++;

                    width += (double)(prediction.HighPlace - prediction.LowPlace + 1) / prediction.FieldSize;
                    errors.Add(Math.Abs(((prediction.LowPlace + prediction.HighPlace) / 2.0) - result.Place));

                    if (prediction.Confidence >= 0.6)
                    {
                        confidentTotal++;

                        if (hit)
                            confidentHits++;
                    }
                }
            }

            // Only once the competition is over does it become history.
            foreach (var result in afterwards)
            {
                if (!history.TryGetValue(result.Person, out var ratios))
                    history[result.Person] = ratios = [];

                ratios.Add(result.Ratio);
            }
        }

        int fields = perCompetition.Count == 0 ? 1 : _data.Results
            .Where(r => Date(_data.Competition(r.Competition)) >= WindowStart)
            .Select(r => (r.Competition, r.Class))
            .Distinct()
            .Count();

        return new Outcome(
            total,
            total == 0 ? 0 : (double)hits / total,
            total == 0 ? 0 : width / total,
            confidentTotal == 0 ? 0 : (double)confidentHits / confidentTotal,
            knownShare / fields,
            errors.Count == 0 ? 0 : Median(errors),
            perCompetition);
    }

    /// <summary>
    /// Races we watched first, and the ranking only when there are too few of them. A ranking is
    /// six results read through a fitted line; three races of our own say more.
    /// </summary>
    private RunnerForm? FormOf(
        Result result,
        Dictionary<string, List<double>> history,
        Use use,
        RankingCalibration calibration,
        DateOnly day,
        double pivot)
    {
        var identity = RunnerIdentity.Of(result.Person);
        var watched = RunnerForm.From(
            identity, history.TryGetValue(result.Person, out var ratios) ? ratios : []);

        if (use == Use.None)
            return watched;

        if (use == Use.WhenUnwatched && watched is not null)
            return watched;

        if (use == Use.OverThinForm && watched is { Races: >= ThinForm })
            return watched;

        if (RankingBefore(result.Person, day) is not { } points)
            return watched;

        var ranked = RunnerForm.Ranked(identity, points, calibration);

        return use == Use.Blended && watched is not null
            ? RunnerForm.Blend(watched, ranked, pivot / (pivot + watched.Races))
            : ranked;
    }

    /// <summary>
    /// The interval is the spread times a fixed band, so scaling the spread is how the band is
    /// swept. Done here rather than by opening the model's constant: the constant is a decision
    /// the model owns, and the sweep is how it gets decided.
    /// </summary>
    private static RunnerForm? Widen(RunnerForm? form, double scale) =>
        form is null || scale == 1.0 ? form : form with { Spread = form.Spread * scale };

    /// <summary>The average of the six best results in the twelve months before <paramref name="day"/>.</summary>
    private double? RankingBefore(string person, DateOnly day)
    {
        if (!_data.Rankings.TryGetValue(person, out var results))
            return null;

        var window = results
            .Where(r => r.Date < day && r.Date >= day.AddYears(-1))
            .Select(r => r.Points)
            .Order()
            .Take(Counting)
            .ToList();

        return window.Count < Counting ? null : window.Average();
    }

    private Dictionary<string, List<double>> HistoryBefore(DateOnly day)
    {
        var history = new Dictionary<string, List<double>>();

        foreach (var result in _data.Results.Where(r => Date(_data.Competition(r.Competition)) < day))
        {
            if (!history.TryGetValue(result.Person, out var ratios))
                history[result.Person] = ratios = [];

            ratios.Add(result.Ratio);
        }

        return history;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();

        return sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static DateOnly Date(Competition competition) =>
        DateOnly.ParseExact(competition.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Outcome(
        int Predictions,
        double Coverage,
        double RelativeWidth,
        double ConfidentCoverage,
        double KnownShare,
        double MedianError,
        IReadOnlyDictionary<int, int> PerCompetition)
    {
        public override string ToString() =>
            $"{Coverage:P1} av {RelativeWidth:P1} bredd, medianfel {MedianError:F1} platser, "
            + $"{Predictions} prognoser, {KnownShare:P1} känd form";
    }

    private sealed record Competition(int Id, string Date, string Name);

    private sealed record Result(string Person, int Competition, string Class, int Place, int Starters, double Ratio);

    private sealed record RankingResult(DateOnly Date, double Points);

    private sealed record Backtest(
        IReadOnlyList<Competition> Competitions,
        IReadOnlyList<Result> Results,
        IReadOnlyDictionary<string, IReadOnlyList<RankingResult>> Rankings)
    {
        private Dictionary<int, Competition>? _byId;

        public Competition Competition(int id) =>
            (_byId ??= Competitions.ToDictionary(c => c.Id))[id];

        public static Backtest Load()
        {
            using var stream = File.OpenRead(Fixture.PathFor("Backtest", "eventor-2026.json"));
            var document = JsonDocument.Parse(stream);

            var competitions = document.RootElement.GetProperty("competitions").EnumerateArray()
                .Select(c => new Competition(
                    c.GetProperty("id").GetInt32(),
                    c.GetProperty("date").GetString()!,
                    c.GetProperty("name").GetString()!))
                .ToList();

            var results = document.RootElement.GetProperty("results").EnumerateArray()
                .Select(r => new Result(
                    r[0].GetString()!,
                    r[1].GetInt32(),
                    r[2].GetString()!,
                    r[3].GetInt32(),
                    r[4].GetInt32(),
                    r[5].GetDouble()))
                .ToList();

            var rankings = document.RootElement.GetProperty("rankings").EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => (IReadOnlyList<RankingResult>)[.. p.Value.EnumerateArray().Select(r =>
                        new RankingResult(
                            DateOnly.ParseExact(r[0].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                            r[1].GetDouble()))]);

            return new Backtest(competitions, results, rankings);
        }
    }
}
