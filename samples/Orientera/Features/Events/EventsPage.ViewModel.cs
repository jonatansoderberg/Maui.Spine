using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Grouping;
using Orientera.Services.Local;
using Orientera.Services.Offline;
using Orientera.Services.Relevance;
using Orientera.Services.Eventor;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Events;

public sealed partial class FilterChip : ObservableObject
{
    public required QuickFilter Filter { get; init; }

    /// <summary>Settable because the district chip is named after whoever is holding the phone.</summary>
    [ObservableProperty]
    public required partial string Label { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public partial class EventsPageViewModel(
    INavigationService _navigation,
    EventorSessionResume _resume,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IOfflineStore _offlineStore,
    DistrictStore _districts,
    RacePreferenceStore _preferences,
    CompetitionContextService _context) : OrienteraViewModel
{
    private IReadOnlyList<Competition> _all = [];

    /// <summary>
    /// What the selected quick filter leaves, before the sheet's filter narrows it further. The
    /// sheet counts against this: a count of the whole calendar would promise rows that "Mina"
    /// or "Denna vecka" is going to take away again.
    /// </summary>
    private IReadOnlyList<Competition> _quickMatches = [];
    private Person? _me;
    private IReadOnlySet<CompetitionId> _interests = new HashSet<CompetitionId>();

    /// <summary>Before the identity has loaded, and for anyone whose district is unknown.</summary>
    private const string DistrictChipLabel = "Mitt distrikt";
    private EventFilter _filter = EventFilter.Default;

    public ObservableCollection<FilterChip> Chips { get; } =
    [
        new() { Filter = QuickFilter.ForYou, Label = "För dig", IsSelected = true },
        new() { Filter = QuickFilter.Near, Label = "Nära" },
        new() { Filter = QuickFilter.District, Label = DistrictChipLabel },
        new() { Filter = QuickFilter.Bigger, Label = "Större" },
        new() { Filter = QuickFilter.ThisWeek, Label = "Denna vecka" },
        new() { Filter = QuickFilter.Mine, Label = "Mina" },
        new() { Filter = QuickFilter.Interested, Label = "Intresserad" },
        new() { Filter = QuickFilter.Past, Label = "Tidigare" },
    ];

    /// <summary>The list, in dated sections. "För dig" is one section, since it is ranked.</summary>
    public ObservableCollection<EventSection> Sections { get; } = [];

    /// <summary>
    /// What the filter sheet has narrowed the list by, one removable chip each. Shown only when
    /// something is set: seeing "Filter (3)" without seeing *which* three is the state a short
    /// list gets mistaken for a broken calendar in.
    /// </summary>
    public ObservableCollection<FilterFacet> ActiveFacets { get; } = [];

    [ObservableProperty]
    public partial bool HasActiveFacets { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasCards { get; set; }

    /// <summary>
    /// The search box, on the page rather than in the sheet: you type and see, instead of typing,
    /// closing the sheet, and finding out whether it matched.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    async partial void OnQueryChanged(string value)
    {
        _filter = _filter with { Query = value.Trim() };
        ShowFilterAction();

        if (_me is not null)
            await LoadAsync(BuildAsync);
    }

    /// <summary>Map discovery is an M4 feature; M0 shows the placeholder rather than pretending.</summary>
    [ObservableProperty]
    public partial bool IsMapMode { get; set; }

    private QuickFilter Selected => Chips.First(c => c.IsSelected).Filter;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        ShowFilterAction();

        var session = _resume.Generation;

        await ReloadAsync();

        // Startfältet på tävlingssidan läses med löparens egen inloggning, så den här fliken är
        // ofta den första man öppnar efter att sessionen dött.
        await _resume.EnsureAsync(_navigation);

        if (_resume.Generation != session)
            await ReloadAsync();
    }

    public override Task OnTabReselectedAsync()
    {
        SelectChip(Chips[0]);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectChip(FilterChip chip)
    {
        foreach (var c in Chips)
            c.IsSelected = ReferenceEquals(c, chip);

        await LoadAsync(BuildAsync);
    }

    /// <summary>
    /// Puts what the filter has narrowed by on the page, as chips that can be taken back off.
    /// </summary>
    /// <remarks>
    /// The header button used to carry a count — "Filter (3)". It said how many choices were set
    /// but never which, and it only ever refreshed on re-entering the tab: the header reads
    /// <c>PageActions</c> when the page appears and is not told when the collection changes. The
    /// chips say which, and say it when it happens.
    /// </remarks>
    private void ShowFilterAction()
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Filter", command: OpenFilterCommand));

        ActiveFacets.Clear();

        foreach (var facet in _filter.Facets)
            ActiveFacets.Add(facet);

        HasActiveFacets = ActiveFacets.Count > 0;
    }

    /// <summary>
    /// Takes one choice back out. The facet carries the filter without itself, so the page never
    /// has to know how any particular choice was stored.
    /// </summary>
    [RelayCommand]
    private async Task RemoveFacet(FilterFacet facet)
    {
        _filter = facet.Without;
        _districts.Save(_filter.Districts);
        ShowFilterAction();
        await LoadAsync(BuildAsync);
    }

    /// <summary>
    /// Clears the sheet's choices and leaves the search box alone — it is its own control, in
    /// view, with its own clear button, and it is not one of the chips being cleared.
    /// </summary>
    [RelayCommand]
    private async Task ClearFacets()
    {
        _filter = EventFilter.Default with { Query = _filter.Query };
        _districts.Save(_filter.Districts);
        ShowFilterAction();
        await LoadAsync(BuildAsync);
    }

    [RelayCommand]
    private async Task OpenFilter()
    {
        var result = await _navigation.NavigateToWithResultAsync<EventFilterSheet, FilterRequest, EventFilter>(
            new FilterRequest(_filter, _all, _quickMatches, _me, _clock.Now));

        if (result is { IsSuccess: true, Value: { } filter })
        {
            // The query belongs to the page's search box, not to the sheet; the sheet must not
            // clear what is typed there by returning a filter that never carried it.
            _filter = filter with { Query = Query.Trim() };
            _districts.Save(_filter.Districts);
            ShowFilterAction();
            await LoadAsync(BuildAsync);
        }
    }

    [RelayCommand]
    private async Task OpenDetails(EventCard card) =>
        await _navigation.NavigateToAsync<EventDetailsPage, CompetitionId>(card.Competition);

    [RelayCommand]
    private async Task ToggleInterest(EventCard card)
    {
        card.IsInterested = await _events.ToggleInterestAsync(card.Competition);
        _interests = await _events.GetInterestsAsync();

        if (Selected == QuickFilter.Interested)
            await BuildAsync();
    }

    [RelayCommand]
    private void ToggleMapMode() => IsMapMode = !IsMapMode;

    private async Task ReloadAsync()
    {
        // Identity and interests are local, so they load whether or not there is a connection.
        _me = await _people.GetMeAsync();

        // The chip is named after the user's own district. It used to read "Gästrikland" for
        // everybody, which was right for exactly one person and a lie for the rest.
        Chips.First(c => c.Filter == QuickFilter.District).Label =
            _me.District is { Length: > 0 } district ? district : DistrictChipLabel;

        // Where you look is a standing preference; what you searched for last week is not.
        if (_districts.Load() is { Count: > 0 } saved)
        {
            _filter = _filter with { Districts = saved };
            ShowFilterAction();
        }

        _interests = await _events.GetInterestsAsync();

        await LoadAsync(async () =>
        {
            _all = await _events.GetCompetitionsAsync();

            await BuildAsync();
        });

        if (IsOffline)
            await ShowSavedAsync();
    }

    /// <summary>
    /// Offline the list becomes the saved packages. Without this the packages exist but are
    /// unreachable — the calendar they are normally opened from needs the network.
    /// </summary>
    private async Task ShowSavedAsync()
    {
        var packages = await _offlineStore.GetAllAsync();
        var today = DateOnly.FromDateTime(_clock.Now.Date);

        Sections.Clear();

        var saved = new EventSection("Sparade offline");

        foreach (var package in packages.OrderBy(p => p.Competition.FirstStart))
        {
            var competition = package.Competition;

            saved.Append(new EventCard
            {
                Competition = competition.Id,
                Title = competition.Name,
                Date = competition.Date,
                DayLabel = Format.DayNumber(competition.Date),
                WeekdayLabel = Format.Weekday(competition.Date),
                MonthLabel = Format.MonthShort(competition.Date),
                SpokenDate = Format.RelativeDate(competition.Date, today),
                SpanLabel = LastDayOf(competition) > competition.Date
                    ? Format.DateRange(competition.Date, LastDayOf(competition))
                    : string.Empty,
                PlaceLabel = $"{competition.Organiser} · {competition.Place}",
                OrganiserLogo = competition.OrganiserLogo,
                SportLabel = Format.Sport(competition.Sport),
                DisciplineLabel = Format.Discipline(competition.Discipline),
                LevelLabel = Format.Level(competition.Level),
                LevelShape = DisciplineShape.For(competition.Level),
                DisciplineShape = DisciplineShape.For(competition.Discipline),
                DisciplineKey = competition.Discipline.ToString(),
                DistanceKm = _me is null ? null : competition.DistanceFrom(_me.Home),
                ContextLabel = "Sparad offline",
                ShowContextBadge = true,
                IsRegistered = package.MyEntryRegisteredAt is not null,
                HasGroupEntry = package.MyEntryRegisteredAt is null && package.GroupEntryRegisteredAt is not null,
            });
        }

        if (saved.Count > 0)
            Sections.Add(saved);

        HasCards = saved.Count > 0;
        IsEmpty = !HasCards;
        EmptyMessage = "Ingen anslutning, och inga sparade tävlingar. Tävlingar sparas när du är anmäld, följer dem eller markerar dig som intresserad.";
    }

    protected override void ClearEmptyState() => IsEmpty = false;

    private async Task BuildAsync()
    {
        if (_me is null)
            return;

        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);

        var entries = await _participation.GetEntriesAsync();
        var group = await _people.GetMyGroupAsync();
        var groupIds = group.Select(f => f.Person.Id).ToHashSet();

        var mine = entries.Where(e => e.Person == _me.Id).Select(e => e.Competition).ToHashSet();
        var groupEntries = entries.Where(e => groupIds.Contains(e.Person)).Select(e => e.Competition).ToHashSet();

        var preferences = _preferences.Load();

        var relevance = new RelevanceContext
        {
            Now = now,
            Home = _me.Home,
            HomeDistrict = _me.District,
            MyClass = _me.DefaultClass,
            MyEntries = mine,
            GroupEntries = groupEntries,
            Interests = _interests,
            Favourites = preferences.Favourites,
        };

        // The sports the runner does, before anything else. A standing preference and not a
        // filter: it never shows as a chip above the list, because there is nothing there for
        // them to take off — they still do not own a bike tomorrow.
        _quickMatches = _all
            .Where(c => preferences.Allows(c.Sport))
            .Where(c => PassesQuick(c, now, today, mine, groupEntries))
            .ToList();

        var candidates = _quickMatches
            .Where(c => _filter.Includes(c, _me, now))
            .ToList();

        // Group first, then order: a recurring series must occupy one slot, not six.
        var groups = EventGrouper.Group(candidates);

        // This tab is for finding a competition to go to. What has been run lives under its own
        // chip and in Resultat — mixing the two put the summer that was at the top of the list.
        bool past = Selected == QuickFilter.Past;

        groups = groups.Where(g => EventTimeline.IsPast(g, today) == past).ToList();

        var ordered = Selected switch
        {
            // Newest first: the race someone is looking back at is the one just run.
            QuickFilter.Past => groups.OrderByDescending(g => g.LastDate).ToList(),
            QuickFilter.ForYou => groups
                .OrderByDescending(g => g.Occurrences.Max(c => RelevanceEngine.Ranking(c, relevance)))
                .ThenBy(g => EventTimeline.SortDate(g, today))
                .ToList(),
            _ => groups.OrderBy(g => EventTimeline.SortDate(g, today)).ThenBy(g => g.Title).ToList(),
        };

        // "För dig" is ranked, not dated, so headings would fight the order it is in. Everything
        // else reads as a calendar and gets the dates to navigate by.
        //
        // Built complete before anything reaches Sections: a UICollectionView that is told about
        // an empty section and then has rows appended to the plain list behind it counts wrong
        // and throws. The observed collection only ever sees finished sections.
        var built = new List<EventSection>();

        foreach (var eventGroup in ordered)
        {
            var card = await BuildCardAsync(eventGroup, today, mine, groupEntries, _me);

            string name = Selected == QuickFilter.ForYou
                ? "Mest relevant"
                : EventTimeline.NameFor(eventGroup, today);

            if (built.Count == 0 || built[^1].Name != name)
                built.Add(new EventSection(name));

            built[^1].Append(card);
        }

        Sections.Clear();

        foreach (var section in built)
            Sections.Add(section);

        IsEmpty = Sections.Count == 0;
        HasCards = !IsEmpty;
        EmptyMessage = EmptyMessageFor(Selected);
    }

    private bool PassesQuick(
        Competition competition,
        DateTimeOffset now,
        DateOnly today,
        IReadOnlySet<CompetitionId> mine,
        IReadOnlySet<CompetitionId> groupEntries) => Selected switch
        {
            // Relevance does not know what time it is, so the filter does: a competition that
            // has already been decided cannot be the most relevant thing on the list.
            QuickFilter.ForYou => true,
            QuickFilter.Past => true,
            // An arena with no position cannot be claimed to be nearby.
            QuickFilter.Near => competition.DistanceFrom(_me!.Home) is { } km && km <= 60,
            QuickFilter.District => competition.District == _me!.District,
            QuickFilter.Bigger => competition.Level <= CompetitionLevel.National,
            QuickFilter.ThisWeek => competition.Date >= today && competition.Date <= today.AddDays(7),
            QuickFilter.Mine => mine.Contains(competition.Id) || groupEntries.Contains(competition.Id),
            QuickFilter.Interested => _interests.Contains(competition.Id),
            _ => true,
        };

    /// <summary>
    /// Arrangemangets sista dag som läsaren räknar den. En målgång i småtimmarna hör till
    /// kvällen innan — en kvällstävling som stänger målet 00:30 är en endagstävling — så sex
    /// timmar dras av innan dagen läses av. En final som avgörs 16:00 nästa dag står kvar
    /// som sin egen dag.
    /// </summary>
    private static DateOnly LastDayOf(Competition competition)
    {
        var lastDay = DateOnly.FromDateTime(competition.LastFinish.DateTime.AddHours(-6).Date);
        return lastDay > competition.Date ? lastDay : competition.Date;
    }

    private static DateOnly LastDayOf(EventGroup eventGroup)
    {
        var last = eventGroup.LastDate;
        foreach (var competition in eventGroup.Occurrences)
        {
            var day = LastDayOf(competition);
            if (day > last)
                last = day;
        }
        return last;
    }

    private async Task<EventCard> BuildCardAsync(
        EventGroup eventGroup,
        DateOnly today,
        IReadOnlySet<CompetitionId> mine,
        IReadOnlySet<CompetitionId> groupEntries,
        Person me)
    {
        var primary = eventGroup.First;
        var decision = await _context.EvaluateAsync(primary);
        double? distance = primary.DistanceFrom(me.Home);

        return new EventCard
        {
            Competition = primary.Id,
            Title = eventGroup.Title,
            Date = eventGroup.FirstDate,
            DayLabel = Format.DayNumber(eventGroup.FirstDate),
            WeekdayLabel = Format.Weekday(eventGroup.FirstDate),
            MonthLabel = Format.MonthShort(eventGroup.FirstDate),
            SpokenDate = Format.RelativeDate(eventGroup.FirstDate, today),
            // Ett arrangemang som spänner flera dagar visas med sitt spann: SM-medelns kval
            // går ena dagen och finalen nästa, och en rad som säger "idag" om helgens
            // huvudlopp lurar läsaren (upptäckt i skarptestet). Datumkolumnen bär en dag —
            // spannet står i ord där undantagen redan står.
            SpanLabel = eventGroup.IsRecurring
                ? $"{eventGroup.Occurrences.Count} tillfällen"
                : LastDayOf(eventGroup) > eventGroup.FirstDate
                    ? Format.DateRange(eventGroup.FirstDate, LastDayOf(eventGroup))
                    : string.Empty,
            PlaceLabel = $"{eventGroup.Organiser} · {eventGroup.Place}",
            OrganiserLogo = primary.OrganiserLogo,
            SportLabel = Format.Sport(eventGroup.Sport),
            DisciplineLabel = Format.Discipline(eventGroup.Discipline),
            LevelLabel = Format.Level(eventGroup.Level),
            LevelShape = DisciplineShape.For(eventGroup.Level),
            DisciplineShape = DisciplineShape.For(eventGroup.Discipline),
            DisciplineKey = eventGroup.Discipline.ToString(),
            DistanceKm = distance,
            ContextLabel = decision.StateText,
            ShowContextBadge = decision.State is not (ContextState.Live or ContextState.Registered),
            IsLive = decision.State == ContextState.Live,
            IsRegistered = eventGroup.Occurrences.Any(c => mine.Contains(c.Id)),
            HasGroupEntry = eventGroup.Occurrences.Any(c => groupEntries.Contains(c.Id))
                            && !eventGroup.Occurrences.Any(c => mine.Contains(c.Id)),
            IsInterested = _interests.Contains(primary.Id),
        };
    }

    private static string EmptyMessageFor(QuickFilter filter) => filter switch
    {
        QuickFilter.Mine => "Du är inte anmäld till något just nu.",
        QuickFilter.Interested => "Inga tävlingar du markerat som intresserad. Tryck på stjärnan i listan.",
        QuickFilter.ThisWeek => "Inget den här veckan. Prova Större eller För dig.",
        QuickFilter.Near => "Inget i närheten. Vidga sökningen i filtret.",
        QuickFilter.Past => "Inga tidigare tävlingar i kalenderfönstret.",
        _ => "Inga tävlingar matchar filtret.",
    };
}
