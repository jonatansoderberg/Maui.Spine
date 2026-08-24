using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Analysis;
using Orientera.Services.Offline;
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

/// <summary>Whose race to open: the competition, the class it was run in, and the runner.</summary>
/// <param name="Person">
/// The runner, or null for the reader's own row. Null rather than the reader's id because the
/// reader's identity is local and the result lists carry Eventor's — the page matches them the
/// way every other list does, by name and club (SP-04).
/// </param>
public sealed record RunnerResultTarget(CompetitionId Competition, string Class, PersonId? Person = null);

public partial class RunnerResultPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IRaceStorySource _stories) : OrienteraViewModel, IReceivesNavigationParameter<RunnerResultTarget>
{
    private RunnerResultTarget _target = new(new CompetitionId(string.Empty), string.Empty);
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


    public ObservableCollection<LegRow> Legs { get; } = [];
    public ObservableCollection<ComparisonRow> Comparison { get; } = [];



    private Prediction? _prediction;

    public Task OnNavigationParameterAsync(RunnerResultTarget param)
    {
        _target = param;
        _id = param.Competition;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (navigationDirection == NavigationDirection.Back)
            return;

        IsIdle = false;

        // Twice before calling it an outage. The backend does not abandon a fetch when the caller
        // hangs up — it keeps loading and holds the result — so a first ask that runs into the
        // app's twenty-second timeout is usually followed by one that answers in under a second.
        if (!await LoadAsync(BuildAsync) && !await LoadAsync(BuildAsync))
        {
            HasResult = false;
            EmptyMessage = "Ingen anslutning. Resultat och sträcktider behöver nätverk.";
        }

        IsIdle = !HasResult;
    }

    /// <summary>
    /// The class this race was run in, with its split times — or none where they cannot be had.
    /// </summary>
    /// <remarks>
    /// The class rather than the competition, which is the whole difference between a page that
    /// opens and one that times out: O-Ringen's result list is 86 MB with splits and its H45 is
    /// a couple of hundred rows of it. Splits are asked for here and nowhere else — a leg is only
    /// a good or a bad leg compared to the class that ran it, so the analysis needs everyone's.
    /// </remarks>
    private async Task<IReadOnlyList<CompetitionResult>> FieldAsync()
    {
        if (_target.Class.Length == 0)
            return [];

        try
        {
            return await _participation.GetClassResultsAsync(_id, _target.Class, splits: true);
        }
        catch (SourceUnavailableException)
        {
            return [];
        }
    }

    /// <summary>
    /// My own rows in this competition, or none when nobody is signed in and none when the
    /// question cannot be asked. A missing answer costs the page nothing: it falls back to the
    /// whole result list, which is what it read before.
    /// </summary>
    private async Task<IReadOnlyList<CompetitionResult>> OwnAsync()
    {
        if (_me is null)
            return [];

        try
        {
            return await _participation.GetOwnResultsAsync(_me.Id, [_id], splits: true);
        }
        catch (SourceUnavailableException)
        {
            return [];
        }
    }

    /// <summary>Keeps an abandoned request from becoming an unhandled failure.</summary>
    private static void Forget(Task task) =>
        task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

    private async Task BuildAsync()
    {
        // Both at once. The competition costs five upstream calls to Eventor — the event, its
        // documents, its classes, the schedule and the first start — and my own row one; in turn
        // they were the page's wait, and a cold competition alone can outlast the twenty seconds
        // the app gives its backend.
        var competitionTask = _events.GetCompetitionAsync(_id);

        _me = await _people.GetMeAsync();

        var ownTask = OwnAsync();

        var competition = await competitionTask;

        if (competition is null || _me is null)
        {
            // The results list reaches back through the runner's whole Eventor history; the
            // calendar the app reads competitions from covers a few months. A race older than that
            // window is not an outage and must not be reported as one — the page said "Ingen
            // anslutning" while the list behind it had just loaded over the same network.
            CompetitionName = string.Empty;
            Title = string.Empty;
            EmptyMessage = "Den här tävlingen ligger utanför kalendern appen läser, så resultatet "
                         + "går inte att öppna här. Raden i listan visar tid och placering.";

            // The request is on its way and nobody is going to read it. Observed rather than
            // abandoned, so its failure is not an unhandled one.
            Forget(ownTask);
            return;
        }

        CompetitionName = competition.Name;
        Title = competition.Name;

        // The whole result list is what this page reads, and for all but the largest competitions
        // it is a few hundred kilobytes. O-Ringen's is 86 MB and ninety-seven seconds of it, and
        // Eventor has no way to ask for one class of a normal event — so where the list cannot be
        // had, my own rows can: eight kilobytes, and a placement out of a field is most of what
        // the page exists to say.
        var own = await ownTask;

        _field = await FieldAsync();

        bool ownOnly = _field.Count == 0 && own.Count > 0;

        if (ownOnly)
            _field = own;

        // A named runner is found by id, which the row they were tapped on already carried. The
        // reader themselves is found by name and club: their identity is local and the result
        // list carries Eventor's, and that is the only comparison spanning both (SP-04).
        var me = RunnerIdentity.Of(_me.Name, _me.Club);

        _mine = _target.Person is { } person
            ? _field.FirstOrDefault(r => r.Person == person)
            : _field.FirstOrDefault(r => me.Matches(RunnerIdentity.Of(r.Name, r.Club)));
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

        if (_mine is null)
        {
            // The page is about one runner, so a runner who is not in the list leaves nothing to
            // draw. The field itself is one step back, where it belongs.
            HasResult = false;
            EmptyMessage = _target.Person is null
                ? $"Du är inte med i resultatlistan för {_target.Class}."
                : "Löparen finns inte i den här resultatlistan.";
            return;
        }

        // Whose race this is, once it is known to be somebody's.
        Title = _target.Person is null ? competition.Name : $"{_mine.Name} — {competition.Name}";

        BuildOverview(competition, _mine);

        // Sträcktider mäts mot klassen. Med bara den egna raden i handen blir varje sträcka
        // "bäst i klassen", vilket är en siffra som ser ut som en analys utan att vara en.
        _legs = ownOnly ? [] : SplitAnalyzer.Analyse(_mine, _field);
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
