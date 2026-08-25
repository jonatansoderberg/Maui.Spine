using System.Collections.ObjectModel;
using System.Globalization;
using Orientera.Controls;
using Orientera.Domain;
using Orientera.Features.Events.Participants;
using Orientera.Features.Dev;
using Orientera.Features.Events;
using Orientera.Features.Onboarding;
using Orientera.Features.Profile;
using Orientera.Features.Results;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Offline;
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
    RacePreferenceStore _preferences,
    CompetitionContextService _context) : OrienteraViewModel
{
    /// <summary>Hem has few large blocks, not a dense dashboard.</summary>
    private const int MaxBlocks = 4;

    /// <summary>How many simultaneous races Hem will lead with before it stops being a summary.</summary>
    private const int MaxLiveBlocks = 3;

    public ObservableCollection<HomeBlock> Blocks { get; } = [];

    [ObservableProperty] public partial string Greeting { get; set; } = string.Empty;
    [ObservableProperty] public partial string TodayText { get; set; } = string.Empty;

    /// <summary>
    /// Hälsningens plats i hjälten. Bilden går under statusfältet (sidan har lämnat toppen ur
    /// sina SafeAreaEdges), så texten måste hålla sig undan det själv — och höjden är mätt,
    /// aldrig gissad: en ö och ett hack är inte lika höga.
    /// </summary>
    public Thickness HeroPadding => new(16, SafeAreaInsets.Top + 8, 16, 0);

    /// <summary>
    /// Luften under sista kortet. Bara underkanten: SafeAreaInsets bär numera statusfältet
    /// också, och hela tjockleken hade lagt lika mycket luft under listan som ovanför den.
    /// </summary>
    public Thickness ListBottomInset => new(0, 0, 0, SafeAreaInsets.Bottom);

    /// <summary>
    /// De två härledda tjocklekarna räknas om när Spine har mätt sidans insets, vilket sker
    /// efter att vyn bundit dem.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName != nameof(SafeAreaInsets))
            return;

        OnPropertyChanged(nameof(HeroPadding));
        OnPropertyChanged(nameof(ListBottomInset));
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        ScheduleWelcome();

        var session = _resume.Generation;

        await ReloadAsync();

        await ResumeEventorAsync(session);
    }

    /// <summary>
    /// Revives an expired Eventor session, if the runner let the app remember the password.
    /// </summary>
    /// <remarks>
    /// The work lives in <see cref="EventorSessionResume"/> so every tab can ask for it — a
    /// session that died while the runner was reading results used to stay dead until they
    /// happened to open Hem. Read again only when the session the blocks were built from is no
    /// longer the app's, whoever the login was triggered by.
    /// </remarks>
    private async Task ResumeEventorAsync(int session)
    {
        await _resume.EnsureAsync(_navigation);

        if (_resume.Generation != session)
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

            // Efter inloggningen, för då vet frågan vem som svarar — och före första listan, så att
            // en MTBO-åkare aldrig ser en kalender som saknar deras tävlingar utan att veta varför.
            await _navigation.NavigateToAsync<SportChoiceSheet>();

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
    private async Task OpenCompetition(CompetitionId competition) =>
        await _navigation.NavigateToAsync<EventDetailsPage, CompetitionId>(competition);

    /// <summary>
    /// Into the race itself, not into a section about races. The block already knows which
    /// competition it is about, so the list opens on it — in live mode, in the reader's class.
    /// </summary>
    [RelayCommand]
    private async Task OpenLive(CompetitionId competition) =>
        await _navigation.NavigateToAsync<ParticipantsPage, ParticipantsTarget>(
            new ParticipantsTarget(competition, Mode: ParticipantMode.Live));

    [RelayCommand]
    private async Task OpenResult(CompetitionId competition) =>
        await _navigation.NavigateToAsync<ParticipantsPage, ParticipantsTarget>(
            new ParticipantsTarget(competition, Mode: ParticipantMode.Results));

    [RelayCommand]
    private async Task OpenEvents() => await _navigation.SwitchToTabAsync<EventsPage>();

    /// <summary>
    /// Hela säsongen, pushad här och inte via Jag: "Se alla" står bredvid det senaste resultatet
    /// och ska svara på det, inte lämna över läsaren till en flik att leta i.
    /// </summary>
    [RelayCommand]
    private async Task OpenMyResults() => await _navigation.NavigateToAsync<MyResultsPage>();

    [RelayCommand]
    private async Task OpenProfile() => await _navigation.SwitchToTabAsync<Profile.ProfilePage>();

    private async Task BuildAsync()
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.Date);
        var me = await _people.GetMeAsync();

        // Klockan från _clock, inte systemet: tidsmaskinen under Jag flyttar hela appens dygn,
        // och en hälsning som stod kvar på "God morgon" hade varit det enda som inte följde med.
        Greeting = $"{Format.Salutation(now)} {me.Name.Split(' ')[0]}";
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
        var liveBlocks = await BuildLiveAsync(me, myEntries, groupEntries);

        blocks.AddRange(liveBlocks);

        // A competition already shown as "Live nu" must not come back as "Nästa för mig" —
        // two blocks about the same event is exactly the dashboard clutter the rule avoids.
        if (await BuildNextForMeAsync(competitions, myEntries, now, today, [.. liveBlocks.Select(b => b.Competition)]) is { } next)
            blocks.Add(next);

        if (await BuildLatestResultAsync(me, competitions) is { } latest)
            blocks.Add(latest);

        if (BuildGroup(competitions, groupEntries, group, today) is { } groupBlock)
            blocks.Add(groupBlock);

        if (BuildDiscovery(competitions, _preferences.Load(), me, myEntries, groupEntries, now, today)
            is { } discovery)
            blocks.Add(discovery);

        if (await BuildDevelopmentAsync(me) is { } development)
            blocks.Add(development);

        Blocks.Clear();

        foreach (var block in blocks.Take(MaxBlocks))
            Blocks.Add(block);
    }

    /// <summary>
    /// Every competition running right now that the reader has someone in.
    /// </summary>
    /// <remarks>
    /// Every one, not the first. Following two runners in two races at once was the live tab's
    /// one job that no competition's own page can do, and with the tab gone this is where it
    /// lands: a championship weekend, or a parent with children in different races, gets a block
    /// each rather than whichever competition happened to sort first.
    /// </remarks>
    private async Task<IReadOnlyList<LiveNowBlock>> BuildLiveAsync(
        Person me,
        IReadOnlySet<CompetitionId> myEntries,
        IReadOnlySet<CompetitionId> groupEntries)
    {
        var liveCompetitions = await _live.GetLiveCompetitionsAsync();

        // "Relevant" means me or someone I follow is in it — not merely that something is live.
        var relevant = liveCompetitions
            .Where(c => myEntries.Contains(c.Id) || groupEntries.Contains(c.Id))
            // Hem is a few large blocks, never a dense dashboard. Past three simultaneous races
            // the page stops being a summary of the day and becomes a list of them.
            .Take(MaxLiveBlocks)
            .ToList();

        _tabBadges.SetBadge<EventsPage>(relevant.Count > 0 ? string.Empty : null);

        var group = await _people.GetMyGroupAsync();

        var blocks = new List<LiveNowBlock>(relevant.Count);

        foreach (var competition in relevant)
            blocks.Add(await LiveBlockAsync(me, competition, group));

        return blocks;
    }

    /// <summary>Hur många ansikten kortet visar innan resten blir ett tal.</summary>
    private const int MaxFaces = 4;

    private async Task<LiveNowBlock> LiveBlockAsync(
        Person me,
        Competition relevant,
        IReadOnlyList<FollowedPerson> group)
    {
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

        // Dem läsaren följer som faktiskt står i det här fältet — inte hela följningslistan.
        // Det är skillnaden mellan "din grupp" och "din grupp i den här tävlingen", och kortet
        // handlar om den andra.
        var entered = snapshot.Entries.Select(e => e.Person).ToHashSet();

        var faces = group
            .Where(f => entered.Contains(f.Person.Id))
            .Take(MaxFaces)
            .Select(f => new Face(null, f.Person.Initials))
            .ToList();

        return new LiveNowBlock
        {
            SectionLabel = "Live nu",
            Faces = faces,
            FieldSize = snapshot.Entries.Count,
            FieldText = faces.Count > 0
                ? $"{faces.Count} du följer och {Math.Max(0, snapshot.Entries.Count - faces.Count)} till i fältet"
                : string.Empty,
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
        IReadOnlyList<CompetitionId> alreadyShown)
    {
        var next = competitions
            .Where(c => myEntries.Contains(c.Id) && c.LastFinish > now)
            // A competition already up as "Live nu" must not come back as "Nästa för dig" — two
            // blocks about the same race is exactly the clutter the priority rule avoids.
            .Where(c => !alreadyShown.Contains(c.Id))
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

    /// <summary>
    /// Fältets storlek för ett resultat. Eventors "mina resultat"-sida bär den inte, så den
    /// hämtas ur tävlingens egen resultatlista — samma källa som resultatsidan läser. Utan
    /// känt fält visas placeringen ensam hellre än mot en gissad nämnare.
    /// </summary>
    private async Task<int> StartersOfAsync(CompetitionResult latest, Person me)
    {
        try
        {
            var field = await _participation.GetResultsAsync(latest.Competition);

            var mine = field.FirstOrDefault(r => r.Person == me.Id && r.Place == latest.Place)
                ?? field.FirstOrDefault(r => r.Person == me.Id);
            return mine?.Starters ?? 0;
        }
        catch (SourceUnavailableException)
        {
            return 0;
        }
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
            Stats = await StatsOfAsync(latest, me),
            TrendText = BestPlaceOfYear(results, latest) ? "Bästa placering i år" : string.Empty,
            HasSplits = latest.Splits.Count > 0,
            ActionText = latest.Splits.Count > 0 ? "Analysera" : "Mitt resultat",
        };
    }

    /// <summary>
    /// Resultatets tre nyckeltal. Det tredje är snittet när banlängden är känd och tappet mot
    /// vinnaren när den inte är det — aldrig ett snitt räknat mot en gissad nämnare.
    /// </summary>
    private async Task<IReadOnlyList<Stat>> StatsOfAsync(CompetitionResult latest, Person me)
    {
        // Placeringen med fältet under sig. Talet ensamt säger inte om 33 är bra: "av 67" är
        // det som gör det läsbart, och enhetsraden är precis den plats StatRow har för det.
        var stats = new List<Stat>(3)
        {
            new("Placering", Format.PlaceNumber(latest.Place), Format.OutOf(await StartersOfAsync(latest, me))),
            new("Tid", Format.Time(latest.Time)),
        };

        if (latest.Time is { } time && await CourseLengthAsync(latest) is { } km
            && Format.Pace(time, km) is { Length: > 0 } pace)
            stats.Add(new Stat("Snitt", pace, "min/km"));
        else if (latest.BehindWinner is { } behind)
            stats.Add(new Stat("Efter", Format.Delta(behind)));

        return stats;
    }

    private async Task<double?> CourseLengthAsync(CompetitionResult latest)
    {
        try
        {
            var course = await _events.GetCourseAsync(latest.Competition, latest.Class);
            return course?.LengthKm;
        }
        catch (SourceUnavailableException)
        {
            // Att inte veta banlängden är inte att veta att den saknas — kortet faller till
            // tappet mot vinnaren i stället, och säger inget om något snitt.
            return null;
        }
    }

    /// <summary>
    /// Om det här resultatet är årets bästa placering. Ett påstående om placeringar och
    /// ingenting annat: fälten skiljer sig åt mellan tävlingar, och en jämförelse av tider
    /// mellan två banor vore ingen jämförelse alls.
    /// </summary>
    private static bool BestPlaceOfYear(IReadOnlyList<CompetitionResult> results, CompetitionResult latest)
    {
        // Utan känt datum finns inget "i år" att jämföra inom, och då sägs ingenting alls.
        if (latest.Place is not { } place || latest.CompetitionDate is not { } date)
            return false;

        var year = results
            .Where(r => r.CompetitionDate?.Year == date.Year && r.Place is not null)
            .ToList();

        return year.Count > 1 && year.All(r => r.Place >= place);
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
        RacePreferences preferences,
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
            Favourites = preferences.Favourites,
        };

        // Ranking, not Score().Total, and the same date tiebreak the list uses: two races of the
        // same championship score identically to five decimals, and picking on the raw total put
        // Sunday's above Saturday's here while the list had them the right way round.
        var candidate = competitions
            .Where(c => preferences.Allows(c.Sport))
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
