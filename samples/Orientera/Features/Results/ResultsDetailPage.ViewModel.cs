using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Analysis;
using Orientera.Services.Sources;

namespace Orientera.Features.Results;

public enum ResultsTab
{
    Overview,
    Legs,
    Analysis,
}

/// <summary>One row in the splits table. Loss is observed; the mistake flag is modelled.</summary>
public sealed record LegRow
{
    public required string Control { get; init; }
    public required string LegTime { get; init; }
    public required string LegPlace { get; init; }
    public required string PositionAfter { get; init; }
    public required string Loss { get; init; }

    /// <summary>Drives the colour: green when level with the best, red as the loss grows.</summary>
    public required double LossShare { get; init; }

    public required bool IsLikelyMistake { get; init; }
    public required string MistakeText { get; init; }

    /// <summary>The leg as one spoken line — five separate cells per leg is unusable.</summary>
    public required string Accessibility { get; init; }
}

public sealed record ComparisonRow
{
    public required string Label { get; init; }
    public required string Mine { get; init; }
    public required string Theirs { get; init; }
    public required string Delta { get; init; }
    public required bool IsBehind { get; init; }
    public required string Accessibility { get; init; }
}

public partial class ResultsDetailPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    ComparisonRequest _comparison) : ViewModelBase, IReceivesNavigationParameter<CompetitionId>
{
    private CompetitionId _id;
    private Person? _me;
    private CompetitionResult? _mine;
    private IReadOnlyList<CompetitionResult> _field = [];
    private IReadOnlyList<LegAnalysis> _legs = [];

    [ObservableProperty] public partial ResultsTab Tab { get; set; } = ResultsTab.Overview;
    [ObservableProperty] public partial bool IsOverview { get; set; } = true;
    [ObservableProperty] public partial bool IsLegs { get; set; }
    [ObservableProperty] public partial bool IsAnalysis { get; set; }

    // ---- overview ----
    [ObservableProperty] public partial string CompetitionName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ClassLine { get; set; } = string.Empty;
    [ObservableProperty] public partial string PlaceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PlaceOfText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TimeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BehindText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PredictionText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PredictionOutcomeText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BeatPrediction { get; set; }
    [ObservableProperty] public partial bool HasPrediction { get; set; }

    // Spoken equivalents: "4:e" and "38:33" are read as "4 e" and a clock time.
    [ObservableProperty] public partial string PlaceSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string TimeSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string BehindSpoken { get; set; } = string.Empty;

    // ---- analysis ----
    [ObservableProperty] public partial string TotalMistakeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TotalMistakeSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string TheoreticalTimeText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TheoreticalTimeSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string StabilityText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BiggestLossControl { get; set; } = string.Empty;
    [ObservableProperty] public partial string BiggestLossText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasMistakes { get; set; }
    [ObservableProperty] public partial string CompareTargetText { get; set; } = string.Empty;

    // ---- states ----
    [ObservableProperty] public partial bool HasResult { get; set; }
    [ObservableProperty] public partial bool HasSplits { get; set; }
    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

    public ObservableCollection<LegRow> Legs { get; } = [];
    public ObservableCollection<ComparisonRow> Comparison { get; } = [];

    private Prediction? _prediction;

