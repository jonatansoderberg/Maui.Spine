using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using Orientera.Domain;
using Orientera.Features.Events.Participants;
using Orientera.Presentation;
using Orientera.Services.Offline;
using Orientera.Services.Eventor;
using Orientera.Services.Sources;

namespace Orientera.Features.Profile;

/// <summary>
/// One race in the runner's own season.
/// </summary>
/// <remarks>
/// The row goes up with what the result itself carries and fills in the rest as it arrives. The
/// distance is the late one: the calendar the app holds is a few months wide, so an older race has
/// to be asked for one at a time — and asking for all of them before drawing anything was what
/// kept the page blank while a season's worth of requests went out in turn.
/// </remarks>
public sealed partial class MyResultRow : ObservableObject
{
    public required CompetitionId Competition { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    /// <summary>The race's own date, which decides whether a competition is describing this race.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The class or course the result was run in.</summary>
    public required string Class { get; init; }

    /// <summary>
    /// The distance, as a word, a one-letter mark and a colour.
    /// </summary>
    /// <remarks>
    /// All three, not one of them. The colour makes the list scannable, the letter says the same
    /// thing for anyone who cannot rely on the colour, and the word is what a screen reader has to
    /// read out — a coloured dot on its own is a dot.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisciplineText))]
    [NotifyPropertyChangedFor(nameof(DisciplineShape))]
    [NotifyPropertyChangedFor(nameof(DisciplineKey))]
    [NotifyPropertyChangedFor(nameof(HasDiscipline))]
    [NotifyPropertyChangedFor(nameof(Accessibility))]
    public partial Discipline? Discipline { get; set; }

    public string DisciplineText => Discipline is { } d ? Format.Discipline(d) : string.Empty;

    /// <summary>The drawn mark, and the name the style picks its colour by.</summary>
    public Geometry? DisciplineShape => Orientera.Presentation.DisciplineShape.For(Discipline);

    public string DisciplineKey => Discipline?.ToString() ?? string.Empty;

    public bool HasDiscipline => Discipline is not null;

    /// <summary>
    /// The placement itself, which is how the runner's own row is picked out of a class.
    /// </summary>
    /// <remarks>
    /// A multi-day event is one competition id and many races, so a class' results carry the
    /// runner five times over. The placement is what tells the five apart.
    /// </remarks>
    public required int? Place { get; init; }

    /// <summary>The placement, as a number and nothing else.</summary>
    public required string PlaceText { get; init; }

    /// <summary>Gold, silver or bronze for a podium place; empty for the rest of the field.</summary>
    public required string MedalText { get; init; }

    public bool HasMedal => MedalText.Length > 0;

    /// <summary>
    /// Whether the placement is written out. A medal already says which of the three it was.
    /// </summary>
    public bool HasPlaceNumber => !HasMedal;

    /// <summary>
    /// "av 91" — the field the placement is out of, once its size is known.
    /// </summary>
    /// <remarks>
    /// Empty rather than "av 0" when it is not: a placement without a field is still the whole
    /// fact the row exists to state, and a fabricated denominator would be worse than silence.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasField))]
    [NotifyPropertyChangedFor(nameof(Accessibility))]
    public partial string FieldText { get; set; } = string.Empty;

    public bool HasField => FieldText.Length > 0;

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

    /// <summary>The race, spoken: its name, its date and the class it was run in.</summary>
    public required string SpokenRace { get; init; }

    /// <summary>The placement, spoken. "3:e" is read as "3 e", so it is said as words.</summary>
    public required string SpokenPlace { get; init; }

    /// <summary>The time, the gap and whether there are splits — the tail of the sentence.</summary>
    public required string SpokenResult { get; init; }

    /// <summary>
    /// The whole row as one sentence, including whatever has filled in since it went up.
    /// </summary>
    public string Accessibility => string.Join(", ",
        new[] { SpokenRace, DisciplineText, SpokenPlace, FieldText, SpokenResult }
            .Where(part => part.Length > 0));

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

public partial class MyResultsPageViewModel(
    INavigationService _navigation,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    EventorSessionResume _resume,
    EventorReader _eventor) : OrienteraViewModel
{
    /// <summary>
    /// Two at a time. One request per competition takes the better part of a second and a season
    /// of them in turn is longer than anyone waits, but four made Eventor slow enough that opening
    /// a result while the fill ran timed out on a competition that answers in a second. Background
    /// work leaves room for the tap that comes next.
    /// </summary>
    private static ParallelOptions Politely(CancellationToken cancellationToken) =>
        new() { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken };

    /// <summary>
    /// Stops the background fill when the page is left or reloaded.
    /// </summary>
    /// <remarks>
    /// The fill is a season's worth of requests and it outlived the page. Opening a result while
    /// it ran put the detail page's own request behind a queue of them and it timed out — the page
    /// said "Ingen anslutning" about a competition that answers in a quarter of a second. Work
    /// that belongs to a page stops with the page.
    /// </remarks>
    private CancellationTokenSource? _filling;

    public ObservableCollection<MyResultRow> Results { get; } = [];

    /// <summary>The same results, under the season they were run in.</summary>
    public ObservableCollection<ResultSeason> Seasons { get; } = [];

    [ObservableProperty] public partial bool IsEmpty { get; set; }

    /// <summary>
    /// The empty state's own words. The list is read with the runner's Eventor session, so when
    /// that is what is missing the page says so instead of "dina resultat dyker upp här" — which
    /// is true, and useless, and leaves them waiting for something that will never arrive.
    /// </summary>
    [ObservableProperty] public partial string EmptyHeading { get; set; } = "Inga resultat ännu";

    [ObservableProperty]
    public partial string EmptyDetail { get; set; } = "Dina resultat dyker upp här när de publicerats.";

    [ObservableProperty] public partial bool HasResults { get; set; }

    /// <summary>
    /// Skeleton rows stand in for a list that is not there yet — never on top of one that is.
    /// </summary>
    /// <remarks>
    /// A reload keeps the results on screen while it runs, so <c>IsLoading</c> alone drew the
    /// skeleton over the rows it was standing in for: two states at once, which is the thing P10
    /// exists to prevent.
    /// </remarks>
    [ObservableProperty] public partial bool ShowSkeleton { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        var session = _resume.Generation;

        // Only when there is nothing to stand in for. A reload keeps the rows on screen.
        ShowSkeleton = !HasResults;

        await LoadAsync(BuildAsync);

        ShowSkeleton = false;

        // The results are read with the runner's own Eventor session, so an expired one empties
        // this page. Reviving it here rather than only on Hem is the whole point of the service —
        // and reading again is decided by whether the session changed, not by who the login
        // happened to be triggered by (#140).
        await _resume.EnsureAsync(_navigation);

        if (_resume.Generation != session)
            await LoadAsync(BuildAsync);

        await ExplainEmptinessAsync();

        if (IsOffline)
        {
            Results.Clear();
            HasResults = false;
            IsEmpty = true;
        }
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        StopFilling();
        return base.OnDisappearingAsync(navigationDirection);
    }

    protected override void ClearEmptyState() => IsEmpty = false;

    private void StopFilling()
    {
        _filling?.Cancel();
        _filling?.Dispose();
        _filling = null;
    }

    /// <summary>
    /// Names the login as the reason when it is one. Asked only when the page has nothing to show:
    /// a full list needs no explanation, and the question costs a request to Eventor.
    /// </summary>
    private async Task ExplainEmptinessAsync()
    {
        if (!IsEmpty)
            return;

        var access = await _eventor.AccessAsync();

        if (!EventorMessage.Explains(access))
            return;

        EmptyHeading = EventorMessage.Heading(access);
        EmptyDetail = EventorMessage.Detail(access, "Dina resultat");
    }

    private async Task BuildAsync()
    {
        // A reload builds new rows; whatever the last one was still filling in belongs to rows
        // that are about to be replaced.
        StopFilling();

        var me = await _people.GetMeAsync();
        var competitions = await _events.GetCompetitionsAsync();
        var results = await _participation.GetResultsForPersonAsync(me.Id);

        // A result that carries neither a name nor a date of its own cannot be drawn at all, so
        // its competition is the one thing worth waiting for — and only for those rows, fetched
        // together rather than one after another. Everything else the competition would have
        // added is filled in afterwards, with the list already on screen.
        var borrowed = await BorrowAsync(results
            .Where(result => (result.CompetitionName is null || result.CompetitionDate is null)
                && competitions.All(competition => competition.Id != result.Competition))
            .Select(result => result.Competition));

        Results.Clear();

        var incomplete = new List<MyResultRow>();

        foreach (var result in results)
        {
            var competition = competitions.FirstOrDefault(c => c.Id == result.Competition)
                ?? borrowed.GetValueOrDefault(result.Competition);

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

            var row = new MyResultRow
            {
                Competition = result.Competition,
                Name = name,
                Meta = $"{date:d MMM} · {Format.ClassOrCourse(result.Class)}",
                Date = date.Value,
                Class = result.Class,
                Discipline = discipline,
                Place = result.Place,
                PlaceText = Format.PlaceNumber(result.Place),
                MedalText = Format.Medal(result.Place),
                FieldText = Format.OutOf(result.Starters),
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
                SpokenRace = string.Join(", ",
                    new[]
                    {
                        name,
                        $"{date:d MMMM}",
                        Format.IsAgeClass(result.Class) ? $"klass {result.Class}" : $"bana {result.Class}",
                    }.Where(part => part.Length > 0)),
                SpokenPlace = Format.SpokenPlace(result.Place),
                SpokenResult = string.Join(", ",
                    new[]
                    {
                        Format.SpokenTime(result.Time),
                        Format.SpokenDelta(result.BehindWinner),
                        result.Splits.Count > 0 ? "sträcktider finns" : string.Empty,
                    }.Where(part => part.Length > 0)),
            };

            Results.Add(row);

            if (!row.HasDiscipline || !row.HasField)
                incomplete.Add(row);
        }

        Seasons.Clear();

        // The order the results already have is the order inside a season; grouping must not
        // resort them.
        foreach (var season in Results.GroupBy(r => r.Year).OrderByDescending(g => g.Key))
            Seasons.Add(new ResultSeason(season.Key, season));

        HasResults = Results.Count > 0;
        IsEmpty = !HasResults;

        // Deliberately not awaited: the list is complete enough to read, and what is missing
        // arrives one competition at a time into rows that are already on screen.
        _filling = new CancellationTokenSource();
        _ = FillAsync(me.Id, incomplete, _filling.Token);
    }

    /// <summary>
    /// The competitions a result needs before it can be a row at all, asked for together.
    /// </summary>
    private async Task<Dictionary<CompetitionId, Competition>> BorrowAsync(IEnumerable<CompetitionId> ids)
    {
        var found = new Dictionary<CompetitionId, Competition>();

        await Parallel.ForEachAsync(ids.Distinct(), Politely(CancellationToken.None), async (id, token) =>
        {
            if (await FetchAsync(id, token) is not { } competition)
                return;

            lock (found)
                found[id] = competition;
        });

        return found;
    }

    /// <summary>
    /// What the rows could not know without asking, filled in after they are up.
    /// </summary>
    /// <remarks>
    /// A row that cannot be filled in keeps the blank it already had, which is what it looked
    /// like before the request went out.
    /// </remarks>
    private async Task FillAsync(
        PersonId me, IReadOnlyList<MyResultRow> rows, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(
                FillDistanceAsync([.. rows.Where(row => !row.HasDiscipline)], cancellationToken),
                FillFieldAsync(me, [.. rows.Where(row => !row.HasField && row.Place is not null)], cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The page was left or reloaded. Nothing to finish and nothing to report.
        }
    }

    /// <summary>
    /// Fills in the distance for the rows whose own name did not state it.
    /// </summary>
    /// <remarks>
    /// Grouped by competition, because O-Ringen is five rows and one request.
    /// </remarks>
    private async Task FillDistanceAsync(IReadOnlyList<MyResultRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return;

        await Parallel.ForEachAsync(
            rows.GroupBy(row => row.Competition),
            Politely(cancellationToken),
            async (group, token) =>
        {
            if (await FetchAsync(group.Key, token) is not { } competition)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var row in group)
                {
                    // Same rule as at build time: the calendar may only speak for the race it is
                    // actually describing, never for the week a stage sat in.
                    if (competition.Date == row.Date)
                        row.Discipline = competition.Discipline;
                }
            });
        });
    }

