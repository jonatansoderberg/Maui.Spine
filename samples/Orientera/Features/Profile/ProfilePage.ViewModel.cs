using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Features.Dev;
using Orientera.Presentation;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Profile;

public sealed record RankingRow
{
    public required string Name { get; init; }
    public required string DateText { get; init; }
    public required string PointsText { get; init; }
    public required bool IsCounting { get; init; }
    public required bool ExpiresSoon { get; init; }
    public required string ExpiryText { get; init; }
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
    IEventSource _events) : ViewModelBase
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Meta { get; set; } = string.Empty;
    [ObservableProperty] public partial string Initials { get; set; } = string.Empty;

    // ---- Sverigelistan ----
    [ObservableProperty] public partial string PointsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PointsSpoken { get; set; } = string.Empty;
    [ObservableProperty] public partial string NationalPlaceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TrendText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsImproving { get; set; }
    [ObservableProperty] public partial string DisciplineText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ExpiryWarning { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasExpiryWarning { get; set; }
    [ObservableProperty] public partial bool HasRanking { get; set; }

    // ---- serie ----
    [ObservableProperty] public partial string SeriesName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SeriesStandingText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSeries { get; set; }

    public ObservableCollection<RankingRow> CountingResults { get; } = [];
    public ObservableCollection<GroupMemberRow> Group { get; } = [];
    public ObservableCollection<SeriesRow> SeriesRounds { get; } = [];

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Tid", command: OpenTimeMachineCommand));

        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenFollowRunner()
    {
        await _navigation.NavigateToAsync<FollowRunnerSheet>();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Unfollow(GroupMemberRow row)
    {
        await _people.UnfollowAsync(row.Person);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenTimeMachine()
    {
        await _navigation.NavigateToAsync<TimeMachineSheet>();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenDesignSystem() => await _navigation.NavigateToAsync<DesignSystemPage>();

    private async Task LoadAsync()
    {
        var me = await _people.GetMeAsync();
        var today = DateOnly.FromDateTime(_clock.Now.Date);

        Name = me.Name;
        Initials = me.Initials;
        Meta = $"{me.Club} · {me.District} · {me.DefaultClass}";

        await LoadRankingAsync(me, today);
        await LoadGroupAsync();
        await LoadSeriesAsync(me, today);
    }

    private async Task LoadRankingAsync(Person me, DateOnly today)
    {
        var ranking = await _progress.GetRankingAsync(me.Id);
        HasRanking = ranking is not null;

        if (ranking is null)
            return;

        PointsText = ranking.Points.ToString("N0");
        PointsSpoken = $"{ranking.Points} poäng, {ranking.NationalPlace}:e plats i Sverige, "
                     + $"{(ranking.Trend >= 0 ? "upp" : "ner")} {Math.Abs(ranking.Trend)} poäng";
        NationalPlaceText = $"{ranking.NationalPlace}:e i Sverige";
        IsImproving = ranking.Trend >= 0;
        TrendText = ranking.Trend >= 0 ? $"+{ranking.Trend} p" : $"{ranking.Trend} p";

        DisciplineText = string.Join("  ·  ", ranking.DisciplinePoints
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{Format.Discipline(kv.Key)} {kv.Value:N0}"));

        CountingResults.Clear();

        foreach (var result in ranking.Results.OrderByDescending(r => r.Points))
        {
            CountingResults.Add(new RankingRow
            {
                Name = result.CompetitionName,
                DateText = result.Date.ToString("d MMM yyyy"),
                PointsText = result.Points.ToString("N0"),
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
            1 => $"Ett räknande resultat faller ur {expiring[0].ExpiresOn:d MMM}.",
            _ => $"{expiring.Count} räknande resultat faller ur inom kort.",
        };
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
