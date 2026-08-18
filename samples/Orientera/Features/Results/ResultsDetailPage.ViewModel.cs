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

/// <summary>One runner in the competition's own result list.</summary>
public sealed record ResultRow
{
    public required string PlaceText { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public string? ClubLogo { get; init; }
    public bool HasClubLogo => !string.IsNullOrEmpty(ClubLogo);
    public required string TimeText { get; init; }
    public required string BehindText { get; init; }

    /// <summary>The user's own row, marked the same way the live list marks it.</summary>
    public required bool IsMe { get; init; }

    public required string Accessibility { get; init; }
}

/// <summary>One class' result list, with the class as the heading.</summary>
public sealed class ResultClassGroup(string _name) : List<ResultRow>
{
    public string Name => _name;

    public string Accessibility => $"Klass {_name}";
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
    IRaceStorySource _stories) : OrienteraViewModel, IReceivesNavigationParameter<CompetitionId>
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

    /// <summary>"Efter vinnaren", or "Före tvåan" for the runner who won.</summary>
    [ObservableProperty] public partial string BehindLabel { get; set; } = "Efter vinnaren";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNeutralGap))]
    public partial bool IsWinner { get; set; }

    /// <summary>
    /// A gap to the winner big enough to be worth marking — a tenth of the winner's time. The
    /// same line the results list draws, so the two pages cannot disagree about the same race.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNeutralGap))]
    public partial bool HasMaterialGap { get; set; }

    /// <summary>Behind, but not by enough to say anything the placing does not already say.</summary>
    public bool HasNeutralGap => !IsWinner && !HasMaterialGap;
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

    /// <summary>
    /// The two names over the two time columns. Without them the table showed three unlabelled
    /// time columns and the reader had to work out which one was theirs.
    /// </summary>
    [ObservableProperty] public partial string MineHeading { get; set; } = "Du";

    [ObservableProperty] public partial string TheirsHeading { get; set; } = string.Empty;

    // ---- race story ----

    /// <summary>The race written back as a few sentences, or empty when nobody wrote it.</summary>
    [ObservableProperty] public partial string StoryText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasStory { get; set; }
    [ObservableProperty] public partial bool IsWritingStory { get; set; }

    // ---- states ----

    /// <summary>The competition has a published result list — the page has something to show.</summary>
    [ObservableProperty] public partial bool HasResult { get; set; }

    /// <summary>
    /// Nothing came back, and nothing is on its way.
    /// </summary>
    /// <remarks>
    /// Set when a fetch finishes rather than derived from <see cref="HasResult"/>, which is false
    /// for the whole four seconds a fetch takes. The page said "Inget resultat ännu" during the
    /// wait, with the spinner drawn on top of that sentence — two answers at once, and the true
    /// one arrived last.
    /// </remarks>
    [ObservableProperty] public partial bool IsIdle { get; set; }

    /// <summary>The user is in that list. Without it there is a field but no analysis.</summary>
    [ObservableProperty] public partial bool HasMine { get; set; }

    [ObservableProperty] public partial bool HasSplits { get; set; }
    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

    /// <summary>Said once, above the list, when the user is not in the result.</summary>
    [ObservableProperty] public partial string NotInFieldText { get; set; } = string.Empty;

    public ObservableCollection<LegRow> Legs { get; } = [];
    public ObservableCollection<ComparisonRow> Comparison { get; } = [];

    /// <summary>The whole field, class by class, with the user's own class first.</summary>
    public ObservableCollection<ResultClassGroup> Field { get; } = [];

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

        IsIdle = false;

        if (!await LoadAsync(BuildAsync))
        {
            HasResult = false;
            EmptyMessage = "Ingen anslutning. Resultat och sträcktider behöver nätverk.";
        }

        IsIdle = !HasResult;
    }

    private async Task BuildAsync()
    {
        var competition = await _events.GetCompetitionAsync(_id);
        _me = await _people.GetMeAsync();

        if (competition is null || _me is null)
            return;

        CompetitionName = competition.Name;
        Title = competition.Name;

        _field = await _participation.GetResultsAsync(_id);

        // The result list carries names and clubs; the person ids in it are Eventor's, and the
        // user's identity is local. Name and club is the only comparison that spans both, the
        // same one the live list is matched through (SP-04).
        var me = RunnerIdentity.Of(_me.Name, _me.Club);
        _mine = _field.FirstOrDefault(r => me.Matches(RunnerIdentity.Of(r.Name, r.Club)));
        _prediction = await _participation.GetPredictionAsync(_id, _me.Id);

        HasResult = _field.Count > 0;
        HasMine = _mine is not null;

        // A reloaded page is a different race until proven otherwise.
        StoryText = string.Empty;
        HasStory = false;

        if (!HasResult)
        {
            EmptyMessage = "Resultatet är inte publicerat ännu.";
            return;
        }

        BuildField(me);

        // The field is worth showing on its own: opening a competition you did not run is the
        // normal case, not an error.
        NotInFieldText = HasMine
            ? string.Empty
            : $"Du är inte med i den här resultatlistan. {_field.Count} resultat totalt.";

        if (_mine is null)
            return;

        BuildOverview(competition, _mine);

        _legs = SplitAnalyzer.Analyse(_mine, _field);
        HasSplits = _legs.Count > 0;

        if (HasSplits)
        {
            BuildLegs();
            BuildAnalysis(_mine);
        }
    }

    /// <summary>
    /// The field class by class. The user's own class comes first — it is the one they opened the
    /// page for, and a championship has forty of them.
    /// </summary>
    private void BuildField(RunnerIdentity me)
    {
        Field.Clear();

        string mine = _mine?.Class ?? _me?.DefaultClass ?? string.Empty;

        var classes = _field
            .GroupBy(r => r.Class)
            .OrderByDescending(g => g.Key == mine)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var byClass in classes)
        {
            var group = new ResultClassGroup(byClass.Key);

            foreach (var result in byClass.OrderBy(r => r.Place ?? int.MaxValue).ThenBy(r => r.Time))
            {
                bool isMe = me.Matches(RunnerIdentity.Of(result.Name, result.Club));

                group.Add(new ResultRow
                {
                    PlaceText = result.Place is { } place ? Format.Place(place) : "—",
                    Name = result.Name,
                    Club = result.Club,
                    ClubLogo = result.ClubLogo,
                    TimeText = result.Status == ResultStatus.Ok ? Format.Time(result.Time) : Format.ResultStatus(result.Status),
                    // The winner's own row says nothing by saying "+0:00".
                    BehindText = result.Place == 1 || result.BehindWinner is not { } behind
                        ? string.Empty
                        : Format.Delta(behind),
                    IsMe = isMe,
                    Accessibility = string.Join(", ", new[]
                    {
                        isMe ? "du" : null,
                        result.Name,
                        $"{result.Club}, klass {result.Class}",
                        result.Place is not null ? Format.SpokenPlace(result.Place) : Format.ResultStatus(result.Status),
                        result.Status == ResultStatus.Ok ? Format.SpokenTime(result.Time) : null,
                    }.OfType<string>()),
                });
            }

            Field.Add(group);
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        Tab = Enum.Parse<ResultsTab>(tab);
        IsOverview = Tab == ResultsTab.Overview;
        IsLegs = Tab == ResultsTab.Legs;
        IsAnalysis = Tab == ResultsTab.Analysis;

        if (IsAnalysis)
            _ = WriteStoryAsync();
    }

    /// <summary>
    /// Written when Analys is opened, not when the page loads: it is the one thing on the page
    /// that costs someone something per read, so a runner who only wanted their time never pays
    /// for it. Deliberately not awaited — the rest of the tab is already computed and readable
    /// while the paragraph is being written.
    /// </summary>
    private async Task WriteStoryAsync()
    {
        if (_mine is null || _legs.Count == 0 || HasStory || IsWritingStory)
            return;

        IsWritingStory = true;

        try
        {
            var facts = RaceStoryFacts.From(_mine, _legs);
            var story = await _stories.WriteAsync(new RaceStoryRequest { Class = facts.Class, Lines = facts.Lines });

            StoryText = story?.Text ?? string.Empty;
            HasStory = StoryText.Length > 0;
        }
        finally
        {
            IsWritingStory = false;
        }
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

        var request = new ComparisonRequest(_id, _mine.Class, _mine.Person);

        var result = await _navigation
            .NavigateToWithResultAsync<CompareRunnerSheet, ComparisonRequest, PersonId>(request);

        if (result is { IsSuccess: true, Value: { } target })
            BuildComparison(target);
    }

    /// <summary>
    /// How far ahead of the runner-up the winner finished, or null when the class had no second
    /// runner to be ahead of. A mispunched runner is not second — the margin is measured to the
    /// next result that counts.
    /// </summary>
    private TimeSpan? MarginToRunnerUp(CompetitionResult winner)
    {
        var runnerUp = _field
            .Where(r => r.Class == winner.Class && r.Status == ResultStatus.Ok && r.Place == 2)
            .OrderBy(r => r.Time)
            .FirstOrDefault();

        return runnerUp is not null ? runnerUp.Time - winner.Time : null;
    }

    private void BuildOverview(Competition competition, CompetitionResult mine)
    {
        ClassLine = $"{mine.Class} · {Format.Discipline(competition.Discipline)} · {competition.Date:d MMM yyyy}";
        PlaceText = Format.Place(mine.Place);
        PlaceOfText = Format.PlaceOf(mine.Place, mine.Starters);
        TimeText = Format.Time(mine.Time);
        StatusText = Format.ResultStatus(mine.Status);

        PlaceSpoken = $"{Format.SpokenPlace(mine.Place)} av {mine.Starters} startande";
        TimeSpoken = $"tid {Format.SpokenTime(mine.Time)}";

        // The winner has no time behind the winner. What a winner wants to know is the margin
        // down to second.
        IsWinner = mine.Place == 1;

        HasMaterialGap = !IsWinner
                         && mine.BehindWinner is { } gap
                         && gap > TimeSpan.Zero
                         && mine.Time - gap is { Ticks: > 0 } winnerTime
                         && gap.TotalSeconds >= winnerTime.TotalSeconds * 0.10;

        if (IsWinner)
        {
            var margin = MarginToRunnerUp(mine);

            BehindLabel = "Före tvåan";
            BehindText = margin is { } ahead ? Format.Time(ahead) : "—";
            BehindSpoken = margin is { } spokenAhead
                ? $"före tvåan {Format.SpokenTime(spokenAhead)}"
                : "ingen tvåa att jämföra med";
        }
        else
        {
            BehindLabel = "Efter vinnaren";
            BehindText = mine.BehindWinner is { } behind ? Format.Delta(behind) : "—";
            BehindSpoken = mine.BehindWinner is { } spokenBehind
                ? $"efter vinnaren {Format.SpokenTime(spokenBehind)}"
                : "ingen tid efter vinnaren";
        }

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

        // The first name is enough over a column, and the surname is what makes it wrap.
        MineHeading = _me?.Name.Split(' ')[0] ?? "Du";
        TheirsHeading = other.Name.Split(' ')[0];

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
