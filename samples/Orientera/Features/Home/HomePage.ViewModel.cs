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
using Orientera.Services.Weather;

// MAUI har ett eget ViewState — dess är en tillståndsgrupp för visuella tillstånd, vårt är de
// fyra lägena i P10. Aliaset säger vilket som avses här.
using ViewState = Orientera.Controls.ViewState;

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
    WeatherService _weather,
    CompetitionContextService _context) : OrienteraViewModel
{
    /// <summary>Hem has few large blocks, not a dense dashboard.</summary>
    private const int MaxBlocks = 4;

    /// <summary>How many simultaneous races Hem will lead with before it stops being a summary.</summary>
    private const int MaxLiveBlocks = 3;

    public ObservableCollection<HomeBlock> Blocks { get; } = [];

    [ObservableProperty] public partial string Greeting { get; set; } = string.Empty;
    [ObservableProperty] public partial string TodayText { get; set; } = string.Empty;

    /// <summary>"☀️ 18° i Gävle". Tom när det inte finns något väder att stå för — se WeatherStore.</summary>
    [ObservableProperty] public partial string WeatherText { get; set; } = string.Empty;

    /// <summary>Samma rad i ord, för den som får den uppläst. Symbolen säger ingenting högt.</summary>
    [ObservableProperty] public partial string WeatherDescription { get; set; } = string.Empty;

    public bool HasWeather => WeatherText.Length > 0;

    partial void OnWeatherTextChanged(string value) => OnPropertyChanged(nameof(HasWeather));

    /// <summary>
    /// Vilket av de fyra lägena sidan står i (P10).
    /// </summary>
    /// <remarks>
    /// Ett värde och inte tre <c>IsVisible</c> som råkar vara falska samtidigt. Ordningen är
    /// regeln: ingenting är tomt medan svaret är okänt, och ingenting är offline medan en
    /// hämtning fortfarande pågår. Det var precis den kombinationen testkörningen hittade — ett
    /// tomt läge uppritat ovanpå en pågående laddning — och med ett enda värde kan den inte uppstå.
    /// <para>
    /// Offline är sidans fel-läge och inte ett femte. Det som gick fel är nätet, det som ändå
    /// fungerar står utskrivet, och knappen försöker igen: samma tre delar som P10 kräver av ett
    /// fel, med orden som hör till just det här felet.
    /// </para>
    /// </remarks>
    public ViewState State =>
        IsLoading ? ViewState.Loading
        : IsOffline ? ViewState.Error
        : HasContent ? ViewState.Content
        : ViewState.Empty;

    /// <summary>
    /// Hälsningens plats i hjälten.
    /// </summary>
    /// <remarks>
    /// Bilden går under statusfältet — sidan har lämnat toppen ur sina <c>SafeAreaEdges</c> — så
    /// texten måste hålla sig undan det själv. Höjden är mätt och aldrig gissad: en ö och ett
    /// hack är inte lika höga.
    /// <para>
    /// Följden är att korten passerar under statusfältet när listan skrollas, eftersom hjälten
    /// skrollar med dem. Det är hur en helbleed-sida beter sig på iOS, och priset för att bilden
    /// ska nå ända upp.
    /// </para>
    /// </remarks>
    public Thickness HeroPadding => new(16, SafeAreaInsets.Top + 12, 16, 0);

    /// <summary>
    /// Hur högt bilden går: knappt halva skärmen.
    /// </summary>
    /// <remarks>
    /// Räknat ur skärmen och inte satt i punkter, eftersom "knappt halva" är ett förhållande och
    /// inte ett mått — 400 punkter är nästan hela en iPhone SE och en tredjedel av en iPad. Läses
    /// en gång, för hjälten ligger i listans huvud där stjärnhöjder inte finns, och en telefon som
    /// vrids på Hem är inte fallet den här sidan är byggd för.
    /// </remarks>
    public double HeroHeight
    {
        get
        {
            var display = DeviceDisplay.MainDisplayInfo;
            var points = display.Density > 0 ? display.Height / display.Density : 0;

            // Innan skärmen är mätt är svaret noll, och en hjälte utan höjd är ingen hjälte.
            return points > 0 ? Math.Round(points * 0.46) : 360;
        }
    }

    /// <summary>
    /// Hur långt blocken får gå upp på bilden: en tredjedel av hjälten, så två tredjedelar av
    /// bilden står fria.
    /// </summary>
    /// <remarks>
    /// Negativ överkant på blockstapeln, inte på hjälten. Hjälten ligger kvar i skrollvyn med hela
    /// sin höjd och ritas hel; det är blocken som dras upp och läggs ovanpå dess nedre hälft, så
    /// bilden fortsätter bakom och bredvid korten i stället för att sluta vid det första.
    /// <para>
    /// Det var precis det som gick fel med hjälten i <c>CollectionView.Header</c>: huvudets cell
    /// beskär sitt innehåll till den höjd marginalen lämnar, så bilden kapades vid kortets
    /// överkant och kortet stod på sidans yta i stället för på fotot.
    /// </para>
    /// <para>
    /// En andel av samma slag som höjden: en tredjedel är en tredjedel på varje skärm.
    /// </para>
    /// </remarks>
    public Thickness HeroOverlap => new(0, -Math.Round(HeroHeight / 3), 0, 0);

    /// <summary>
    /// Den hopfällda rubrikradens höjd, under statusfältet.
    /// </summary>
    /// <remarks>
    /// Samma höjd som Spines egen rubrikrad, så den lilla rubriken hamnar på exakt den plats
    /// "TÄVLINGAR" har på sin sida — uppmätt till 76 punkter från skärmens överkant, och det är
    /// den här höjden som avgör det, eftersom texten centreras i raden.
    /// <para>
    /// Talen är avskrivna och inte lånade: <c>HeaderBarConstants</c> är internal i Spine. Ändras
    /// de där måste de ändras här.
    /// </para>
    /// </remarks>
#if ANDROID
    public double TopTitleHeight => 48;
#else
    public double TopTitleHeight => 32;
#endif

    /// <summary>
    /// Rubrikradens plats: direkt under statusfältet, precis som Spines egen.
    /// </summary>
    /// <remarks>
    /// Ingen extra luft ovanför. Raden börjar där statusfältet slutar och texten centreras i den,
    /// vilket lägger bläcket 76 punkter från skärmens överkant — uppmätt till samma punkt som
    /// "TÄVLINGAR" står på sin sida.
    /// </remarks>
    public Thickness TopTitleMargin => new(16, SafeAreaInsets.Top, 16, 0);

    /// <summary>
    /// Höjden på oskärpan bakom statusfältet: fältet självt, rubrikraden, och den sträcka bandet
    /// tonar ut över.
    /// </summary>
    /// <remarks>
    /// Uttoningen måste rymmas inom bandet — ett lager kan inte tona utanför sin egen ram — så den
    /// läggs till här i stället för att ätas ur det som ska vara tätt. Måttet kommer från
    /// <see cref="EdgeBlur.DefaultFadeHeight"/>, så det bara finns på ett ställe.
    /// </remarks>
    public double TopBlurHeight => SafeAreaInsets.Top + TopTitleHeight + EdgeBlur.DefaultFadeHeight;

    /// <summary>
    /// Luften under sista kortet. Bara underkanten: SafeAreaInsets bär numera statusfältet
    /// också, och hela tjockleken hade lagt lika mycket luft under listan som ovanför den.
    /// </summary>
    public Thickness ListBottomInset => new(0, 0, 0, SafeAreaInsets.Bottom);

    /// <summary>
    /// De härledda egenskaperna räknas om när det de vilar på ändras: läget när en hämtning
    /// börjar eller slutar, och tjocklekarna när Spine har mätt sidans insets — vilket sker
    /// efter att vyn bundit dem.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(IsLoading) or nameof(IsOffline) or nameof(HasContent))
            OnPropertyChanged(nameof(State));

        if (e.PropertyName != nameof(SafeAreaInsets))
            return;

        OnPropertyChanged(nameof(HeroPadding));
        OnPropertyChanged(nameof(ListBottomInset));
        OnPropertyChanged(nameof(TopBlurHeight));
        OnPropertyChanged(nameof(TopTitleMargin));
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // Läses före ScheduleWelcome, som besvarar frågan i samma andetag: det här är det enda
        // som skiljer första körningen från alla senare, och positionsdialogen får inte ställas
        // i den. Se WeatherService.HasLocationPermissionAsync.
        var isFirstRun = !_firstRun.IsAnswered;

        ScheduleWelcome();

        var session = _resume.Generation;

        await ReloadAsync();

        await LoadWeatherAsync(mayAskForLocation: !isFirstRun);

        await ResumeEventorAsync(session);
    }

    /// <summary>
    /// Vädret hämtas efter blocken och aldrig före dem. Det är en utsmyckning på en hälsning, och
    /// en sida som väntar på SMHI innan den visar dagens tävling har fel ordning på sina svar.
    /// </summary>
    private async Task LoadWeatherAsync(bool mayAskForLocation)
    {
        Person me;

        try
        {
            me = await _people.GetMeAsync();
        }
        catch (SourceUnavailableException)
        {
            // Hemorten är det enda vädret behöver av källorna, och utan den finns ingen rad. Att
            // låta det slå igenom hade tagit ned hela OnAppearing för en utsmyckning.
            WeatherText = string.Empty;
            WeatherDescription = string.Empty;
            return;
        }

        if (await _weather.LoadAsync(me, mayAskForLocation) is not { } weather)
        {
            WeatherText = string.Empty;
            WeatherDescription = string.Empty;
            return;
        }

        var degrees = (int)Math.Round(weather.TemperatureC);

        WeatherText = $"{WeatherWords.Symbol(weather.Symbol)} {degrees.ToString(Format.Culture)}° i {weather.Place}";

        WeatherDescription = string.Join(", ", new[]
        {
            $"{degrees.ToString(Format.Culture)} grader i {weather.Place}",
            WeatherWords.Spoken(weather.Symbol),
        }.Where(s => s.Length > 0));
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

    /// <summary>Knappen i offline-läget. Samma väg in som när sidan visas.</summary>
    [RelayCommand]
    private async Task Reload() => await ReloadAsync();

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
