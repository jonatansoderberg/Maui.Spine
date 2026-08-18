using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;
using Orientera.Features.Live;
using Orientera.Features.Results;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Travel;
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

/// <summary>One line of the start field, as Sverigelistan ranks it.</summary>
public sealed record StartFieldRow
{
    public required string Order { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string PointsText { get; init; }
    public required string RankText { get; init; }
    public required bool IsMe { get; init; }

    /// <summary>
    /// False on an entry list, where the order and the points do not exist yet. Carried on the row
    /// rather than read off the page's own state so the template stays bound to one thing.
    /// </summary>
    public bool ShowRanking { get; init; } = true;
}

public partial class EventDetailsPageViewModel(
    INavigationService _navigation,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IStartFieldSource _field,
    ILiveSource _live,
    ILiveloxSource _livelox,
    OfflinePackageService _offline,
    CompetitionContextService _context,
    CompetitionClassStore _classes,
    LiveSelection _liveSelection) : OrienteraViewModel, IReceivesNavigationParameter<CompetitionId>
{
    private CompetitionId _id;
    private Competition? _competition;
    private Person? _me;
    private ContextDecision? _decision;

    // ---- hero ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InterestDescription))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty] public partial string OrganiserLine { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArena))]
    public partial GeoPoint Arena { get; set; }

    public bool HasArena => Arena is not { Latitude: 0, Longitude: 0 };


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrganiserLogo))]
    public partial string? OrganiserLogo { get; set; }

    public bool HasOrganiserLogo => !string.IsNullOrEmpty(OrganiserLogo);
    [ObservableProperty] public partial string DateLine { get; set; } = string.Empty;
    [ObservableProperty] public partial string MetaLine { get; set; } = string.Empty;

    /// <summary>The distance and the level as marks, the same two the list draws.</summary>
    [ObservableProperty] public partial Geometry? DisciplineShape { get; set; }

    [ObservableProperty] public partial string DisciplineKey { get; set; } = string.Empty;

    [ObservableProperty] public partial Geometry? LevelShape { get; set; }

    public bool HasLevelShape => LevelShape is not null;
    [ObservableProperty] public partial bool IsInterested { get; set; }

    // ---- för dig ----
    [ObservableProperty] public partial string StateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PrimaryActionText { get; set; } = string.Empty;

    /// <summary>False when the one action the state offers is one the app cannot deliver.</summary>
    [ObservableProperty] public partial bool HasPrimaryAction { get; set; } = true;
    [ObservableProperty] public partial string MyClass { get; set; } = string.Empty;
    [ObservableProperty] public partial string MyStartText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TravelColumn))]
    [NotifyPropertyChangedFor(nameof(TravelColumnSpan))]
    public partial bool HasMyStart { get; set; }

    /// <summary>
    /// Travel sits beside the start time when there is one, and takes its place when there is
    /// not — an empty half-card reads as something failing to load.
    /// </summary>
    public int TravelColumn => HasMyStart ? 1 : 0;

    public int TravelColumnSpan => HasMyStart ? 1 : 2;
    [ObservableProperty] public partial string DeadlineText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasDeadline { get; set; }

    /// <summary>
    /// When entry has a closing date but has not opened yet. A page that says only when entry
    /// closes, while the state says the competition is merely discovered, contradicts itself —
    /// and the reader is left to guess which half is wrong.
    /// </summary>
    [ObservableProperty] public partial string OpensText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasOpens { get; set; }
    [ObservableProperty] public partial string TravelText { get; set; } = string.Empty;

    /// <summary>The time, kept apart from the distance so only it carries the estimate colour.</summary>
    [ObservableProperty] public partial string TravelDurationText { get; set; } = string.Empty;

    [ObservableProperty] public partial string TravelSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasStartField { get; set; }
    [ObservableProperty] public partial string StartFieldCaption { get; set; } = string.Empty;

    /// <summary>
    /// Whether the list below is who has entered rather than who has been drawn. It changes the
    /// heading and hides the ranking columns, which are empty before the draw and would otherwise
    /// read as a Sverigelistan that failed to load.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartFieldHeading))]
    public partial bool IsEntryList { get; set; }

    public string StartFieldHeading => IsEntryList ? "ANMÄLDA" : "STARTFÄLT";

    /// <summary>
    /// Fetches the entry list on demand and folds it out under the start field.
    /// </summary>
    /// <remarks>
    /// On demand rather than always: the two lists say almost the same thing once the draw is out,
    /// and a page that shows both at full length twice is a page nobody scrolls to the bottom of.
    /// </remarks>
    [RelayCommand]
    private async Task ToggleEntrants()
    {
        ShowEntrants = !ShowEntrants;

        if (!ShowEntrants || Entrants.Count > 0 || _competition is null || _me is null)
            return;

        var entrants = await _field.GetEntryListAsync(_competition.Id, MyClass);

        foreach (var runner in entrants)
        {
            Entrants.Add(new StartFieldRow
            {
                Order = "—",
                Name = runner.Name,
                Club = runner.Club,
                PointsText = string.Empty,
                RankText = string.Empty,
                IsMe = RunnerIdentity.Of(runner.Name, runner.Club)
                    .Matches(RunnerIdentity.Of(_me.Name, _me.Club)),
                ShowRanking = false,
            });
        }

        EntrantsCaption = $"{Entrants.Count} anmälda i {MyClass}";
    }

    /// <summary>Whether the entry list is worth offering as a separate list of its own.</summary>
    [ObservableProperty] public partial bool HasEntryList { get; set; }

    /// <summary>The entry list, opened on demand once the start list has taken its place.</summary>
    public ObservableCollection<StartFieldRow> Entrants { get; } = [];

    [ObservableProperty] public partial bool ShowEntrants { get; set; }

    [ObservableProperty] public partial string EntrantsCaption { get; set; } = string.Empty;

    public ObservableCollection<StartFieldRow> StartField { get; } = [];

    [ObservableProperty] public partial string PredictionText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasPrediction { get; set; }
    [ObservableProperty] public partial string PredictionAccessibility { get; set; } = string.Empty;

    // ---- sections ----
    [ObservableProperty] public partial bool HasBriefing { get; set; }
    [ObservableProperty] public partial bool HasDocuments { get; set; }

    // ---- Livelox ----

    /// <summary>
    /// Livelox has this competition and there is something there to look at. An event with no
    /// participants and no map is a shell, and a link to it is a dead end.
    /// </summary>
    [ObservableProperty] public partial bool HasLivelox { get; set; }

    [ObservableProperty] public partial string LiveloxText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanFollowLive { get; set; }
    [ObservableProperty] public partial bool HasResults { get; set; }

    /// <summary>Why a quick action is unavailable. Empty when it is available.</summary>
    [ObservableProperty] public partial string LiveConditionText { get; set; } = string.Empty;

    /// <inheritdoc cref="LiveConditionText" />
    [ObservableProperty] public partial string ResultsConditionText { get; set; } = string.Empty;

    /// <summary>Which bundled terrain picture the hero looks up — the discipline, in lower case.</summary>
    [ObservableProperty] public partial string HeroDiscipline { get; set; } = string.Empty;

    // ---- offline ----
    [ObservableProperty] public partial bool IsFromCache { get; set; }
    [ObservableProperty] public partial bool IsUnavailable { get; set; }
    [ObservableProperty] public partial string CacheLabel { get; set; } = string.Empty;

    public ObservableCollection<BriefingItem> Briefing { get; } = [];
    public ObservableCollection<BriefingItem> Facts { get; } = [];
    public ObservableCollection<DocumentItem> Documents { get; } = [];

    public string InterestGlyph => IsInterested ? "★" : "☆";

    /// <summary>The same sentence the card in the list uses, so the star reads alike in both.</summary>
    public string InterestDescription => IsInterested
        ? $"Ta bort intressemarkeringen för {Name}"
        : $"Markera att du är intresserad av {Name}";

    partial void OnIsInterestedChanged(bool value)
    {
        OnPropertyChanged(nameof(InterestGlyph));
        OnPropertyChanged(nameof(InterestDescription));
    }

    public Task OnNavigationParameterAsync(CompetitionId param)
    {
        _id = param;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _me = await _people.GetMeAsync();

        // Read through the offline package: with coverage this is live and refreshes the
        // stored copy, without it the stored copy is what keeps the page usable at the arena.
        var snapshot = await _offline.GetAsync(_id);

        IsFromCache = snapshot.Origin == DataOrigin.Cache;
        IsUnavailable = snapshot.Origin == DataOrigin.Unavailable;

        CacheLabel = snapshot is { Origin: DataOrigin.Cache, CachedAt: { } cachedAt }
            ? $"Offline — sparat {Format.Clock(cachedAt)}"
            : string.Empty;

        _competition = snapshot.Competition;

        if (_competition is null || _me is null)
            return;

        Title = _competition.Name;

        await LoadAsync(() => BuildAsync(_competition, _me, snapshot));
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
                // A tab root takes no navigation parameter, so the competition is left where the
                // live tab looks for it rather than guessed at from whatever is running.
                _liveSelection.Select(_competition.Id);
                await _navigation.SwitchToTabAsync<LivePage>();
                break;

            case ContextAction.ShowMyResult:
            case ContextAction.Analyse:
            case ContextAction.ShowRouteChoice:
            case ContextAction.ShowPreliminary:
                await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(_competition.Id);
                break;

            case ContextAction.Navigate:
                await NavigateToArena();
                break;

            case ContextAction.Register:
                await OpenEventorEntry();
                break;

            // Every action that can become a button label needs its own case. A default that
            // does something else than the button says is the bug this switch already had once:
            // on race day it read "Navigera" and opened the class picker.
            default:
                break;
        }
    }

    private LiveloxLink? _liveloxLink;

    /// <summary>
    /// Looks up the competition in Livelox.
    /// </summary>
    /// <remarks>
    /// A link, and only a link. Livelox keeps maps and routes deliberately — for copyright,
    /// attribution and privacy — and no API returns them. Course data does have an endpoint, but
    /// it is scoped and our key does not carry <c>courses.read</c> (SP-07). Offering the door and
    /// saying whose house it is beats pretending the app has what is behind it.
    /// </remarks>
    private async Task LoadLiveloxAsync(CompetitionId competition)
    {
        _liveloxLink = null;
        HasLivelox = false;

        try
        {
            _liveloxLink = await _livelox.GetLiveloxAsync(competition);
        }
        catch (SourceUnavailableException)
        {
            return;
        }

        if (_liveloxLink is not { } link || (!link.HasMap && link.Participants == 0))
            return;

        HasLivelox = true;

        LiveloxText = link.Participants > 0
            ? $"{link.Participants} löpares vägval i Livelox"
            : "Karta och banor i Livelox";
    }

    [RelayCommand]
    private async Task OpenLivelox()
    {
        if (_liveloxLink is { Url: { Length: > 0 } url })
            await Launcher.OpenAsync(url);
    }

    /// <summary>
    /// Whether live actually exists for this competition, not just whether it is under way.
    /// </summary>
    /// <remarks>
    /// The context engine knows the calendar; it does not know whether LiveResults has this race.
    /// A competition can be running and have no live source at all, and offering "Följ live" for
    /// one lands the runner in a different race with nothing said (#89). The list of what is live
    /// right now is the same list the live tab reads, so asking costs one cached request.
    /// </remarks>
    /// <summary>
    /// The field, as Sverigelistan ranks it. Not a forecast — three measurements said an honest
    /// placement interval covers half the field (#113, #117), so this shows what the interval was
    /// made of and lets the reader draw their own conclusion.
    /// </summary>
    private async Task LoadStartFieldAsync(CompetitionId competition, string className, Person me)
    {
        StartField.Clear();
        Entrants.Clear();
        HasStartField = false;
        HasEntryList = false;
        ShowEntrants = false;

        if (string.IsNullOrWhiteSpace(className))
            return;

        var field = await _field.GetStartFieldAsync(competition, className);

        // Before the draw the start list is empty and the entry list is the whole answer to "who
        // else is going?" — the question people actually ask in the weeks they are deciding.
        IsEntryList = field.Count == 0;

        if (IsEntryList)
            field = await _field.GetEntryListAsync(competition, className);

        // Everyone who has entered stays reachable after the draw too. A start list is ordered by
        // time and stops at the class you are in; the entry list answers a different question —
        // who is coming at all — and losing it the moment the times are drawn was an either/or
        // nobody asked for.
        HasEntryList = !IsEntryList;

        if (field.Count == 0)
            return;

        int ranked = 0;

        foreach (var runner in field)
        {
            if (runner.Points is not null)
                ranked++;

            StartField.Add(new StartFieldRow
            {
                Order = runner.Points is null ? "—" : ranked.ToString(Format.Culture),
                Name = runner.Name,
                Club = runner.Club,
                PointsText = runner.Points is { } points ? points.ToString("N2", Format.Culture) : "—",

                // "utan ranking" på varje rad var en kolumn som mest sa att den var tom. Raden
                // visar riksplaceringen när den finns och tiger annars — och före lottningen,
                // när listan är anmälda och inte startande, finns den inte för någon.
                RankText = !IsEntryList && runner.NationalRank is { } rank ? $"riks {rank}" : string.Empty,

                // The entry list has no person ids, so the reader is found the way the live lists
                // find them — by name and club (#75).
                IsMe = IsEntryList
                    ? RunnerIdentity.Of(runner.Name, runner.Club).Matches(RunnerIdentity.Of(me.Name, me.Club))
                    : runner.Person == me.Id,
                ShowRanking = !IsEntryList,
            });
        }

        // Two different sentences because they answer two different questions. Before the draw
        // nobody has points here — the entry list carries no club ids to look them up by — so a
        // "0 av 36 finns på listan" would read as a broken Sverigelistan rather than as a field
        // that has not been drawn.
        StartFieldCaption = IsEntryList
            ? $"{field.Count} anmälda i {className}. Startlistan är inte lottad än."
            : $"{ranked} av {field.Count} finns på listan";

        HasStartField = true;
    }

    private async Task<bool> HasLiveSourceAsync(CompetitionId competition)
    {
        try
        {
            var live = await _live.GetLiveCompetitionsAsync();

            return live.Any(c => c.Id == competition);
        }
        // Not knowing is not the same as knowing there is none. Offline, the button stays and
        // fails the way everything else does.
        catch (SourceUnavailableException)
        {
            return true;
        }
    }

    /// <summary>
    /// Hands the arena to the phone's map app. Orientera does not do turn-by-turn navigation, and
    /// the answer to "how do I get there" is one the map already has.
    /// </summary>
    private async Task NavigateToArena()
    {
        if (_competition is not { Location: { Latitude: not 0, Longitude: not 0 } arena } competition)
            return;

        try
        {
            await Map.OpenAsync(
                new Location(arena.Latitude, arena.Longitude),
                new MapLaunchOptions { Name = competition.Place, NavigationMode = NavigationMode.Driving });
        }
        catch (Exception)
        {
            // No map app, or a platform that will not open one. The arena is on the page either
            // way, and a failed launch must not take the page down.
        }
    }

    /// <summary>
    /// Hands the runner over to Eventor to actually enter.
    /// </summary>
    /// <remarks>
    /// The button used to open the class picker, which saves a class locally and enters nothing.
    /// A runner who pressed "Anmäl dig", picked a class and closed the app was not entered and had
    /// no way to tell. The class picker is still a quick action of its own, where the word for it
    /// is "Klass".
    /// </remarks>
    [RelayCommand]
    private async Task OpenEventorEntry()
    {
        if (_competition is null)
            return;

        // The landing first (P11): what is about to happen, and which class goes with it. Leaving
        // for a page in someone else's language is a step the runner takes, not a side effect of
        // pressing the button they were offered.
        var go = await _navigation.NavigateToWithResultAsync<EntryHandoffSheet, EntryHandoff, bool>(
            new EntryHandoff(_competition.Name, MyClass));

        if (go is not { IsSuccess: true, Value: true })
            return;

        // In the app, not Safari: the Eventor session lives in the app's own web view store, and
        // an entry page opened externally is an entry page that says you are not logged in.
        await _navigation.NavigateToAsync<EventorEntrySheet, EventorEntry>(
            new EventorEntry(_competition.Id, MyClass));
    }

    [RelayCommand]
    private async Task OpenChooseClass()
    {
        if (_competition is null)
            return;

        var result = await _navigation.NavigateToWithResultAsync<ChooseClassSheet, ClassChoice, string>(
            new ClassChoice(
                _competition.Classes,
                "Klassen avgör vilka PM-punkter som visas, och vilken klass Live öppnar i.",
                MyClass));

        if (result is not { IsSuccess: true, Value: { } className })
            return;

        MyClass = className;
        _classes.Save(_competition.Id, className);

        // The briefing is filtered by class, so it has to be rebuilt rather than wait for the
        // next visit — the choice is meant to be visible the moment it is made.
        BuildBriefing(_competition, className);
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
    private async Task ToggleInterest()
    {
        if (_competition is not null)
            IsInterested = await _events.ToggleInterestAsync(_competition.Id);
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

    private async Task BuildAsync(Competition competition, Person me, CompetitionSnapshot snapshot)
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);

        // Offline the context engine is fed from the package instead of the sources — the CTA
        // is the most useful thing on this page and has to survive the outage with the rest.
        _decision = IsFromCache
            ? ContextEngine.Evaluate(new ContextInput
            {
                Now = now,
                Competition = competition,
                MyEntryRegisteredAt = snapshot.MyEntryRegisteredAt,
                GroupEntryRegisteredAt = snapshot.GroupEntryRegisteredAt,
                MyStartTime = snapshot.MyStart?.StartTime,
            })
            : await _context.EvaluateAsync(competition);
        var interests = await _events.GetInterestsAsync();

        // Entries are only needed to know whether I am registered; offline that is answered by
        // whether the package carried a start time for me.
        var entries = IsFromCache
            ? []
            : await _participation.GetEntriesAsync();

        var starts = IsFromCache
            ? (snapshot.MyStart is { } cachedStart ? new List<Start> { cachedStart } : [])
            : await _participation.GetStartsAsync(competition.Id);

        Name = competition.Name;
        OrganiserLine = $"{competition.Organiser} · {competition.Place}";
        OrganiserLogo = competition.OrganiserLogo;
        Arena = competition.Location;
        DateLine = competition.HasFirstStart
            ? $"{Format.RelativeDate(competition.Date, today)} · första start {Format.Clock(competition.FirstStart)}"
            : $"{Format.RelativeDate(competition.Date, today)} · starttid ej satt";
        MetaLine = $"{Format.Discipline(competition.Discipline)} · {Format.Level(competition.Level)} · {competition.District}";
        // Qualified: this view model has a property of the same name as the helper.
        DisciplineShape = Presentation.DisciplineShape.For(competition.Discipline);
        DisciplineKey = competition.Discipline.ToString();
        HeroDiscipline = competition.Discipline.ToString().ToLowerInvariant();
        LevelShape = Presentation.DisciplineShape.For(competition.Level);
        OnPropertyChanged(nameof(HasLevelShape));
        IsInterested = interests.Contains(competition.Id);

        StateText = _decision.StateText;
        PrimaryActionText = _decision.PrimaryActionText;

        // The entry wins, then the picked class. #61 put the picker first because the app had no
        // way of knowing what the runner had actually entered, and a picker that silently did
        // nothing was the bug being fixed. Since the entries are read from Eventor there is a
        // fact where there was only a preference, and a page that shows H45 to somebody entered
        // in H21 offers them the wrong start list, the wrong field and the wrong start time.
        // The picker still decides every competition the runner has not entered, which is all of
        // the ones it was ever really for.
        var myEntry = entries.FirstOrDefault(e => e.Competition == competition.Id && e.Person == me.Id);
        MyClass = myEntry?.Class
            ?? _classes.For(competition.Id)
            ?? snapshot.MyStart?.Class
            ?? me.DefaultClass;

        var myStart = starts.FirstOrDefault(s => s.Person == me.Id);
        HasMyStart = myStart is not null;
        MyStartText = myStart is not null ? Format.Clock(myStart.StartTime) : "—";

        HasDeadline = myEntry is null
                      && competition.Schedule.EntryDeadline is { } deadline
                      && deadline > now;

        DeadlineText = HasDeadline
            ? $"Anmälan stänger {Format.Deadline(DateOnly.FromDateTime(competition.Schedule.EntryDeadline!.Value.Date), today)}"
            : string.Empty;

        // Eventor publishes both dates, and a competition whose entry has not opened yet was
        // showing only the closing one — beside a state that said "Upptäckt". Two halves of the
        // same schedule, and the reader had to guess which one to believe.
        HasOpens = myEntry is null
                   && competition.Schedule.RegistrationOpensAt is { } opens
                   && opens > now;

        OpensText = HasOpens
            ? $"Anmälan öppnar {Format.Deadline(DateOnly.FromDateTime(competition.Schedule.RegistrationOpensAt!.Value.Date), today)}"
            : string.Empty;

        double distance = TravelEstimate.DistanceKm(me.Home, competition.Location);
        var duration = TravelEstimate.Duration(me.Home, competition.Location);

        // "Fågelvägen" is the whole caveat in one word: it is the distance the app can compute,
        // and every driver knows the road is longer.
        TravelText = $"ca {Format.Distance(distance)} fågelvägen";
        TravelDurationText = $"~{duration.TotalMinutes:0} min";
        TravelSpoken = $"uppskattat {Format.Distance(distance)} fågelvägen, ungefär {duration.TotalMinutes:0} minuter";

        var prediction = IsFromCache
            ? snapshot.Prediction
            : await _participation.GetPredictionAsync(competition.Id, me.Id);
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

        await LoadStartFieldAsync(competition.Id, MyClass, me);

        CanFollowLive = _decision.State == ContextState.Live && await HasLiveSourceAsync(competition.Id);

        await LoadLiveloxAsync(competition.Id);

        // The big button routes through the same action, so it lies in the same way. A race with
        // no live source has nothing to offer here; the quick actions below still do.
        //
        // ShowCompetition is dropped outright: its label is "Visa tävling", and this is the
        // competition. A primary action that leads to the page it is standing on is not an action,
        // and the deadline block above already says what there is to know before entry opens.
        HasPrimaryAction = _decision.PrimaryAction is not ContextAction.ShowCompetition
                           && (_decision.PrimaryAction != ContextAction.FollowLive || CanFollowLive);

        HasResults = _decision.State >= ContextState.ResultsPublished;

        // A greyed button with no reason reads as a broken button (testkörningen, skärm 17).
        LiveConditionText = CanFollowLive ? string.Empty : "finns när tävlingen startat";
        ResultsConditionText = HasResults ? string.Empty : "finns efter målgång";

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
