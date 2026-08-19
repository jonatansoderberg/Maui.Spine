using System.Collections.ObjectModel;
using System.Globalization;
using Orientera.Domain;
using Orientera.Features.Dev;
using Orientera.Features.Events;
using Orientera.Features.Live;
using Orientera.Features.Onboarding;
using Orientera.Features.Profile;
using Orientera.Features.Results;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Relevance;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Home;

public partial class HomePageViewModel(
    INavigationService _navigation,
    ITabBadgeService _tabBadges,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    ILiveSource _live,
    IProgressSource _progress,
    FirstRunStore _firstRun,
    EventorSessionResume _resume,
    CompetitionContextService _context) : OrienteraViewModel
{
    /// <summary>Hem has few large blocks, not a dense dashboard.</summary>
    private const int MaxBlocks = 4;

    public ObservableCollection<HomeBlock> Blocks { get; } = [];

    [ObservableProperty] public partial string Greeting { get; set; } = string.Empty;
    [ObservableProperty] public partial string TodayText { get; set; } = string.Empty;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
            PageActions.Add(new PageAction(text: "Tid", command: OpenTimeMachineCommand));

        ScheduleWelcome();

        await ReloadAsync();

        await ResumeEventorAsync();
    }

    /// <summary>
    /// Revives an expired Eventor session, if the runner let the app remember the password.
    /// </summary>
    /// <remarks>
    /// The work lives in <see cref="EventorSessionResume"/> so every tab can ask for it — a
    /// session that died while the runner was reading results used to stay dead until they
    /// happened to open Hem.
    /// </remarks>
    private async Task ResumeEventorAsync()
    {
        if (await _resume.TryResumeAsync(_navigation))
            await ReloadAsync();
    }

    /// <summary>
    /// The first launch asks the one question the app cannot answer for the user, and then never
    /// asks again — skipping is an answer.
    /// </summary>
    /// <remarks>
    /// Queued rather than awaited. Pushing a sheet from inside the first page's own appearing
    /// crashed the app at startup with "MauiContext is null": the window the sheet needs is not
    /// there until this method has returned.
    /// </remarks>
    private void ScheduleWelcome()
    {
        if (_firstRun.IsAnswered)
            return;

        _firstRun.MarkAnswered();

        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            var choice = await _navigation.NavigateToWithResultAsync<WelcomeSheet, WelcomeChoice>();

            // Appens egna fält, samma väg in som Jag erbjuder (#142). Den första inloggningen en
            // användare möter ska inte vara den vi valt bort.
            if (choice is { IsSuccess: true, Value.WantsLogin: true })
                await _navigation.NavigateToWithResultAsync<AppLoginSheet, EventorWebSession>();

            await ReloadAsync();
        });
    }

    private async Task ReloadAsync()
    {
        await LoadAsync(BuildAsync);

        if (IsOffline)
            Blocks.Clear();

        HasContent = Blocks.Count > 0;
    }

    [RelayCommand]
    private async Task OpenTimeMachine()
    {
        await _navigation.NavigateToAsync<TimeMachineSheet>();
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenCompetition(CompetitionId competition) =>
        await _navigation.NavigateToAsync<EventDetailsPage, CompetitionId>(competition);

    [RelayCommand]
    private async Task OpenLive() => await _navigation.SwitchToTabAsync<LivePage>();

    [RelayCommand]
    private async Task OpenResult(CompetitionId competition) =>
        await _navigation.NavigateToAsync<ResultsDetailPage, CompetitionId>(competition);

    [RelayCommand]
    private async Task OpenEvents() => await _navigation.SwitchToTabAsync<EventsPage>();

    [RelayCommand]
    private async Task OpenProfile() => await _navigation.SwitchToTabAsync<Profile.ProfilePage>();

    private async Task BuildAsync()
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);
        var me = await _people.GetMeAsync();

        Greeting = $"Hej {me.Name.Split(' ')[0]}";
        TodayText = now.ToString("dddd d MMMM");

        var competitions = await _events.GetCompetitionsAsync();
        var entries = await _participation.GetEntriesAsync();
        var group = await _people.GetMyGroupAsync();
        var groupIds = group.Select(f => f.Person.Id).ToHashSet();

        var myEntries = entries.Where(e => e.Person == me.Id).Select(e => e.Competition).ToHashSet();
        var groupEntries = entries.Where(e => groupIds.Contains(e.Person)).Select(e => e.Competition).ToHashSet();

        var blocks = new List<HomeBlock>();

        // Prioriteringsregeln, i ordning:
        // 1. Något relevant live → högst upp. 2. Annars Nästa för mig.
        // 3. Sedan senaste resultat, discovery, Min grupp och utveckling.
        var liveBlock = await BuildLiveAsync(me, myEntries, groupEntries);

        if (liveBlock is not null)
            blocks.Add(liveBlock);

        // A competition already shown as "Live nu" must not come back as "Nästa för mig" —
        // two blocks about the same event is exactly the dashboard clutter the rule avoids.
        if (await BuildNextForMeAsync(competitions, myEntries, now, today, liveBlock?.Competition) is { } next)
            blocks.Add(next);

        if (await BuildLatestResultAsync(me, competitions) is { } latest)
            blocks.Add(latest);

        if (BuildGroup(competitions, groupEntries, group, today) is { } groupBlock)
            blocks.Add(groupBlock);

        if (BuildDiscovery(competitions, me, myEntries, groupEntries, now, today) is { } discovery)
            blocks.Add(discovery);

        if (await BuildDevelopmentAsync(me) is { } development)
            blocks.Add(development);

        Blocks.Clear();

        foreach (var block in blocks.Take(MaxBlocks))
            Blocks.Add(block);
    }

    private async Task<LiveNowBlock?> BuildLiveAsync(
        Person me,
        IReadOnlySet<CompetitionId> myEntries,
        IReadOnlySet<CompetitionId> groupEntries)
    {
        var liveCompetitions = await _live.GetLiveCompetitionsAsync();

        // "Relevant" means me or someone I follow is in it — not merely that something is live.
        var relevant = liveCompetitions
            .FirstOrDefault(c => myEntries.Contains(c.Id) || groupEntries.Contains(c.Id));

        if (relevant is null)
        {
            _tabBadges.SetBadge<LivePage>(null);
            return null;
        }

        _tabBadges.SetBadge<LivePage>("");

        var snapshot = await _live.GetSnapshotAsync(relevant.Id);
        var mine = snapshot.Entries.FirstOrDefault(e => e.Person == me.Id);

        string status = mine switch
        {
            { Status: LiveStatus.Running, LastPassing.Control: var control, Position: { } position } =>
                $"Du är vid kontroll {ControlName(mine.Class, control)}, {Format.Place(position)} i {mine.Class}",
            { Status: LiveStatus.Finished, FinalPlace: { } place } =>
                $"Du är i mål, {Format.Place(place)}",
            { Status: LiveStatus.NotStarted, StartTime: { } start } =>
                $"Din start {Format.Clock(start)}",
            // The live source has the user in the class but no start time for them, which is how
            // it reports someone who never started.
            { Status: LiveStatus.NotStarted } =>
                $"Du står som ej start i {mine.Class}",
            _ => $"{snapshot.Entries.Count(e => e.Status == LiveStatus.Running)} löpare i skogen",
        };

        return new LiveNowBlock
        {
            SectionLabel = "Live nu",
            Competition = relevant.Id,
            DisciplineShape = DisciplineShape.For(relevant.Discipline),
            DisciplineKey = relevant.Discipline.ToString(),
            DisciplineLabel = Format.Discipline(relevant.Discipline),
            LevelShape = DisciplineShape.For(relevant.Level),
            LevelLabel = Format.Level(relevant.Level),
            Title = relevant.Name,
            Subtitle = $"{relevant.Organiser} · {relevant.Place}",
            MyStatus = status,
            ActionText = "Följ live",
        };

        // The live source numbers a radio control by its timing-system code; the number written
        // on the control in the forest is the one a runner recognises.
        string ControlName(string className, int code) =>
            snapshot.ControlsFor(className).FirstOrDefault(c => c.Code == code)?.Name
                ?? code.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<NextForMeBlock?> BuildNextForMeAsync(
        IReadOnlyList<Competition> competitions,
        IReadOnlySet<CompetitionId> myEntries,
        DateTimeOffset now,
        DateOnly today,
        CompetitionId? alreadyShown)
    {
        var next = competitions
            .Where(c => myEntries.Contains(c.Id) && c.LastFinish > now)
            .Where(c => alreadyShown is not { } shown || c.Id != shown)
            .OrderBy(c => c.FirstStart)
            .FirstOrDefault();

        if (next is null)
            return null;

        var decision = await _context.EvaluateAsync(next);
        var starts = await _participation.GetStartsAsync(next.Id);
        var me = await _people.GetMeAsync();
        var myStart = starts.FirstOrDefault(s => s.Person == me.Id);

        return new NextForMeBlock
        {
            SectionLabel = "Nästa för dig",
            Competition = next.Id,
            DisciplineShape = DisciplineShape.For(next.Discipline),
            DisciplineKey = next.Discipline.ToString(),
            DisciplineLabel = Format.Discipline(next.Discipline),
            LevelShape = DisciplineShape.For(next.Level),
            LevelLabel = Format.Level(next.Level),
            Title = next.Name,
            WhenText = $"{Format.RelativeDate(next.Date, today)} · första start {Format.Clock(next.FirstStart)}",
            PlaceText = $"{next.Organiser} · {next.Place}",
            StartText = myStart is not null ? $"Din start {Format.Clock(myStart.StartTime)}" : string.Empty,
            HasStart = myStart is not null,
            StateText = decision.StateText,
            ActionText = decision.PrimaryActionText,
        };
    }

    private async Task<LatestResultBlock?> BuildLatestResultAsync(Person me, IReadOnlyList<Competition> competitions)
    {
        var results = await _participation.GetResultsForPersonAsync(me.Id);
        var latest = results.FirstOrDefault();

        if (latest is null)
            return null;

        var competition = competitions.FirstOrDefault(c => c.Id == latest.Competition);

        if (competition is null)
            return null;

        // The race's own name first: a stage of a multi-day event knows its distance where the
        // calendar entry for the whole week does not.
        var resultDiscipline = latest.CompetitionDiscipline ?? competition.Discipline;

        return new LatestResultBlock
        {
            SectionLabel = "Senaste resultat",
            Competition = latest.Competition,
            DisciplineShape = DisciplineShape.For(resultDiscipline),
            DisciplineKey = resultDiscipline.ToString(),
            DisciplineLabel = Format.Discipline(resultDiscipline),
            LevelShape = DisciplineShape.For(competition.Level),
            LevelLabel = Format.Level(competition.Level),
            Title = competition.Name,
            PlaceText = Format.Place(latest.Place),
            TimeText = Format.Time(latest.Time),
            BehindText = latest.BehindWinner is { } behind ? Format.Delta(behind) : string.Empty,
            HasSplits = latest.Splits.Count > 0,
            ActionText = latest.Splits.Count > 0 ? "Analysera" : "Mitt resultat",
        };
    }

    private static GroupBlock? BuildGroup(
        IReadOnlyList<Competition> competitions,
        IReadOnlySet<CompetitionId> groupEntries,
        IReadOnlyList<FollowedPerson> group,
        DateOnly today)
    {
        var upcoming = competitions
            .Where(c => groupEntries.Contains(c.Id) && c.Date >= today)
            .OrderBy(c => c.FirstStart)
            .Take(2)
            .ToList();

        if (upcoming.Count == 0)
            return null;

        return new GroupBlock
        {
            SectionLabel = "Favoriter",
            Summary = $"{group.Count} personer du följer",
            Lines = upcoming
                .Select(c => $"{Format.RelativeDate(c.Date, today)} · {c.Name}")
                .ToList(),
        };
    }

    private static DiscoveryBlock? BuildDiscovery(
        IReadOnlyList<Competition> competitions,
        Person me,
        IReadOnlySet<CompetitionId> myEntries,
        IReadOnlySet<CompetitionId> groupEntries,
        DateTimeOffset now,
        DateOnly today)
    {
        var context = new RelevanceContext
        {
            Now = now,
            Home = me.Home,
            HomeDistrict = me.District,
            MyClass = me.DefaultClass,
            MyEntries = myEntries,
            GroupEntries = groupEntries,
        };

        // Ranking, not Score().Total, and the same date tiebreak the list uses: two races of the
        // same championship score identically to five decimals, and picking on the raw total put
        // Sunday's above Saturday's here while the list had them the right way round.
        var candidate = competitions
            .Where(c => !myEntries.Contains(c.Id) && !c.IsLowPriority && c.FirstStart > now)
            .Select(c => (Competition: c, Score: RelevanceEngine.Ranking(c, context)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Competition.FirstStart)
            .FirstOrDefault();

        if (candidate.Competition is null)
            return null;

        var deadline = candidate.Competition.Schedule.EntryDeadline;

        string reason = deadline is { } dl && dl > now && (dl - now).TotalDays <= 14
            ? $"Anmälan stänger {Format.RelativeDate(DateOnly.FromDateTime(dl.Date), today)}"
            : $"{Format.Level(candidate.Competition.Level)} i {candidate.Competition.District}";

        return new DiscoveryBlock
        {
            SectionLabel = "Kan vara något för dig",
            Competition = candidate.Competition.Id,
            DisciplineShape = DisciplineShape.For(candidate.Competition.Discipline),
            DisciplineKey = candidate.Competition.Discipline.ToString(),
            DisciplineLabel = Format.Discipline(candidate.Competition.Discipline),
            LevelShape = DisciplineShape.For(candidate.Competition.Level),
            LevelLabel = Format.Level(candidate.Competition.Level),
            Title = candidate.Competition.Name,
            WhenText = Format.RelativeDate(candidate.Competition.Date, today),
            ReasonText = reason,
        };
    }

    private async Task<DevelopmentBlock?> BuildDevelopmentAsync(Person me)
    {
        var ranking = await _progress.GetRankingAsync(me.Id);

        if (ranking is null)
            return null;

        return new DevelopmentBlock
        {
            SectionLabel = "Utveckling",
            // Two decimals, like Jag-fliken and Eventor itself. Sverigelistan separates runners
            // by hundredths, so a rounded 63 hides the whole difference it exists to show.
            PointsText = ranking.Points.ToString("N2", Format.Culture),
            PlaceText = $"{ranking.NationalPlace}:e i Sverige",
            TrendText = ranking.Trend >= 0 ? $"+{ranking.Trend} p" : $"{ranking.Trend} p",
            IsImproving = ranking.Trend >= 0,
        };
    }
}
