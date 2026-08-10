using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Features.Live;
using Orientera.Features.Results;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Events;

/// <summary>A PM fact as the briefing renders it: the value, and where it came from.</summary>
public sealed record BriefingItem
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required string Source { get; init; }

    /// <summary>Low-confidence extractions are hedged in the UI rather than stated flatly.</summary>
    public required bool IsUncertain { get; init; }
}

public sealed record DocumentItem
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Meta { get; init; }

    public string Accessibility => $"{Title}, {Meta}";
}

public partial class EventDetailsPageViewModel(
    INavigationService _navigation,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    CompetitionContextService _context) : ViewModelBase, IReceivesNavigationParameter<CompetitionId>
{
    private CompetitionId _id;
    private Competition? _competition;
    private Person? _me;
    private ContextDecision? _decision;

    // ---- hero ----
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrganiserLine { get; set; } = string.Empty;
    [ObservableProperty] public partial string DateLine { get; set; } = string.Empty;
    [ObservableProperty] public partial string MetaLine { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsFavourite { get; set; }

    // ---- för dig ----
    [ObservableProperty] public partial string StateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PrimaryActionText { get; set; } = string.Empty;
    [ObservableProperty] public partial string MyClass { get; set; } = string.Empty;
    [ObservableProperty] public partial string MyStartText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasMyStart { get; set; }
    [ObservableProperty] public partial string DeadlineText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasDeadline { get; set; }
    [ObservableProperty] public partial string TravelText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PredictionText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasPrediction { get; set; }
    [ObservableProperty] public partial string PredictionAccessibility { get; set; } = string.Empty;

    // ---- sections ----
    [ObservableProperty] public partial bool HasBriefing { get; set; }
    [ObservableProperty] public partial bool HasDocuments { get; set; }
    [ObservableProperty] public partial bool CanFollowLive { get; set; }
    [ObservableProperty] public partial bool HasResults { get; set; }

    public ObservableCollection<BriefingItem> Briefing { get; } = [];
    public ObservableCollection<BriefingItem> Facts { get; } = [];
    public ObservableCollection<DocumentItem> Documents { get; } = [];

    public string FavouriteGlyph => IsFavourite ? "★" : "☆";

    partial void OnIsFavouriteChanged(bool value) => OnPropertyChanged(nameof(FavouriteGlyph));

    public Task OnNavigationParameterAsync(CompetitionId param)
    {
        _id = param;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _competition = await _events.GetCompetitionAsync(_id);
        _me = await _people.GetMeAsync();

        if (_competition is null || _me is null)
            return;

        Title = _competition.Name;

        await BuildAsync(_competition, _me);
    }

    [RelayCommand]
    private async Task PrimaryAction()
    {
        if (_decision is null || _competition is null)
            return;

        // The context state decides the verb; the detail page just routes it.
        switch (_decision.PrimaryAction)
        {
            case ContextAction.FollowLive:
                await _navigation.SwitchToTabAsync<LivePage>();
                break;

            case ContextAction.ShowMyResult:
            case ContextAction.Analyse:
            case ContextAction.ShowRouteChoice:
            case ContextAction.ShowPreliminary:
                await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(_competition.Id);
                break;

            case ContextAction.Register:
                await OpenChooseClass();
                break;

            default:
                await OpenChooseClass();
                break;
        }
    }

    [RelayCommand]
    private async Task OpenChooseClass()
    {
        if (_competition is null)
            return;

        var result = await _navigation.NavigateToWithResultAsync<ChooseClassSheet, string>();

        if (result is { IsSuccess: true, Value: { } className })
            MyClass = className;
    }

    [RelayCommand]
    private async Task OpenPrediction()
    {
        if (_competition is null || _me is null)
            return;

        var prediction = await _participation.GetPredictionAsync(_competition.Id, _me.Id);

        if (prediction is not null)
            await _navigation.NavigateToAsync<PredictionInfoSheet, Prediction>(prediction);
    }

    [RelayCommand]
    private async Task OpenLive() => await _navigation.SwitchToTabAsync<LivePage>();

    [RelayCommand]
    private async Task OpenResults()
    {
        if (_competition is not null)
            await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(_competition.Id);
    }

    [RelayCommand]
    private async Task ToggleFavourite()
    {
        if (_competition is not null)
            IsFavourite = await _events.ToggleFavouriteAsync(_competition.Id);
    }

    /// <summary>
    /// External destinations are opened explicitly, never silently embedded — the user should
    /// never be unsure whether they have left the app.
    /// </summary>
    [RelayCommand]
    private static async Task OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // M0 documents point at placeholder URLs; a failed launch must not break the page.
        }
    }

    private async Task BuildAsync(Competition competition, Person me)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);

        _decision = await _context.EvaluateAsync(competition);
        var entries = await _participation.GetEntriesAsync();
        var starts = await _participation.GetStartsAsync(competition.Id);
        var favourites = await _events.GetFavouritesAsync();

        Name = competition.Name;
        OrganiserLine = $"{competition.Organiser} · {competition.Place}";
        DateLine = $"{Format.RelativeDate(competition.Date, today)} · första start {Format.Clock(competition.FirstStart)}";
        MetaLine = $"{Format.Discipline(competition.Discipline)} · {Format.Level(competition.Level)} · {competition.District}";
        IsFavourite = favourites.Contains(competition.Id);

        StateText = _decision.StateText;
        PrimaryActionText = _decision.PrimaryActionText;

        var myEntry = entries.FirstOrDefault(e => e.Competition == competition.Id && e.Person == me.Id);
        MyClass = myEntry?.Class ?? me.DefaultClass;

        var myStart = starts.FirstOrDefault(s => s.Person == me.Id);
        HasMyStart = myStart is not null;
        MyStartText = myStart is not null ? Format.Clock(myStart.StartTime) : "—";

        HasDeadline = myEntry is null
                      && competition.Schedule.EntryDeadline is { } deadline
                      && deadline > now;

        DeadlineText = HasDeadline
            ? $"Anmälan stänger {Format.RelativeDate(DateOnly.FromDateTime(competition.Schedule.EntryDeadline!.Value.Date), today)}"
            : string.Empty;

        double distance = me.Home.DistanceKmTo(competition.Location);
        TravelText = $"{Format.Distance(distance)} hemifrån · ca {Math.Round(distance / 70.0 * 60)} min";

        var prediction = await _participation.GetPredictionAsync(competition.Id, me.Id);
        HasPrediction = prediction is not null;
        PredictionText = prediction is not null
            ? $"Förväntad placering {prediction.Range} av {prediction.FieldSize}"
            : string.Empty;

        // The interval is modelled, and the spoken form has to say so — colour alone carries
        // that distinction for sighted users only.
        PredictionAccessibility = prediction is not null
            ? $"Uppskattning: förväntad placering {prediction.LowPlace} till {prediction.HighPlace} "
              + $"av {prediction.FieldSize} anmälda"
            : string.Empty;

        CanFollowLive = _decision.State == ContextState.Live;
        HasResults = _decision.State >= ContextState.ResultsPublished;

        BuildBriefing(competition, MyClass);
        BuildDocuments(competition, now);
    }

    private void BuildBriefing(Competition competition, string className)
    {
        Briefing.Clear();
        Facts.Clear();

        if (competition.Profile is not { } profile)
        {
            HasBriefing = false;
            return;
        }

        // The briefing leads with what changes how you run: terrain, then the risks.
        foreach (var fact in profile.ForClass(className)
                     .Where(f => f.Group is ProfileGroup.Terrain or ProfileGroup.ClassSpecific or ProfileGroup.Risk))
        {
            Briefing.Add(ToItem(fact));
        }

        foreach (var fact in profile.ForClass(className)
                     .Where(f => f.Group is ProfileGroup.Logistics or ProfileGroup.Competition))
        {
            Facts.Add(ToItem(fact));
        }

        HasBriefing = Briefing.Count > 0;
    }

    private static BriefingItem ToItem(ProfileFact fact) => new()
    {
        Label = fact.Label,
        Value = fact.Value,
        Source = fact.SourceLabel,
        IsUncertain = fact.Confidence < 0.7,
    };

    private void BuildDocuments(Competition competition, DateTimeOffset now)
    {
        Documents.Clear();

        foreach (var document in competition.Documents.Where(d => d.PublishedAt is null || d.PublishedAt <= now))
        {
            Documents.Add(new DocumentItem
            {
                Title = document.Title,
                Url = document.Url,
                Meta = document.PublishedAt is { } published
                    ? $"Publicerat {published:d MMM}"
                    : "Öppnas externt",
            });
        }

        HasDocuments = Documents.Count > 0;
    }
}
