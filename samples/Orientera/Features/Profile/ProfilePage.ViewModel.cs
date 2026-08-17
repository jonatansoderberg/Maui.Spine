using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;
using Orientera.Features.Dev;
using Orientera.Presentation;
using Orientera.Services.Eventor;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Profile;

public sealed record RankingRow
{
    public required string Name { get; init; }

    /// <summary>The distance the race's own name states, drawn. Null when it states none.</summary>
    public Geometry? DisciplineShape { get; init; }

    public string DisciplineKey { get; init; } = string.Empty;

    public bool HasDisciplineShape => DisciplineShape is not null;
    public required string DateText { get; init; }
    public required string PointsText { get; init; }
    public required bool IsCounting { get; init; }
    public required bool ExpiresSoon { get; init; }
    public required string ExpiryText { get; init; }
}

public sealed record ActivityRow
{
    public required string Name { get; init; }
    public required string Meta { get; init; }
    public required string DeadlineText { get; init; }
    public required bool ClosesSoon { get; init; }
    public required string Url { get; init; }

    public string OpenDescription => $"Öppna {Name} i Eventor";
}

public sealed record GroupMemberRow
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    public string UnfollowDescription => $"Sluta följa {Name}";
}

public sealed record SeriesRow
{
    public required string Name { get; init; }
    public required string DateText { get; init; }
    public required string PointsText { get; init; }
    public required string PlaceText { get; init; }
    public required bool IsCounting { get; init; }
    public required bool IsPending { get; init; }
}