    /// <summary>
    /// Fills in how large the field was, for the rows whose own source did not carry it.
    /// </summary>
    /// <remarks>
    /// One request for the whole season. Eventor answers a person and a list of events directly,
    /// so the app asks its own narrow question instead of pulling a competition at a time to find
    /// one row in it — O-Ringen's whole result list is 86 MB and 97 seconds of it, which is why
    /// its five rows never got a number at all.
    /// </remarks>
    private async Task FillFieldAsync(
        PersonId me, IReadOnlyList<MyResultRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return;

        IReadOnlyList<CompetitionResult> mine;

        try
        {
            mine = await _participation.GetOwnResultsAsync(
                me,
                [.. rows.Select(row => row.Competition).Distinct()],
                cancellationToken: cancellationToken);
        }
        catch (SourceUnavailableException)
        {
            return;
        }

        if (mine.Count == 0)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var row in rows)
            {
                // A multi-day event is one id and many races, so the runner appears once per
                // stage; the placement is what tells the stages apart. One result is one race
                // and needs no telling apart.
                var here = mine.Where(result => result.Competition == row.Competition).ToList();

                var result = here.FirstOrDefault(r => r.Place == row.Place)
                    ?? (here.Count == 1 ? here[0] : null);

                if (result is { Starters: > 0 })
                    row.FieldText = Format.OutOf(result.Starters);
            }
        });
    }

    /// <summary>A single missing competition is a missing symbol, never a failed page.</summary>
    private async Task<Competition?> FetchAsync(CompetitionId id, CancellationToken cancellationToken)
    {
        try
        {
            return await _events.GetCompetitionAsync(id, cancellationToken);
        }
        catch (SourceUnavailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the race the row is about — under the competition, not beside it.
    /// </summary>
    /// <remarks>
    /// The participant list in its result mode, opened on the class this result was run in. A
    /// result read outside its own competition is the split the redesign exists to close: the
    /// reader lands in the field they ran against, with their own row marked, and the runner's
    /// own analysis one tap further in.
    /// </remarks>
    [RelayCommand]
    private async Task OpenResult(MyResultRow row) =>
        await _navigation.NavigateToAsync<ParticipantsPage, ParticipantsTarget>(
            new ParticipantsTarget(row.Competition, row.Class, ParticipantMode.Results));
}
