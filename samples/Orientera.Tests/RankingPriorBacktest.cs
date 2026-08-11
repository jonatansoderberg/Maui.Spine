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
    /// The verdict, written as a test so it cannot quietly stop being true. The product needs an
    /// interval that holds four times in five while covering well under half the field; the best
    /// this model manages at that coverage is 52,8 % of the field. The ranking moved it and did
    /// not move it far enough.
    /// </summary>
    /// <remarks>
    /// This assertion fails the day the bar is cleared. That is the point: the verdict in
    /// issues/113-ranking-prior.md would then be out of date, and so would the decision to keep
    /// the forecast out of the app.
    /// </remarks>
    [Fact]
    public void The_bar_the_product_needs_is_still_not_met()
    {
        var best = Tightest(Use.WhenUnwatched, RequiredCoverage);

        Assert.True(
            best.Width > RequiredWidth,
            $"The bar is met: {best}. Revisit issues/113-ranking-prior.md and wire the forecast in.");
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
    }

    /// <summary>Below this many races of our own, a ranking is the better estimate.</summary>
    private const int ThinForm = 6;

    private Outcome Run(bool useRanking) =>
        Run(useRanking ? Use.WhenUnwatched : Use.None);

    private Outcome Run(Use use, double spreadScale = 1.0)
    {
        var calibration = Calibrate();
        var history = new Dictionary<string, List<double>>();
        var perCompetition = new Dictionary<int, int>();

        int hits = 0, total = 0, confidentHits = 0, confidentTotal = 0;
        double width = 0, knownShare = 0;

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
                    .Select(r => (Result: r, Form: Widen(FormOf(r, history, use, calibration, day), spreadScale)))
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
        DateOnly day)
    {
        var identity = RunnerIdentity.Of(result.Person);
        var watched = RunnerForm.From(
            identity, history.TryGetValue(result.Person, out var ratios) ? ratios : []);

        if (use == Use.None)
            return watched;

        if (watched is not null && (use == Use.WhenUnwatched || watched.Races >= ThinForm))
            return watched;

        return RankingBefore(result.Person, day) is { } points
            ? RunnerForm.Ranked(identity, points, calibration)
            : watched;
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

    private static DateOnly Date(Competition competition) =>
        DateOnly.ParseExact(competition.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Outcome(
        int Predictions,
        double Coverage,
        double RelativeWidth,
        double ConfidentCoverage,
        double KnownShare,
        IReadOnlyDictionary<int, int> PerCompetition)
    {
        public override string ToString() =>
            $"{Coverage:P1} av {RelativeWidth:P1} bredd, {Predictions} prognoser, {KnownShare:P1} känd form";
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
