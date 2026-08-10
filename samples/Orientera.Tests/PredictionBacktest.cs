using System.Text.Json;
using Orientera.Backend.Predictions;

namespace Orientera.Tests;

/// <summary>
/// Spike SP-11. A forecast that is never wrong because it always says "somewhere in the field"
/// is worthless, and a narrow one that is often wrong is worse than none. The only way to know
/// which this model is, is to run it against races that have already happened.
/// </summary>
/// <remarks>
/// 60 Swedish competitions from 2026, recorded from LiveResults — see Fixtures/Backtest.
/// Every prediction is made from results that were available <em>before</em> that competition,
/// which is what keeps the measurement honest.
/// </remarks>
public class PredictionBacktest
{
    private readonly Backtest _data = Backtest.Load();

    /// <summary>
    /// What the model actually achieves on this data, pinned so a change has to be a
    /// deliberate one. It is <em>not</em> the bar the product needs — see the verdict in
    /// issues/40-prediction.md: the interval would have to hold the placing about four times
    /// in five while covering well under half the field, and this model does neither.
    /// </summary>
    private const double MinimumCoverage = 0.70;

    private const double MaximumWidth = 0.60;

    [Fact]
    public void The_interval_holds_the_real_placing_often_enough()
    {
        var outcome = Run();

        Assert.True(outcome.Predictions >= 500, $"Too few predictions to judge: {outcome.Predictions}.");
        Assert.True(
            outcome.Coverage >= MinimumCoverage,
            $"Coverage {outcome.Coverage:P1} over {outcome.Predictions} predictions, below {MinimumCoverage:P0}.");
    }

    [Fact]
    public void And_it_commits_to_something()
    {
        var outcome = Run();

        Assert.True(
            outcome.RelativeWidth <= MaximumWidth,
            $"Mean interval covers {outcome.RelativeWidth:P1} of the field, above {MaximumWidth:P0}.");
    }

    /// <summary>
    /// Confidence has to mean something: the intervals the model is sure about must be right
    /// more often than the ones it hedges.
    /// </summary>
    [Fact]
    public void Confidence_tracks_being_right()
    {
        var outcome = Run();

        Assert.True(
            outcome.ConfidentCoverage >= outcome.Coverage,
            $"Confident predictions hit {outcome.ConfidentCoverage:P1}, all of them {outcome.Coverage:P1}.");
    }

    /// <summary>A prediction is never made from the race it is predicting.</summary>
    [Fact]
    public void No_prediction_uses_its_own_race()
    {
        var first = _data.Competitions[0];

        var predictions = Run().PerCompetition;

        Assert.False(predictions.ContainsKey(first.Id));
    }

    private Outcome Run()
    {
        var history = new Dictionary<string, List<double>>();
        var perCompetition = new Dictionary<int, int>();

        int hits = 0, total = 0, confidentHits = 0, confidentTotal = 0;
        double width = 0;

        foreach (var competition in _data.Competitions.OrderBy(c => c.Date))
        {
            var races = _data.Results
                .Where(r => r.Competition == competition.Id)
                .GroupBy(r => r.Class);

            var afterwards = new List<Result>();

            foreach (var race in races)
            {
                var starters = race.ToList();

                var forms = starters
                    .Select(r => (Result: r, Form: RunnerForm.From(
                        RunnerIdentity.Of(r.Name, r.Club),
                        history.TryGetValue(Key(r), out var ratios) ? ratios : [])))
                    .ToList();

                var known = forms.Where(f => f.Form is not null).Select(f => f.Form!).ToList();

                foreach (var (result, form) in forms)
                {
                    if (form is null)
                        continue;

                    var prediction = PredictionModel.Predict(
                        new CompetitionId(competition.Id.ToString()),
                        new PersonId(form.Identity.Key),
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

                afterwards.AddRange(starters);
            }

            // Only once the competition is over does it become history.
            foreach (var result in afterwards)
            {
                if (!history.TryGetValue(Key(result), out var ratios))
                    history[Key(result)] = ratios = [];

                ratios.Add(result.Ratio);
            }
        }

        return new Outcome(
            total,
            total == 0 ? 0 : (double)hits / total,
            total == 0 ? 0 : width / total,
            confidentTotal == 0 ? 0 : (double)confidentHits / confidentTotal,
            perCompetition);
    }

    private static string Key(Result result) => RunnerIdentity.Of(result.Name, result.Club).Key;

    private sealed record Outcome(
        int Predictions,
        double Coverage,
        double RelativeWidth,
        double ConfidentCoverage,
        IReadOnlyDictionary<int, int> PerCompetition);

    private sealed record Competition(int Id, string Date, string Name, string Organizer);

    private sealed record Result(string Name, string Club, int Competition, string Class, int Place, int Starters, double Ratio);

    private sealed record Backtest(IReadOnlyList<Competition> Competitions, IReadOnlyList<Result> Results)
    {
        public static Backtest Load()
        {
            using var stream = File.OpenRead(Fixture.PathFor("Backtest", "swedish-2026.json"));
            var document = JsonDocument.Parse(stream);

            var competitions = document.RootElement.GetProperty("competitions").EnumerateArray()
                .Select(c => new Competition(
                    c.GetProperty("id").GetInt32(),
                    c.GetProperty("date").GetString()!,
                    c.GetProperty("name").GetString()!,
                    c.GetProperty("organizer").GetString()!))
                .ToList();

            var results = document.RootElement.GetProperty("results").EnumerateArray()
                .Select(r => new Result(
                    r[0].GetString()!,
                    r[1].GetString()!,
                    r[2].GetInt32(),
                    r[3].GetString()!,
                    r[4].GetInt32(),
                    r[5].GetInt32(),
                    r[6].GetDouble()))
                .ToList();

            return new Backtest(competitions, results);
        }
    }
}