public partial class ProfilePageViewModel(
    INavigationService _navigation,
    IClock _clock,
    IPeopleSource _people,
    IProgressSource _progress,
    IEventSource _events,
    IClubActivitySource _activities,
    EventorSessionStore _eventorSessions,
    EventorReader _eventor,
    DataSourceInfo _source) : OrienteraViewModel
{
    /// <summary>Which data source this run is against — a demo must not read as live data.</summary>
    public string SourceDescription => _source.Description;

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Meta { get; set; } = string.Empty;
    [ObservableProperty] public partial string Initials { get; set; } = string.Empty;
    [ObservableProperty] public partial string ClassText { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user may rewrite their own name and club.
    /// </summary>
    /// <remarks>
    /// Only while nobody owns the answer. Logged in, Eventor does, and the next fetch would
    /// overwrite whatever was typed here — two truths about the same thing are worse than one.
    /// The class stays editable either way: people enter classes other than the one they are
    /// ranked in.
    /// </remarks>
    [ObservableProperty] public partial bool CanEditIdentity { get; set; } = true;

    // ---- Sverigelistan ----
    [ObservableProperty] public partial string PointsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PointsSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string PlacesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TrendText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsImproving { get; set; }
    [ObservableProperty] public partial string DisciplineText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExpiryWarning { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasExpiryWarning { get; set; }
    [ObservableProperty] public partial bool HasRanking { get; set; }

    /// <summary>Why there is no Sverigelistan to show, when there is not.</summary>
    [ObservableProperty] public partial string RankingExplanation { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasRankingExplanation { get; set; }

    // ---- serie ----
    [ObservableProperty] public partial string SeriesName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SeriesStandingText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSeries { get; set; }

    // ---- Eventor-inloggning ----
    [ObservableProperty] public partial string EventorStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string EventorAction { get; set; } = "Logga in på Eventor";

    // ---- klubbaktiviteter ----
    [ObservableProperty] public partial bool HasActivities { get; set; }

    public ObservableCollection<ActivityRow> Activities { get; } = [];

    public ObservableCollection<RankingRow> CountingResults { get; } = [];
    public ObservableCollection<GroupMemberRow> Group { get; } = [];
    public ObservableCollection<SeriesRow> SeriesRounds { get; } = [];

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Tid", command: OpenTimeMachineCommand));

        await ReloadAsync();
    }

    /// <summary>
    /// Sverigelistan läses med användarens egen inloggning, så var och en ser det hen betalar för.
    /// Utan inloggning uteblir rankingen — den lånas inte av någon annan (#123).
    /// </summary>
    [RelayCommand]
    private async Task OpenEventorLogin()
    {
        await _navigation.NavigateToWithResultAsync<EventorLoginSheet, EventorWebSession>();
        await ReloadAsync();
    }

    /// <summary>
    /// Den andra vägen in: appens egna fält i stället för Eventors sida.
    /// </summary>
    /// <remarks>
    /// Ligger bredvid den första med avsikt, inte i stället för. Båda skickar samma POST från
    /// samma formulär; det som skiljer är var lösenordet skrivs, och det är den skillnaden som
    /// ska utvärderas på en riktig telefon innan någon av dem tas bort.
    /// </remarks>
    [RelayCommand]
    private async Task OpenAppLogin()
    {
        await _navigation.NavigateToWithResultAsync<AppLoginSheet, EventorWebSession>();
        await ReloadAsync();
    }

    /// <summary>
    /// What the login can and cannot read, in the user's words.
    /// </summary>
    /// <remarks>
    /// Four states rather than two. A session Eventor has forgotten looks exactly like never having
    /// logged in, but "logga in igen" and "logga in" are different requests; and a club without the
    /// fee is not a failure at all, so it must not be phrased as one. Being offline says nothing
    /// about any of it, and so says nothing.
    /// </remarks>
    private async Task ShowEventorStatusAsync()
    {
        var session = _eventorSessions.Load();
        var access = await _eventor.AccessAsync();

        var who = session?.Account is { } account
            ? $"Inloggad som {account.Name}, {account.Club}"
            : "Inloggad";

        EventorStatus = access switch
        {
            EventorAccess.NoSession => "Inte inloggad. Sverigelistan och klubbens aktiviteter läses med din egen inloggning.",
            EventorAccess.Expired => "Eventor känner inte längre igen inloggningen. Logga in igen så kommer listan tillbaka.",
            EventorAccess.NoSubscription => $"{who}. Klubben har inte Sverigelistan för säsongen, så ingen placering visas.",
            EventorAccess.Unreachable => $"{who}. Eventor svarar inte just nu.",
            _ => who + Validity(session),
        };

        EventorAction = access is EventorAccess.NoSession ? "Logga in på Eventor" : "Logga in igen";
        CanEditIdentity = access is EventorAccess.NoSession or EventorAccess.Expired;

        RankingExplanation = access switch
        {
            EventorAccess.NoSession => "Logga in på Eventor så visas din Sverigelistan här. Du loggar in på Eventors egen sida, och uppgifterna stannar på telefonen.",
            EventorAccess.Expired => "Inloggningen har gått ut. Logga in igen så visas din Sverigelistan här.",
            EventorAccess.NoSubscription => "Din klubb har inte betalat avgiften för Sverigelistan i år, så det finns ingen placering att visa.",
            _ => string.Empty,
        };

        HasRankingExplanation = !HasRanking && RankingExplanation.Length > 0;
    }

    /// <summary>
    /// How long the login lasts, when Eventor's own cookies say. Only theirs are counted: the
    /// advertising cookies on the same pages live for years and once had the app promising a
    /// login valid until 2027 (#123).
    /// </summary>
    private static string Validity(EventorWebSession? session) =>
        session?.ExpiresAt is { } expires ? $" · giltig till {expires:d MMM yyyy}" : string.Empty;

    [RelayCommand]
    private async Task OpenNotifications() => await _navigation.NavigateToAsync<NotificationSheet>();

    [RelayCommand]
    private async Task OpenIdentity()
    {
        await _navigation.NavigateToAsync<IdentitySheet>();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenFollowRunner()
    {
        await _navigation.NavigateToAsync<FollowRunnerSheet>();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task Unfollow(GroupMemberRow row)
    {
        await _people.UnfollowAsync(row.Person);
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenTimeMachine()
    {
        await _navigation.NavigateToAsync<TimeMachineSheet>();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenDesignSystem() => await _navigation.NavigateToAsync<DesignSystemPage>();

    /// <summary>
    /// Who I am and who I follow are local, so they load unconditionally. Sverigelistan and the
    /// series come from the network and are guarded separately — an outage should cost those
    /// two cards, not the whole page.
    /// </summary>
    private async Task ReloadAsync()
    {
        var me = await _people.GetMeAsync();
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);

        Name = me.Name;
        Initials = me.Initials;

        // The class has a row of its own, so it is not repeated here.
        Meta = string.Join(" · ", new[] { me.Club, me.District }.Where(part => part.Length > 0));
        ClassText = me.DefaultClass is { Length: > 0 } ownClass ? ownClass : "Ingen klass vald";

        await LoadGroupAsync();

        await LoadAsync(async () =>
        {
            await LoadRankingAsync(me, today);
            await LoadSeriesAsync(me, today);
            await LoadActivitiesAsync(now);
        });

        if (IsOffline)
        {
            HasRanking = false;
            HasSeries = false;
            HasActivities = false;
        }

        // Last, because what it says depends on whether the ranking came through.
        await ShowEventorStatusAsync();
    }

    /// <summary>
    /// The club's activity list, soonest deadline first. Only what is still ahead: a relay that
    /// closed in April is not something to act on, and the list is long enough without it.
    /// </summary>
    private async Task LoadActivitiesAsync(DateTimeOffset now)
    {
        var activities = await _activities.GetClubActivitiesAsync();

        Activities.Clear();

        var upcoming = activities
            .Where(a => a.EntryDeadline is null || a.EntryDeadline > now)
            .OrderBy(a => a.EntryDeadline ?? DateTimeOffset.MaxValue);

        var today = DateOnly.FromDateTime(now.Date);

        // "ons 10 feb." reads as this February. Relay sign-ups close a year and a half ahead —
        // 10-mila 2027 closes in February 2027 — so a date in another year says which.
        string When(DateTimeOffset moment)
        {
            var date = DateOnly.FromDateTime(moment.Date);
            var relative = Format.RelativeDate(date, today);

            return date.Year == today.Year ? relative : $"{relative} {date.Year}";
        }

        foreach (var activity in upcoming)
        {
            var deadline = activity.EntryDeadline;

            Activities.Add(new ActivityRow
            {
                Name = WithoutOrganisation(activity),
                Meta = activity.StartsAt is { } start
                    ? $"{activity.Organisation} · {When(start)}"
                    : activity.Organisation,
                DeadlineText = deadline is null
                    ? $"{activity.EntryCount} anmälda"
                    : $"{activity.EntryCount} anmälda · anmälan stänger {When(deadline.Value)}",
                ClosesSoon = activity.IsOpen && deadline is { } close && close - now <= TimeSpan.FromDays(7),
                Url = activity.Url,
            });
        }

        HasActivities = Activities.Count > 0;
    }

    /// <summary>
    /// Eventor names a district's activities "Träningsdag inför USM (Gästriklands OF)". The row
    /// says whose it is on its own line, so the parenthesis is the same word twice.
    /// </summary>
    private static string WithoutOrganisation(ClubActivity activity) =>
        activity.Name.EndsWith($"({activity.Organisation})", StringComparison.Ordinal)
            ? activity.Name[..^$"({activity.Organisation})".Length].TrimEnd()
            : activity.Name;

    [RelayCommand]
    private async Task OpenActivity(ActivityRow row) =>
        await Launcher.Default.OpenAsync(row.Url);

    private async Task LoadRankingAsync(Person me, DateOnly today)
    {
        var ranking = await _progress.GetRankingAsync(me.Id);
        HasRanking = ranking is not null;

        if (ranking is null)
            return;

        // Two decimals, as Sverigelistan publishes them: places are separated by hundredths.
        PointsText = ranking.Points.ToString("N2", Format.Culture);
        PointsSpoken = $"{ranking.Points} poäng, {ranking.NationalPlace}:e plats i Sverige, "
                     + $"{(ranking.Trend >= 0 ? "upp" : "ner")} {Math.Abs(ranking.Trend)} poäng";
        PlacesText = string.Join(" · ", Places(ranking));
        IsImproving = ranking.Trend >= 0;
        TrendText = ranking.Trend >= 0 ? $"+{ranking.Trend} p" : $"{ranking.Trend} p";

        DisciplineText = string.Join("  ·  ", ranking.DisciplinePoints
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{Format.Discipline(kv.Key)} {kv.Value.ToString("N2", Format.Culture)}"));

        CountingResults.Clear();

        // The six that make up the average, newest first — not every result the runner has, and
        // not sorted by points. Sverigelistan counts downwards: a lower figure is a better race,
        // so ordering by points descending put the worst results at the top of a list headed
        // "resultat i snittet", where they were not in the average at all.
        foreach (var result in ranking.Counting.OrderByDescending(r => r.Date))
        {
            var discipline = DisciplineNames.In(result.CompetitionName);

            CountingResults.Add(new RankingRow
            {
                Name = result.CompetitionName,
                DisciplineShape = DisciplineShape.For(discipline),
                DisciplineKey = discipline?.ToString() ?? string.Empty,
                DateText = result.Date.ToString("d MMM yyyy"),
                PointsText = result.Points.ToString("N2", Format.Culture),
                IsCounting = result.IsCounting,
                ExpiresSoon = result.ExpiresSoon(today),
                ExpiryText = result.ExpiresSoon(today)
                    ? $"faller ur {result.ExpiresOn:d MMM}"
                    : string.Empty,
            });
        }

        var expiring = ranking.Results.Where(r => r.ExpiresSoon(today)).ToList();
        HasExpiryWarning = expiring.Count > 0;
        ExpiryWarning = expiring.Count switch
        {
            0 => string.Empty,
            1 => $"Ett räknande resultat faller ur {Format.DateInSentence(expiring[0].ExpiresOn)}.",
            _ => $"{expiring.Count} räknande resultat faller ur inom kort.",
        };
    }

    /// <summary>
    /// The same average read three ways: against the country, against the runner's own class, and
    /// against their club. A place the source did not carry is left out rather than filled in.
    /// </summary>
    private static IEnumerable<string> Places(RankingSnapshot ranking)
    {
        yield return $"{Format.Place(ranking.NationalPlace)} i Sverige";

        if (ranking.Class is { } ownClass)
            yield return $"{Format.Place(ownClass.Place)} i {ownClass.Class}";

        if (ranking.Club is { } club)
        {
            // The club page ranks women and men separately, so the number means half a club.
            yield return club.Section is { } section
                ? $"{Format.Place(club.Place)} i {club.Club}, {Format.Section(section)}"
                : $"{Format.Place(club.Place)} i {club.Club}";
        }
    }

    private async Task LoadGroupAsync()
    {
        var group = await _people.GetMyGroupAsync();

        Group.Clear();

        foreach (var followed in group)
        {
            Group.Add(new GroupMemberRow
            {
                Person = followed.Person.Id,
                Name = followed.Person.Name,
                Meta = $"{Format.FollowReason(followed.Reason)} · {followed.Person.Club} · {followed.Person.DefaultClass}",
            });
        }
    }

    private async Task LoadSeriesAsync(Person me, DateOnly today)
    {
        var standings = await _progress.GetSeriesStandingsAsync(me.Id);
        var standing = standings.FirstOrDefault();

        HasSeries = standing is not null;

        if (standing is null)
            return;

        var series = await _events.GetSeriesAsync(standing.Series);
        SeriesName = series?.Name ?? "Serie";
        SeriesStandingText = $"{Format.Place(standing.Place)} · {standing.TotalPoints} p";

        SeriesRounds.Clear();

        foreach (var round in standing.Rounds)
        {
            SeriesRounds.Add(new SeriesRow
            {
                Name = round.CompetitionName,
                DateText = Format.RelativeDate(round.Date, today),
                PointsText = round.Place is null ? "—" : round.Points.ToString(),
                PlaceText = round.Place is { } place ? Format.Place(place) : "—",
                IsCounting = round.IsCounting,
                IsPending = round.Place is null,
            });
        }
    }
}
