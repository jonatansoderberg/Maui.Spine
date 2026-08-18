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

    /// <summary>
    /// Whether the gap to the winner is worth marking. Everything below it stays neutral.
    /// </summary>
    /// <remarks>
    /// Red on every difference — including +0:55 for a second place — meant red only ever said
    /// "not the winner", which the reader already knew from the placing beside it. A mark that
    /// applies to all but one row carries nothing.
    /// <para>
    /// The boundary is a tenth of the winner's time: it scales, which an absolute number cannot —
    /// a minute is a rout over a sprint and a decent run over a long distance. Ten per cent is a
    /// chosen line rather than a measured one, and it is one number in one place if it moves.
    /// </para>
    /// </remarks>
    public required bool HasMaterialGap { get; init; }

    /// <summary>A gap that is there but not worth a colour.</summary>
    public bool HasNeutralGap => BehindText.Length > 0 && !HasMaterialGap;
    public required bool HasSplits { get; init; }
    public required bool IsPreliminary { get; init; }
    public required string Accessibility { get; init; }

    /// <summary>The season the result belongs to, for the list's headings.</summary>
    public required int Year { get; init; }
}

/// <summary>
/// One season of results, with what the season came to as its heading.
/// </summary>
/// <remarks>
/// The list ran from this August straight back through every year without a break, so nothing
/// said where one season ended and the next began — and a season is the unit a runner thinks in.
/// </remarks>
public sealed class ResultSeason(int year, IEnumerable<MyResultRow> results) : List<MyResultRow>(results)
{
    public int Year { get; } = year;

    public string Heading => Year > 0 ? Year.ToString(Format.Culture) : "Utan datum";

    public string Summary => Count == 1 ? "1 tävling" : $"{Count} tävlingar";
}

public partial class ResultsPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation) : OrienteraViewModel
{
    public ObservableCollection<MyResultRow> Results { get; } = [];

    /// <summary>The same results, under the season they were run in.</summary>
    public ObservableCollection<ResultSeason> Seasons { get; } = [];

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
                Meta = $"{date:d MMM} · {Format.ClassOrCourse(result.Class)}",
                DisciplineText = discipline is { } d ? Format.Discipline(d) : string.Empty,
                DisciplineShape = DisciplineShape.For(discipline),
                DisciplineKey = discipline?.ToString() ?? string.Empty,
                HasDiscipline = discipline is not null,
                PlaceText = Format.Place(result.Place),
                TimeText = Format.Time(result.Time),
                BehindText = result.BehindWinner is { } behind ? Format.Delta(behind) : string.Empty,
                HasMaterialGap = result.BehindWinner is { } gap
                                 && gap > TimeSpan.Zero
                                 && result.Time - gap is { Ticks: > 0 } winner
                                 && gap.TotalSeconds >= winner.TotalSeconds * 0.10,
                HasSplits = result.Splits.Count > 0,
                IsPreliminary = result.Status == ResultStatus.Preliminary,
                // A result with no date of its own and no competition to borrow one from cannot
                // be placed in a season; it lands under 0, which sorts last and says as much.
                Year = date?.Year ?? 0,
                Accessibility = string.Join(", ",
                    new[]
                    {
                        name,
                        $"{date:d MMMM}",
                        Format.IsAgeClass(result.Class) ? $"klass {result.Class}" : $"bana {result.Class}",
                        discipline is { } spoken ? Format.Discipline(spoken) : string.Empty,
                        Format.SpokenPlace(result.Place),
                        Format.SpokenTime(result.Time),
                        Format.SpokenDelta(result.BehindWinner),
                        result.Splits.Count > 0 ? "sträcktider finns" : string.Empty,
                    }.Where(part => part.Length > 0)),
            });
        }

        Seasons.Clear();

        // The order the results already have is the order inside a season; grouping must not
        // resort them.
        foreach (var season in Results.GroupBy(r => r.Year).OrderByDescending(g => g.Key))
            Seasons.Add(new ResultSeason(season.Key, season));

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