    public Task OnNavigationParameterAsync(CompetitionId param)
    {
        _id = param;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (navigationDirection == NavigationDirection.Back)
            return;

        var competition = await _events.GetCompetitionAsync(_id);
        _me = await _people.GetMeAsync();

        if (competition is null || _me is null)
            return;

        CompetitionName = competition.Name;
        Title = competition.Name;

        _field = await _participation.GetResultsAsync(_id);
        _mine = _field.FirstOrDefault(r => r.Person == _me.Id);
        _prediction = await _participation.GetPredictionAsync(_id, _me.Id);

        if (_mine is null)
        {
            HasResult = false;
            EmptyMessage = "Resultatet är inte publicerat ännu. Flytta klockan framåt i tidsmaskinen för att se det.";
            return;
        }

        HasResult = true;
        BuildOverview(competition, _mine);

        _legs = SplitAnalyzer.Analyse(_mine, _field);
        HasSplits = _legs.Count > 0;

        if (HasSplits)
        {
            BuildLegs();
            BuildAnalysis(_mine);
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        Tab = Enum.Parse<ResultsTab>(tab);
        IsOverview = Tab == ResultsTab.Overview;
        IsLegs = Tab == ResultsTab.Legs;
        IsAnalysis = Tab == ResultsTab.Analysis;
    }

    [RelayCommand]
    private async Task OpenPrediction()
    {
        if (_prediction is not null)
            await _navigation.NavigateToAsync<PredictionInfoSheet, Prediction>(_prediction);
    }

    [RelayCommand]
    private async Task Compare()
    {
        if (_mine is null)
            return;

        // Spine's NavigateToWithResultAsync cannot carry a parameter, so the request is handed
        // over through a small shared state object instead. Tracked as Spine issue #18.
        _comparison.Set(_id, _mine.Class, _mine.Person);

        var result = await _navigation.NavigateToWithResultAsync<CompareRunnerSheet, PersonId>();

        if (result is { IsSuccess: true, Value: { } target })
            BuildComparison(target);
    }

    private void BuildOverview(Competition competition, CompetitionResult mine)
    {
        ClassLine = $"{mine.Class} · {Format.Discipline(competition.Discipline)} · {competition.Date:d MMM yyyy}";
        PlaceText = Format.Place(mine.Place);
        PlaceOfText = Format.PlaceOf(mine.Place, mine.Starters);
        TimeText = Format.Time(mine.Time);
        BehindText = mine.BehindWinner is { } behind ? Format.Delta(behind) : "—";
        StatusText = Format.ResultStatus(mine.Status);

        PlaceSpoken = $"{Format.SpokenPlace(mine.Place)} av {mine.Starters} startande";
        TimeSpoken = $"tid {Format.SpokenTime(mine.Time)}";
        BehindSpoken = mine.BehindWinner is { } spokenBehind
            ? $"efter vinnaren {Format.SpokenTime(spokenBehind)}"
            : "ingen tid efter vinnaren";

        HasPrediction = _prediction is not null && mine.Place is not null;

        if (_prediction is { } prediction && mine.Place is { } place)
        {
            PredictionText = $"Prognos {prediction.Range}";
            BeatPrediction = place <= prediction.LowPlace;

            PredictionOutcomeText = place < prediction.LowPlace
                ? $"{prediction.LowPlace - place} bättre än prognos"
                : place > prediction.HighPlace
                    ? $"{place - prediction.HighPlace} sämre än prognos"
                    : "Inom prognosen";
        }
    }

    private void BuildLegs()
    {
        Legs.Clear();

        var worstLoss = _legs.Max(l => l.LossToBest);

        foreach (var leg in _legs)
        {
            Legs.Add(new LegRow
            {
                Control = $"{leg.ControlNumber} ({leg.ControlCode})",
                LegTime = Format.Time(leg.LegTime),
                LegPlace = leg.LegPlace.ToString(),
                PositionAfter = leg.PositionAfter.ToString(),
                Loss = leg.LossToBest > TimeSpan.Zero ? Format.Delta(leg.LossToBest) : "—",
                LossShare = worstLoss > TimeSpan.Zero ? leg.LossToBest / worstLoss : 0,
                IsLikelyMistake = leg.IsLikelyMistake,
                MistakeText = leg.IsLikelyMistake
                    ? $"trolig bom, ca {Format.Time(leg.EstimatedMistakeTime)}"
                    : string.Empty,
                Accessibility = string.Join(", ",
                    new[]
                    {
                        $"kontroll {leg.ControlNumber}",
                        Format.SpokenTime(leg.LegTime),
                        $"sträckplacering {leg.LegPlace}",
                        $"totalt {Format.SpokenPlace(leg.PositionAfter)}",
                        leg.LossToBest > TimeSpan.Zero ? $"{Format.SpokenTime(leg.LossToBest)} efter bästa" : "bästa sträcktid",
                        leg.IsLikelyMistake ? $"trolig bom, uppskattat {Format.SpokenTime(leg.EstimatedMistakeTime)}" : string.Empty,
                    }.Where(part => part.Length > 0)),
            });
        }
    }

    private void BuildAnalysis(CompetitionResult mine)
    {
        var mistakes = _legs.Where(l => l.IsLikelyMistake).ToList();
        HasMistakes = mistakes.Count > 0;

        var total = SplitAnalyzer.TotalMistakeTime(_legs);
        TotalMistakeText = Format.Time(total);
        TotalMistakeSpoken = $"uppskattad bomtid {Format.SpokenTime(total)}";

        var theoretical = SplitAnalyzer.TheoreticalTime(mine.Time ?? TimeSpan.Zero, _legs);
        TheoreticalTimeText = Format.Time(theoretical);
        TheoreticalTimeSpoken = $"uppskattad tid utan bommar {Format.SpokenTime(theoretical)}";

        StabilityText = SplitAnalyzer.StabilityIndex(_legs).ToString("0.00");

        var biggest = _legs.MaxBy(l => l.EstimatedMistakeTime);

        if (biggest is not null && biggest.EstimatedMistakeTime > TimeSpan.Zero)
        {
            BiggestLossControl = $"Kontroll {biggest.ControlNumber}";
            BiggestLossText = $"{Format.Time(biggest.EstimatedMistakeTime)} över din egen fart";
        }

        CompareTargetText = "Jämför med vinnaren";
    }

    private void BuildComparison(PersonId target)
    {
        if (_mine is null)
            return;

        var other = _field.FirstOrDefault(r => r.Person == target);

        if (other is null || other.Splits.Count == 0)
            return;

        CompareTargetText = $"Jämför med {other.Name}";
        Comparison.Clear();

        int count = Math.Min(_mine.Splits.Count, other.Splits.Count);

        for (int i = 0; i < count; i++)
        {
            var mineLeg = _mine.Splits[i];
            var theirLeg = other.Splits[i];
            var delta = mineLeg.LegTime - theirLeg.LegTime;

            Comparison.Add(new ComparisonRow
            {
                Label = $"{mineLeg.ControlNumber} ({mineLeg.ControlCode})",
                Mine = Format.Time(mineLeg.LegTime),
                Theirs = Format.Time(theirLeg.LegTime),
                Delta = Format.Delta(delta),
                IsBehind = delta > TimeSpan.Zero,
                Accessibility = $"kontroll {mineLeg.ControlNumber}, du {Format.SpokenTime(mineLeg.LegTime)}, "
                              + $"{other.Name} {Format.SpokenTime(theirLeg.LegTime)}, {Format.SpokenDelta(delta)}",
            });
        }
    }
}
