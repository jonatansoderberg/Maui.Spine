using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Offline;
using Orientera.Services.Sources;

namespace Orientera.Features.Results;

public sealed record MyResultRow
{
    public required CompetitionId Competition { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    /// <summary>
    /// The distance, as a word, a one-letter mark and a colour.
    /// </summary>
    /// <remarks>
    /// All three, not one of them. The colour makes the list scannable, the letter says the same
    /// thing for anyone who cannot rely on the colour, and the word is what a screen reader has to
    /// read out — a coloured dot on its own is a dot.
    /// </remarks>
    public required string DisciplineText { get; init; }

    /// <summary>The drawn mark, and the name the style picks its colour by.</summary>
    public required Geometry? DisciplineShape { get; init; }

    public required string DisciplineKey { get; init; }

    public required bool HasDiscipline { get; init; }
    public required string PlaceText { get; init; }
    public required string TimeText { get; init; }
    public required string BehindText { get; init; }
    public required bool HasSplits { get; init; }
    public required bool IsPreliminary { get; init; }
    public required string Accessibility { get; init; }
}

public partial class ResultsPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation) : OrienteraViewModel
{
    public ObservableCollection<MyResultRow> Results { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial bool HasResults { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await LoadAsync(BuildAsync);

        if (IsOffline)
        {
            Results.Clear();
            HasResults = false;
            IsEmpty = true;
        }
    }

    protected override void ClearEmptyState() => IsEmpty = false;

    private async Task BuildAsync()
    {
        var me = await _people.GetMeAsync();
        var competitions = await _events.GetCompetitionsAsync();
        var results = await _participation.GetResultsForPersonAsync(me.Id);

        Results.Clear();

        // The calendar the app holds is a window a few months wide. A result from April is outside
        // it, so the competition it belongs to is not in the list — and with it went the distance:
        // "Hittahem #2 2026" is a sprint in Eventor, and the app showed no mark at all because its
        // name does not contain the word. Fetched one at a time, and only for the ones missing.
        var fetched = new Dictionary<CompetitionId, Competition?>();

        foreach (var result in results)
        {
            var competition = competitions.FirstOrDefault(c => c.Id == result.Competition);

            if (competition is null && !fetched.TryGetValue(result.Competition, out competition))
                fetched[result.Competition] = competition = await Fetch(result.Competition);

            // The result's own name and date win, because a multi-day event is one id and many
            // races: O-Ringen's five stages all carry eventId 50594, and taking the calendar's
            // answer gave five identical rows called "O-Ringen Göteborg" on the same day. Eventor's
            // own results page names them one at a time — "etapp 3, medel" — and that is the race.
            string? name = result.CompetitionName ?? competition?.Name;
            var date = result.CompetitionDate ?? competition?.Date;

            if (name is null || date is null)
                continue;

            // The race's own name first, the calendar only when it is describing this race and
            // not the week it sat in. A container that calls four middle-distance stages "Lång"
            // is worse than silence, and the stage's own name says "medel" outright.
            var discipline = result.CompetitionDiscipline
                ?? (competition is not null && competition.Date == date ? competition.Discipline : null);

            Results.Add(new MyResultRow
            {
                Competition = result.Competition,
                Name = name,
                Meta = $"{date:d MMM} · {result.Class}",
                DisciplineText = discipline is { } d ? Format.Discipline(d) : string.Empty,
                DisciplineShape = DisciplineShape.For(discipline),
                DisciplineKey = discipline?.ToString() ?? string.Empty,
                HasDiscipline = discipline is not null,
                PlaceText = Format.Place(result.Place),
                TimeText = Format.Time(result.Time),
                BehindText = result.BehindWinner is { } behind ? Format.Delta(behind) : string.Empty,
                HasSplits = result.Splits.Count > 0,
                IsPreliminary = result.Status == ResultStatus.Preliminary,
                Accessibility = string.Join(", ",
                    new[]
                    {
                        name,
                        $"{date:d MMMM}",
                        $"klass {result.Class}",
                        discipline is { } spoken ? Format.Discipline(spoken) : string.Empty,
                        Format.SpokenPlace(result.Place),
                        Format.SpokenTime(result.Time),
                        Format.SpokenDelta(result.BehindWinner),
                        result.Splits.Count > 0 ? "sträcktider finns" : string.Empty,
                    }.Where(part => part.Length > 0)),
            });
        }

        HasResults = Results.Count > 0;
        IsEmpty = !HasResults;

        // A single missing competition is a missing symbol, never a failed page.
        async Task<Competition?> Fetch(CompetitionId id)
        {
            try
            {
                return await _events.GetCompetitionAsync(id);
            }
            catch (SourceUnavailableException)
            {
                return null;
            }
        }
    }

    [RelayCommand]
    private async Task OpenResult(MyResultRow row) =>
        await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(row.Competition);
}
