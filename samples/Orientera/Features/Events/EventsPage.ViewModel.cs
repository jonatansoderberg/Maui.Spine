using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Grouping;
using Orientera.Services.Offline;
using Orientera.Services.Relevance;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Events;

public sealed partial class FilterChip : ObservableObject
{
    public required QuickFilter Filter { get; init; }
    public required string Label { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public partial class EventsPageViewModel(
    INavigationService _navigation,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IOfflineStore _offlineStore,
    CompetitionContextService _context) : OrienteraViewModel
{
    private IReadOnlyList<Competition> _all = [];
    private Person? _me;
    private IReadOnlySet<CompetitionId> _favourites = new HashSet<CompetitionId>();
    private EventFilter _filter = EventFilter.Default;

    public ObservableCollection<FilterChip> Chips { get; } =
    [
        new() { Filter = QuickFilter.ForYou, Label = "För dig", IsSelected = true },
        new() { Filter = QuickFilter.Near, Label = "Nära" },
        new() { Filter = QuickFilter.District, Label = "Gästrikland" },
        new() { Filter = QuickFilter.Bigger, Label = "Större" },
        new() { Filter = QuickFilter.ThisWeek, Label = "Denna vecka" },
        new() { Filter = QuickFilter.Mine, Label = "Mina" },
        new() { Filter = QuickFilter.Favourites, Label = "Favoriter" },
    ];

    public ObservableCollection<EventCard> Cards { get; } = [];

    [ObservableProperty]
    public partial string EmptyMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasCards { get; set; }

    [ObservableProperty]
    public partial string FilterLabel { get; set; } = "Filter";

    /// <summary>Map discovery is an M4 feature; M0 shows the placeholder rather than pretending.</summary>
    [ObservableProperty]
    public partial bool IsMapMode { get; set; }

    private QuickFilter Selected => Chips.First(c => c.IsSelected).Filter;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Filter", command: OpenFilterCommand));

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

    [RelayCommand]
    private async Task OpenFilter()
    {
        var result = await _navigation.NavigateToWithResultAsync<EventFilterSheet, EventFilter>();

        if (result is { IsSuccess: true, Value: { } filter })
        {
            _filter = filter;
            FilterLabel = filter.IsActive ? $"Filter ({filter.ActiveCount})" : "Filter";
            await LoadAsync(BuildAsync);
        }
    }

    [RelayCommand]
    private async Task OpenDetails(EventCard card) =>
        await _navigation.NavigateToAsync<EventDetailsPage, CompetitionId>(card.Competition);

    [RelayCommand]
    private async Task ToggleFavourite(EventCard card)
    {
        card.IsFavourite = await _events.ToggleFavouriteAsync(card.Competition);
        _favourites = await _events.GetFavouritesAsync();

        if (Selected == QuickFilter.Favourites)
            await BuildAsync();
    }

    [RelayCommand]
    private void ToggleMapMode() => IsMapMode = !IsMapMode;

    private async Task ReloadAsync()
    {
        // Identity and favourites are local, so they load whether or not there is a connection.
        _me = await _people.GetMeAsync();
        _favourites = await _events.GetFavouritesAsync();

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

        Cards.Clear();

        foreach (var package in packages.OrderBy(p => p.Competition.FirstStart))
        {
            var competition = package.Competition;

            Cards.Add(new EventCard
            {
                Competition = competition.Id,
                Title = competition.Name,
                DateLabel = Format.RelativeDate(competition.Date, today),
                PlaceLabel = $"{competition.Organiser} · {competition.Place}",
                MetaLabel = $"{Format.Discipline(competition.Discipline)} · {Format.Level(competition.Level)}",
                DistanceLabel = _me is null ? string.Empty : Format.Distance(_me.Home.DistanceKmTo(competition.Location)),
                ContextLabel = "Sparad offline",
                ShowContextBadge = true,
                IsRegistered = package.MyEntryRegisteredAt is not null,
                HasGroupEntry = package.MyEntryRegisteredAt is null && package.GroupEntryRegisteredAt is not null,
            });
        }

        HasCards = Cards.Count > 0;
        IsEmpty = !HasCards;
        EmptyMessage = "Ingen anslutning, och inga sparade tävlingar. Tävlingar sparas när du är anmäld, följer dem eller favoritmarkerar dem.";
    }

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

        var relevance = new RelevanceContext
        {
            Now = now,
            Home = _me.Home,
            HomeDistrict = _me.District,
            MyClass = _me.DefaultClass,
            MyEntries = mine,
            GroupEntries = groupEntries,
            Favourites = _favourites,
        };

        var candidates = _all
            .Where(c => PassesAdvanced(c, relevance))
            .Where(c => PassesQuick(c, now, today, mine, groupEntries))
            .ToList();

        // Group first, then order: a recurring series must occupy one slot, not six.
        var groups = EventGrouper.Group(candidates);

        var ordered = Selected == QuickFilter.ForYou
            ? groups.OrderByDescending(g => g.Occurrences.Max(c => RelevanceEngine.Score(c, relevance).Total)).ToList()
            : groups.OrderBy(g => g.FirstDate).ToList();

        Cards.Clear();

        foreach (var eventGroup in ordered)
            Cards.Add(await BuildCardAsync(eventGroup, today, mine, groupEntries, _me));

        IsEmpty = Cards.Count == 0;
        HasCards = !IsEmpty;
        EmptyMessage = EmptyMessageFor(Selected);
    }

    private bool PassesAdvanced(Competition competition, RelevanceContext relevance)
    {
        // Training and recreational events are hidden unless explicitly asked for — the spec's
        // "minska Eventor-bruset" applied at its most common source.
        if (competition.IsLowPriority && !_filter.ShowTraining)
            return false;

        if (_filter.MinimumLevel is { } level && competition.Level > level)
            return false;

        if (_filter.Discipline is { } discipline && competition.Discipline != discipline)
            return false;

        if (_filter.MaxDistanceKm is { } maxDistance
            && relevance.Home.DistanceKmTo(competition.Location) > maxDistance)
            return false;

        if (_filter.OnlyMyClass
            && competition.Classes.Count > 0
            && !competition.Classes.Contains(relevance.MyClass))
            return false;

        if (_filter.OnlyRegisterable && !IsRegisterable(competition, relevance.Now))
            return false;

        return true;
    }

    private bool PassesQuick(
        Competition competition,
        DateTimeOffset now,
        DateOnly today,
        IReadOnlySet<CompetitionId> mine,
        IReadOnlySet<CompetitionId> groupEntries) => Selected switch
        {
            QuickFilter.ForYou => true,
            QuickFilter.Near => _me!.Home.DistanceKmTo(competition.Location) <= 60,
            QuickFilter.District => competition.District == _me!.District,
            QuickFilter.Bigger => competition.Level <= CompetitionLevel.National,
            QuickFilter.ThisWeek => competition.Date >= today && competition.Date <= today.AddDays(7),
            QuickFilter.Mine => mine.Contains(competition.Id) || groupEntries.Contains(competition.Id),
            QuickFilter.Favourites => _favourites.Contains(competition.Id),
            _ => true,
        };

    private static bool IsRegisterable(Competition competition, DateTimeOffset now) =>
        competition.Schedule is { RegistrationOpensAt: { } opens, EntryDeadline: { } deadline }
        && opens <= now
        && now <= deadline;

    private async Task<EventCard> BuildCardAsync(
        EventGroup eventGroup,
        DateOnly today,
        IReadOnlySet<CompetitionId> mine,
        IReadOnlySet<CompetitionId> groupEntries,
        Person me)
    {
        var primary = eventGroup.First;
        var decision = await _context.EvaluateAsync(primary);
        double distance = me.Home.DistanceKmTo(primary.Location);

        return new EventCard
        {
            Competition = primary.Id,
            Title = eventGroup.Title,
            DateLabel = eventGroup.IsRecurring
                ? Format.DateRange(eventGroup.FirstDate, eventGroup.LastDate)
                : Format.RelativeDate(eventGroup.FirstDate, today),
            PlaceLabel = $"{eventGroup.Organiser} · {eventGroup.Place}",
            MetaLabel = $"{Format.Discipline(eventGroup.Discipline)} · {Format.Level(eventGroup.Level)}",
            DistanceLabel = Format.Distance(distance),
            OccurrenceLabel = eventGroup.IsRecurring ? $"{eventGroup.Occurrences.Count} tillfällen" : string.Empty,
            ContextLabel = decision.StateText,
            ShowContextBadge = decision.State is not (ContextState.Live or ContextState.Registered),
            IsLive = decision.State == ContextState.Live,
            IsRegistered = eventGroup.Occurrences.Any(c => mine.Contains(c.Id)),
            HasGroupEntry = eventGroup.Occurrences.Any(c => groupEntries.Contains(c.Id))
                            && !eventGroup.Occurrences.Any(c => mine.Contains(c.Id)),
            IsFavourite = _favourites.Contains(primary.Id),
        };
    }

    private static string EmptyMessageFor(QuickFilter filter) => filter switch
    {
        QuickFilter.Mine => "Du är inte anmäld till något just nu.",
        QuickFilter.Favourites => "Inga favoritmarkerade tävlingar. Tryck på stjärnan i listan.",
        QuickFilter.ThisWeek => "Inget den här veckan. Prova Större eller För dig.",
        QuickFilter.Near => "Inget i närheten. Vidga sökningen i filtret.",
        _ => "Inga tävlingar matchar filtret.",
    };
}
