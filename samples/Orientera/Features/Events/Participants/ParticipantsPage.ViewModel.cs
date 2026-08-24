using System.Collections.ObjectModel;
using Orientera.Controls;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Context;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Offline;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Events.Participants;

/// <summary>Where the participant list should open: whose competition, which class, which mode.</summary>
/// <param name="Competition">The competition. The only part that is required.</param>
/// <param name="Class">The class to open in, or null to let the page resolve the reader's own.</param>
/// <param name="Mode">The mode to open in, or null to let the journey decide.</param>
public sealed record ParticipantsTarget(
    CompetitionId Competition,
    string? Class = null,
    ParticipantMode? Mode = null);

/// <summary>Which slice of the field the list is showing.</summary>
public enum ParticipantScope
{
    /// <summary>The people the reader follows, wherever in the competition they are running.</summary>
    MyGroup,

    /// <summary>The class the reader's own entry, choice or start says.</summary>
    MyClass,

    /// <summary>A class the reader picked, which is the only way to reach the rest of the field.</summary>
    Class,
}

/// <summary>
/// One competition's field, in whichever of its four modes there is something to show.
/// </summary>
/// <remarks>
/// The page the whole redesign is for: anmälda, startlista, live and resultat are the same list
/// asked four questions, and a reader who wants the next one switches rather than navigating to
/// another part of the app. What can be asked is decided by
/// <see cref="ParticipantModeEngine"/> from what the sources have actually answered.
/// </remarks>
public partial class ParticipantsPageViewModel(
    INavigationService _navigation,
    IClock _clock,
    IEventSource _events,
    IPeopleSource _people,
    IParticipationSource _participation,
    IStartFieldSource _field,
    ILiveSource _live,
    ILiveloxSource _livelox,
    OfflinePackageService _offline,
    CompetitionContextService _context,
    CompetitionClassStore _classes,
    EventorReader _eventor) : OrienteraViewModel, IReceivesNavigationParameter<ParticipantsTarget>
{
    /// <summary>LiveResults caches for 15 seconds, so polling faster only wastes data.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The name column stays put while the controls scroll past it, so how wide the table is
    /// depends on layout. These two are the same numbers the view lays the columns out with;
    /// the table would scroll to the wrong place if they drifted.
    /// </summary>
    public const double FrozenWidth = 156;

    /// <summary>Narrowest a column may be — a finish time with its place and time behind under it.</summary>
    public const double MinColumnWidth = 82;

    /// <summary>The gap between columns, which the view carries as each cell's right margin.</summary>
    private const double ColumnGap = 10;

    private ParticipantsTarget? _target;
    private Competition? _competition;
    private Person? _me;
    private ContextState _state = ContextState.Discovered;
    private IReadOnlyList<RunnerIdentity> _group = [];
    private RunnerIdentity _meIdentity;
    private IReadOnlyList<string> _classList = [];
    private LiveloxLink? _liveloxLink;
    private CancellationTokenSource? _polling;
    private DateTimeOffset _lastUpdate;
    private CompetitionSnapshot? _snapshot;

    private IReadOnlyDictionary<string, IReadOnlyList<LiveControl>> _controls =
        new Dictionary<string, IReadOnlyList<LiveControl>>();

    /// <summary>
    /// What each source has answered for the class on screen. Reset when the class or the scope
    /// changes — the answers belong to the question they were asked about.
    /// </summary>
    private ParticipantSightings _sightings = new();

    /// <summary>
    /// Set once the reader picks a mode by hand. After that the page stops moving under them:
    /// a switcher that keeps reverting to what the calendar prefers is a switcher that does not
    /// work.
    /// </summary>
    private bool _chosenByHand;

    private int _widest = 1;
    private double _available;

    // ---------------------------------------------------------------- the switcher

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(IsStartList))]
    [NotifyPropertyChangedFor(nameof(IsList))]
    public partial ParticipantMode Mode { get; set; } = ParticipantMode.Entries;

    /// <summary>The four chips, with the unavailable ones dimmed rather than hidden.</summary>
    public ObservableCollection<Segment> Modes { get; } = [];

    /// <summary>The split table is a surface of its own; every other mode is a plain list.</summary>
    public bool IsLive => Mode == ParticipantMode.Live;

    public bool IsList => Mode != ParticipantMode.Live;

    public bool IsStartList => Mode == ParticipantMode.StartList;

    // ---------------------------------------------------------------- the scope

    [ObservableProperty] public partial ParticipantScope Scope { get; set; } = ParticipantScope.MyClass;
    [ObservableProperty] public partial bool IsMyGroup { get; set; }
    [ObservableProperty] public partial bool IsMyClass { get; set; } = true;
    [ObservableProperty] public partial bool IsClass { get; set; }

    /// <summary>
    /// False in the two modes whose source has no way to find a person across classes: an entry
    /// list carries neither person ids nor start times, so "min grupp" there would be a filter
    /// with nothing to filter on.
    /// </summary>
    [ObservableProperty] public partial bool CanScopeToGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassChipText))]
    public partial string? SelectedClass { get; set; }

    public string ClassChipText => SelectedClass ?? "Välj klass";

    /// <summary>
    /// False for a competition whose classes Eventor does not list — a chip that opens an empty
    /// picker is worse than no chip.
    /// </summary>
    [ObservableProperty] public partial bool CanPickClass { get; set; }

    // ---------------------------------------------------------------- the start list's own order

    /// <summary>
    /// Whether the start list is ordered by the clock or by Sverigelistan.
    /// </summary>
    /// <remarks>
    /// Two different questions of the same list — "when do I go" and "who is the field" — and
    /// #119 built the second one before the first existed. The toggle keeps both rather than
    /// letting the mode's name quietly decide which of them survives.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortText))]
    public partial bool SortByTime { get; set; } = true;

    public string SortText => SortByTime ? "Starttid ⇅" : "Ranking ⇅";

    // ---------------------------------------------------------------- the page

    [ObservableProperty] public partial string CompetitionName { get; set; } = string.Empty;

    /// <summary>
    /// Whether there is a competition at all. Without one the switcher and the scope row have
    /// nothing to switch between, and a lone chip above an error reads as a page half-drawn.
    /// </summary>
    [ObservableProperty] public partial bool HasCompetition { get; set; }

    /// <summary>What the list is and how big it is: "36 anmälda i H21".</summary>
    [ObservableProperty] public partial string CaptionText { get; set; } = string.Empty;

    /// <summary>
    /// Whether the rows are the preliminary list rather than the published one. Never implied by
    /// styling alone — the badge says the word (D11).
    /// </summary>
    [ObservableProperty] public partial bool IsPreliminary { get; set; }

    /// <summary>Whether anybody is out on the course right now, which is what makes Live live.</summary>
    [ObservableProperty] public partial bool IsRunningNow { get; set; }

    [ObservableProperty] public partial string UpdatedText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty] public partial string EmptyHeading { get; set; } = string.Empty;
    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

    /// <summary>The list is a surface of its own; it must not cover the empty state.</summary>
    public bool HasRows => !IsEmpty;

    [ObservableProperty] public partial bool IsFromCache { get; set; }
    [ObservableProperty] public partial string CacheLabel { get; set; } = string.Empty;

    /// <summary>Livelox for the class on screen, when Livelox has one.</summary>
    [ObservableProperty] public partial bool HasLivelox { get; set; }

    [ObservableProperty] public partial string LiveloxText { get; set; } = string.Empty;

    [ObservableProperty] public partial double TableWidth { get; set; } = FrozenWidth + MinColumnWidth;

    [ObservableProperty] public partial double CellWidth { get; set; } = MinColumnWidth - ColumnGap;

    public ObservableCollection<ParticipantRow> Rows { get; } = [];

    /// <summary>
    /// The same rows, under the class they were run in. Min grupp spans classes, and a placing
    /// has no meaning outside its own.
    /// </summary>
    public ObservableCollection<ParticipantClassGroup> Groups { get; } = [];

    public Task OnNavigationParameterAsync(ParticipantsTarget param)
    {
        _target = param;
        return Task.CompletedTask;
    }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_target is null)
            return;

        await LoadAsync(OpenAsync);

        StartPolling();
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        StopPolling();
        return Task.CompletedTask;
    }

    protected override void ClearEmptyState() => IsEmpty = false;

    // ---------------------------------------------------------------- opening

    private async Task OpenAsync()
    {
        var target = _target!;

        _me ??= await _people.GetMeAsync();

        var group = await _people.GetMyGroupAsync();
        _meIdentity = RunnerIdentity.Of(_me.Name, _me.Club);
        _group = [.. group.Select(f => RunnerIdentity.Of(f.Person.Name, f.Person.Club))];

        // Through the offline package, like the competition page: with coverage this is live and
        // refreshes the stored copy, without it the stored copy is what keeps the start list
        // readable at the arena.
        _snapshot = await _offline.GetAsync(target.Competition);
        _competition = _snapshot.Competition;

        IsFromCache = _snapshot.Origin == DataOrigin.Cache;

        CacheLabel = _snapshot is { Origin: DataOrigin.Cache, CachedAt: { } cachedAt }
            ? $"Offline — sparat {Format.Clock(cachedAt)}"
            : string.Empty;

        if (_competition is null)
        {
            ShowNoCompetition(_snapshot.Origin);
            return;
        }

        CompetitionName = _competition.Name;
        HasCompetition = true;
        Title = _competition.Name;

        await AdoptClassesAsync(_competition, target.Class);

        _state = IsFromCache
            ? ContextEngine.Evaluate(new ContextInput
            {
                Now = _clock.Now,
                Competition = _competition,
                MyEntryRegisteredAt = _snapshot.MyEntryRegisteredAt,
                GroupEntryRegisteredAt = _snapshot.GroupEntryRegisteredAt,
                MyStartTime = _snapshot.MyStart?.StartTime,
            }).State
            : (await _context.EvaluateAsync(_competition)).State;

        // The calendar picks where to look first; the answer picks where to stay. A mode the
        // caller asked for outright is a decision already made and is not second-guessed.
        var opening = Decide();

        if (target.Mode is { } wanted)
        {
            _chosenByHand = true;
            Mode = wanted;
        }
        else
        {
            Mode = opening.Default;
        }

        await ShowModeAsync();

        // Now that a source has answered, the decision can be made on facts rather than on the
        // calendar. Only once, and never over a choice the reader or the caller already made.
        if (!_chosenByHand && Decide().Default is var settled && settled != Mode)
        {
            Mode = settled;
            await ShowModeAsync();
        }

        await LoadLiveloxAsync();
    }

    /// <summary>
    /// Works out what the switcher may offer, and redraws it when that has changed.
    /// </summary>
    /// <remarks>
    /// Called after every load rather than only at the start: availability is a fact about the
    /// sources, and the sources answer as the page runs. Redrawn only on a real change, because a
    /// live poll comes round every fifteen seconds and rebuilding four chips under the reader's
    /// finger each time is a switcher that flickers for no reason.
    /// </remarks>
    private ParticipantDecision Decide()
    {
        var decision = ParticipantModeEngine.Decide(new ParticipantInput
        {
            State = _state,
            Sightings = _sightings,
            IsRunningNow = IsRunningNow,
        });

        bool same = Modes.Count == decision.Modes.Count
            && decision.Modes.Zip(Modes).All(pair =>
                Equals(pair.First.Mode, pair.Second.Value) && pair.First.IsAvailable == pair.Second.IsEnabled);

        if (same)
            return decision;

        Modes.Clear();

        foreach (var offer in decision.Modes)
            Modes.Add(new Segment(offer.Text, offer.Mode, offer.IsAvailable));

        return decision;
    }

    /// <summary>
    /// The competition's classes and which one this reader is in.
    /// </summary>
    /// <remarks>
    /// Resolved in the same order the competition page uses, so the two can never show different
    /// classes for the same competition: what the caller asked for, then the entry, then the
    /// remembered choice, then the start, then the runner's own class.
    /// </remarks>
    private async Task AdoptClassesAsync(Competition competition, string? asked)
    {
        var detailed = IsFromCache ? null : await _events.GetCompetitionAsync(competition.Id);
        _classList = detailed?.Classes is { Count: > 0 } classes ? classes : competition.Classes;
        CanPickClass = _classList.Count > 0;

        var entries = IsFromCache ? [] : await _participation.GetEntriesAsync();
        var mine = entries.FirstOrDefault(e => e.Competition == competition.Id && e.Person == _me!.Id);

        SelectedClass = asked
            ?? mine?.Class
            ?? _classes.For(competition.Id)
            ?? _snapshot?.MyStart?.Class
            ?? _me!.DefaultClass;

        Scope = ParticipantScope.MyClass;
        IsMyClass = true;
        IsMyGroup = false;
        IsClass = false;
    }

    // ---------------------------------------------------------------- the four modes

    /// <summary>
    /// Loads whatever the current mode and scope ask for, and records what the source said.
    /// </summary>
    private async Task ShowModeAsync()
    {
        // Min grupp needs a source that can find a person across classes. Two of the four cannot.
        CanScopeToGroup = Mode is ParticipantMode.Live or ParticipantMode.Results;

        if (!CanScopeToGroup && Scope == ParticipantScope.MyGroup)
            ApplyScope(ParticipantScope.MyClass);

        IsPreliminary = false;
        CaptionText = string.Empty;
        UpdatedText = string.Empty;

        switch (Mode)
        {
            case ParticipantMode.Entries:
                await ShowEntriesAsync();
                break;
            case ParticipantMode.StartList:
                await ShowStartListAsync();
                break;
            case ParticipantMode.Live:
                await ShowLiveAsync();
                break;
            default:
                await ShowResultsAsync();
                break;
        }

        Regroup();

        IsEmpty = Rows.Count == 0;

        // The source has answered, so the switcher is redrawn on what it said. Here rather than
        // only at the start: every path into this method — opening, switching mode, changing
        // class or scope, a live poll — can be the one that learns a mode exists.
        Decide();

        if (IsEmpty)
            ExplainEmptiness();
    }

    private async Task ShowEntriesAsync()
    {
        if (ClassInScope() is not { } className)
            return;

        var entrants = await Sighted(
            ParticipantMode.Entries,
            () => _field.GetEntryListAsync(_competition!.Id, className));

        Replace(entrants.Select(runner => new ParticipantRow
        {
            // The entry list carries no person ids, so the reader is found the way the live lists
            // find them — by name and club (#75).
            Person = new PersonId($"entry:{className}:{runner.Name}"),
            Name = runner.Name,
            Club = runner.Club,
            Class = className,
            IsMe = RunnerIdentity.Of(runner.Name, runner.Club).Matches(_meIdentity),
            IsInMyGroup = IsGroup(runner.Name, runner.Club),
        }));

        foreach (var row in Rows)
            row.UpdateAccessibility();

        CaptionText = Rows.Count > 0
            ? $"{Rows.Count} anmälda i {className}"
            : string.Empty;
    }

    private async Task ShowStartListAsync()
    {
        if (ClassInScope() is not { } className)
            return;

        // Offline the package's own starts are what there is: the reader, and whoever they
        // follow. A partial list said to be partial beats no list at the arena.
        if (IsFromCache)
        {
            ShowCachedStarts(className);
            return;
        }

        var field = await Sighted(
            ParticipantMode.StartList,
            () => _field.GetStartFieldAsync(_competition!.Id, className));

        int ranked = field.Count(runner => runner.Points is not null);

        var ordered = SortByTime
            ? field.OrderBy(runner => runner.StartTime ?? DateTimeOffset.MaxValue).ThenBy(runner => runner.Name, StringComparer.CurrentCulture)
            : field.OrderBy(runner => runner.Points ?? double.MaxValue).ThenBy(runner => runner.StartTime ?? DateTimeOffset.MaxValue);

        int order = 0;

        Replace(ordered.Select(runner =>
        {
            order++;

            var row = new ParticipantRow
            {
                Person = runner.Person,
                Name = runner.Name,
                Club = runner.Club,
                Class = className,
                IsMe = runner.Person == _me!.Id
                       || RunnerIdentity.Of(runner.Name, runner.Club).Matches(_meIdentity),
                IsInMyGroup = IsGroup(runner.Name, runner.Club),

                // In start order the number is the order; in ranking order it is the standing on
                // Sverigelistan, and a runner the list does not carry has neither.
                LeadText = SortByTime
                    ? order.ToString(Format.Culture)
                    : runner.Points is null ? "—" : order.ToString(Format.Culture),

                ValueText = runner.StartTime is { } start ? Format.Clock(start) : "—",
                ValueDetailText = Detail(runner),
            };

            row.SpokenValue = runner.StartTime is { } spoken
                ? $"start {Format.Clock(spoken)}"
                : "ingen starttid";

            row.UpdateAccessibility();
            return row;
        }));

        await ExplainFieldAsync(className, ranked, field.Count);
    }

    private static string Detail(StartFieldRunner runner) => (runner.Points, runner.NationalRank) switch
    {
        ({ } points, { } rank) => $"{points.ToString("N2", Format.Culture)} · riks {rank}",
        ({ } points, null) => points.ToString("N2", Format.Culture),
        _ => string.Empty,
    };

    /// <summary>
    /// Why the points column is empty, when it is. "0 av 36 finns på listan" is true both when
    /// nobody is ranked and when nobody could be read, and the two mean entirely different
    /// things to the reader — if it is the login that is missing, the line says so.
    /// </summary>
    private async Task ExplainFieldAsync(string className, int ranked, int total)
    {
        if (total == 0)
            return;

        var access = ranked == 0 ? await _eventor.AccessAsync() : EventorAccess.Available;

        CaptionText = EventorMessage.Explains(access)
            ? EventorMessage.Detail(access, "Sverigelistan")
            : $"{total} i {className} · {ranked} finns på Sverigelistan";
    }

    private void ShowCachedStarts(string className)
    {
        var starts = new List<Start>(_snapshot!.GroupStarts);

        if (_snapshot.MyStart is { } mine && starts.All(s => s.Person != mine.Person))
            starts.Add(mine);

        _sightings = _sightings.Saw(
            ParticipantMode.StartList,
            starts.Count > 0 ? Sighting.Present : Sighting.Unknown);

        Replace(starts
            .Where(start => start.Class == className || Scope == ParticipantScope.MyGroup)
            .OrderBy(start => start.StartTime)
            .Select(start =>
            {
                var row = new ParticipantRow
                {
                    Person = start.Person,
                    Name = start.Person == _me!.Id ? _me.Name : NameOfGroupMember(start.Person),
                    Club = start.Person == _me.Id ? _me.Club : string.Empty,
                    Class = start.Class,
                    IsMe = start.Person == _me.Id,
                    IsInMyGroup = start.Person != _me.Id,
                    ValueText = Format.Clock(start.StartTime),
                };

                row.SpokenValue = $"start {Format.Clock(start.StartTime)}";
                row.UpdateAccessibility();
                return row;
            }));

        CaptionText = "Sparade starttider — du och din grupp. Hela startlistan kräver nätverk.";
    }

    private string NameOfGroupMember(PersonId person) => person.Value;

    private async Task ShowLiveAsync()
    {
        var snapshot = await Sighted(
            ParticipantMode.Live,
            async () =>
            {
                var read = await _live.GetSnapshotAsync(_competition!.Id, ClassInScope());
                _lastUpdate = read.GeneratedAt;
                AdoptControls(read);
                return read.Entries;
            });

        IsRunningNow = snapshot.Any(entry => entry.Status == LiveStatus.Running);

        var visible = snapshot
            .Where(InScope)
            .OrderBy(e => e.Class, StringComparer.CurrentCulture)
            .ThenBy(e => e.Status == LiveStatus.NotStarted ? 1 : 0)
            .ThenBy(e => e.Position ?? int.MaxValue)
            .ThenBy(e => e.StartTime ?? DateTimeOffset.MaxValue)
            .ToList();

        Replace(visible.Select(LiveRow));

        CaptionText = ScopeCaption(visible.Count);
        ShowAge();
    }

    private async Task ShowResultsAsync()
    {
        // The published list where there is one, and the live source's own view of the class
        // where there is not. Same mode, same list, and the badge says which (D11).
        if (ClassInScope() is { } className)
        {
            var official = await Sighted(
                ParticipantMode.Results,
                () => _participation.GetClassResultsAsync(_competition!.Id, className));

            if (official.Count > 0)
            {
                Replace(official.Select(ResultRow));
                CaptionText = $"{official.Count} i mål i {className}";
                return;
            }
        }

        var snapshot = await Sighted(
            ParticipantMode.Results,
            async () =>
            {
                var read = await _live.GetSnapshotAsync(_competition!.Id, ClassInScope());
                _lastUpdate = read.GeneratedAt;
                AdoptControls(read);
                return read.Entries;
            });

        IsRunningNow = snapshot.Any(entry => entry.Status == LiveStatus.Running);

        var finished = snapshot
            .Where(InScope)
            .OrderBy(e => e.Class, StringComparer.CurrentCulture)
            .ThenBy(e => e.FinalPlace ?? int.MaxValue)
            .ThenBy(e => e.FinishTime ?? TimeSpan.MaxValue)
            .ToList();

        if (finished.Count == 0)
            return;

        IsPreliminary = true;

        Replace(finished.Select(PreliminaryRow));

        CaptionText = ScopeCaption(finished.Count);
        ShowAge();
    }

    private string ScopeCaption(int count) => Scope switch
    {
        ParticipantScope.MyGroup => count == 1 ? "1 löpare i din grupp" : $"{count} löpare i din grupp",
        _ => ClassInScope() is { } className ? $"{count} i {className}" : $"{count} löpare",
    };

    // ---------------------------------------------------------------- rows

    private ParticipantRow LiveRow(LiveEntry entry)
    {
        var row = new ParticipantRow
        {
            Person = entry.Person,
            Name = entry.Name,
            Club = entry.Club,
            ClubLogo = entry.ClubLogo,
            Class = entry.Class,
            IsMe = IsMe(entry),
            IsInMyGroup = IsGroup(entry.Name, entry.Club),
            Cells = [.. Columns(entry.Class).Select(control => new ParticipantCell { Control = control })],
        };

        Apply(row, entry);
        return row;
    }

    private ParticipantRow PreliminaryRow(LiveEntry entry)
    {
        var row = new ParticipantRow
        {
            Person = entry.Person,
            Name = entry.Name,
            Club = entry.Club,
            ClubLogo = entry.ClubLogo,
            Class = entry.Class,
            IsMe = IsMe(entry),
            IsInMyGroup = IsGroup(entry.Name, entry.Club),
            LeadText = Format.PlaceNumber(entry.FinalPlace),
            MedalText = Format.Medal(entry.FinalPlace),
            ValueText = entry.FinishTime is { } time ? Format.Time(time) : "—",
            ValueDetailText = entry.FinishBehind is { Ticks: > 0 } behind ? Format.Delta(behind) : string.Empty,
        };

        row.StatusText = entry.Status switch
        {
            LiveStatus.Mispunch => "Felstämplat",
            LiveStatus.DidNotFinish => "Bröt",
            LiveStatus.Running => "Ute på banan",
            LiveStatus.NotStarted => entry.StartTime is { } start ? $"Start {Format.Clock(start)}" : "Ej start",
            _ => string.Empty,
        };

        row.HasStatus = row.StatusText.Length > 0;

        row.SpokenValue = string.Join(", ", new[]
        {
            Format.SpokenPlace(entry.FinalPlace),
            Format.SpokenTime(entry.FinishTime),
            Format.SpokenDelta(entry.FinishBehind),
        }.Where(part => part.Length > 0));

        row.UpdateAccessibility();
        return row;
    }

    private ParticipantRow ResultRow(CompetitionResult result)
    {
        var row = new ParticipantRow
        {
            Person = result.Person,
            Name = result.Name,
            Club = result.Club,
            ClubLogo = result.ClubLogo,
            Class = result.Class,
            IsMe = result.Person == _me!.Id
                   || RunnerIdentity.Of(result.Name, result.Club).Matches(_meIdentity),
            IsInMyGroup = IsGroup(result.Name, result.Club),
            LeadText = Format.PlaceNumber(result.Place),
            MedalText = Format.Medal(result.Place),
            ValueText = Format.Time(result.Time),
            ValueDetailText = result.BehindWinner is { Ticks: > 0 } behind ? Format.Delta(behind) : string.Empty,
            CanOpen = true,
        };

        row.StatusText = result.Status switch
        {
            ResultStatus.Mispunch => "Felstämplat",
            ResultStatus.DidNotFinish => "Bröt",
            ResultStatus.DidNotStart => "Ej start",
            ResultStatus.Preliminary => "Preliminärt",
            _ => string.Empty,
        };

        row.HasStatus = row.StatusText.Length > 0;

        row.SpokenValue = string.Join(", ", new[]
        {
            Format.SpokenPlace(result.Place),
            Format.SpokenTime(result.Time),
            Format.SpokenDelta(result.BehindWinner),
        }.Where(part => part.Length > 0));

        row.UpdateAccessibility();
        return row;
    }

    private void Apply(ParticipantRow row, LiveEntry entry)
    {
        // The table carries the race; the row only says what the table cannot.
        row.StatusText = entry.Status switch
        {
            LiveStatus.NotStarted => entry.StartTime is { } start ? $"Start {Format.Clock(start)}" : "Ej start",
            LiveStatus.Mispunch => "Felstämplat",
            LiveStatus.DidNotFinish => "Bröt",
            LiveStatus.Running when entry.Passings.Count == 0 => "Startad",
            _ => string.Empty,
        };

        row.HasStatus = row.StatusText.Length > 0;
        row.UpdateAccessibility();

        var controls = ControlsFor(entry.Class);

        for (int i = 0; i < controls.Count && i < row.Cells.Count; i++)
        {
            var passing = entry.Passings.FirstOrDefault(p => p.Control == controls[i].Code);
            row.Cells[i].Update(passing?.Elapsed, passing?.Place, passing?.Behind);
        }

        if (row.Cells.Count > 0)
            row.Cells[^1].Update(entry.FinishTime, entry.FinalPlace, entry.FinishBehind);
    }

    private void Replace(IEnumerable<ParticipantRow> rows)
    {
        Rows.Clear();

        foreach (var row in rows)
            Rows.Add(row);
    }

    // ---------------------------------------------------------------- what the source said

    /// <summary>
    /// Runs one mode's fetch and writes down what came back.
    /// </summary>
    /// <remarks>
    /// The three answers are kept apart on purpose (D10): rows, an empty list, and an outage are
    /// three different facts, and only the first two say anything about the competition. An
    /// unreachable source leaves the sighting untouched, which is what keeps a mode that has
    /// worked once from greying out on the walk to the start.
    /// </remarks>
    private async Task<IReadOnlyList<T>> Sighted<T>(ParticipantMode mode, Func<Task<IReadOnlyList<T>>> load)
    {
        try
        {
            var rows = await load();

            _sightings = _sightings.Saw(mode, rows.Count > 0 ? Sighting.Present : Sighting.Absent);

            return rows;
        }
        catch (SourceUnavailableException)
        {
            IsOffline = true;
            return [];
        }
    }

    private void ExplainEmptiness()
    {
        var offer = Decide()[Mode];

        if (IsOffline)
        {
            EmptyHeading = "Ingen anslutning";

            EmptyMessage = Mode == ParticipantMode.Live
                ? "Live behöver nätverk. Dina och din grupps starttider finns sparade."
                : "Listan kunde inte hämtas, och den finns inte sparad offline.";

            return;
        }

        EmptyHeading = offer.IsAvailable ? "Ingen i det här urvalet" : $"{offer.Text} saknas";

        EmptyMessage = offer.IsAvailable
            ? "Byt klass eller urval för att se resten av fältet."
            : $"Listan {offer.ConditionText}.";
    }

    /// <summary>
    /// There is no competition to list anybody from, and the two reasons read differently.
    /// </summary>
    /// <remarks>
    /// A race the calendar does not carry is not an outage. Mina resultat reaches back through the
    /// runner's whole Eventor history and the calendar covers a few months, so opening a race from
    /// last winter lands here on a perfectly good connection — and "ingen anslutning" would
    /// contradict the list it was opened from.
    /// </remarks>
    private void ShowNoCompetition(DataOrigin origin)
    {
        Rows.Clear();
        Groups.Clear();
        Modes.Clear();
        HasCompetition = false;
        CanScopeToGroup = false;
        CanPickClass = false;
        CaptionText = string.Empty;
        IsEmpty = true;

        (EmptyHeading, EmptyMessage) = origin == DataOrigin.Missing
            ? ("Tävlingen finns inte i kalendern",
               "Den ligger utanför de månader appen läser, så deltagarlistan går inte att öppna. "
               + "Raden i Mina resultat visar tid och placering.")
            : ("Kunde inte hämta tävlingen",
               "Ingen anslutning, och tävlingen finns inte sparad offline.");
    }

    // ---------------------------------------------------------------- commands

    [RelayCommand]
    private async Task SelectMode(ParticipantMode mode)
    {
        if (mode == Mode)
            return;

        _chosenByHand = true;
        Mode = mode;

        await LoadAsync(ShowModeAsync);

        StartPolling();

        SemanticScreenReader.Default.Announce($"{Modes.First(s => Equals(s.Value, mode)).Text}, {Rows.Count} löpare");
    }

    [RelayCommand]
    private async Task SelectScope(string scope)
    {
        ApplyScope(Enum.Parse<ParticipantScope>(scope));

        await LoadAsync(ShowModeAsync);

        SemanticScreenReader.Default.Announce($"{ScopeLabel}, {Rows.Count} löpare");
    }

    [RelayCommand]
    private async Task PickClass()
    {
        if (_competition is not { } competition || _classList.Count == 0)
            return;

        var choice = await _navigation.NavigateToWithResultAsync<ChooseClassSheet, ClassChoice, string>(
            new ClassChoice(_classList, "Deltagarlistan visar klassen du väljer.", SelectedClass));

        if (choice is not { IsSuccess: true, Value: { } className } || className == SelectedClass)
            return;

        SelectedClass = className;

        // The same store the competition page reads, so the two can never disagree about which
        // class this reader is in.
        _classes.Save(competition.Id, className);

        // The answers on file were about the class that just left the screen.
        _sightings = new ParticipantSightings();

        ApplyScope(ParticipantScope.Class);

        await LoadAsync(ShowModeAsync);
        await LoadLiveloxAsync();
    }

    [RelayCommand]
    private async Task ToggleSort()
    {
        SortByTime = !SortByTime;
        await LoadAsync(ShowModeAsync);
    }

    /// <summary>
    /// Opens the runner behind a row: their splits, their legs, what the analysis makes of them.
    /// </summary>
    /// <remarks>
    /// The second level of the redesign. The switcher above is about the field; this is about one
    /// person in it, and keeping the two apart is what lets any row be opened rather than only
    /// the reader's own.
    /// </remarks>
    [RelayCommand]
    private async Task OpenRunner(ParticipantRow row)
    {
        if (!row.CanOpen || _competition is null)
            return;

        await _navigation.NavigateToAsync<Results.RunnerResultPage, Results.RunnerResultTarget>(
            new Results.RunnerResultTarget(_competition.Id, row.Class, row.Person));
    }

    [RelayCommand]
    private async Task OpenLivelox()
    {
        if (_liveloxLink is null)
            return;

        // The class' own link when Livelox has one, the event's otherwise. Both are doors, and
        // the nearer door is the one the reader is standing at.
        string url = ClassInScope() is { } className
            && _liveloxLink.Classes.FirstOrDefault(c => c.Name == className) is { } theirs
                ? theirs.Url
                : _liveloxLink.Url;

        if (url.Length > 0)
            await Launcher.OpenAsync(url);
    }

    private void ApplyScope(ParticipantScope scope)
    {
        Scope = scope;
        IsMyGroup = scope == ParticipantScope.MyGroup;
        IsMyClass = scope == ParticipantScope.MyClass;
        IsClass = scope == ParticipantScope.Class;

        _sightings = new ParticipantSightings();
    }

    private string ScopeLabel => Scope switch
    {
        ParticipantScope.MyGroup => "Min grupp",
        ParticipantScope.MyClass => "Min klass",
        _ => ClassChipText,
    };

    // ---------------------------------------------------------------- Livelox

    /// <summary>
    /// Looks the competition up in Livelox, and the class inside it.
    /// </summary>
    /// <remarks>
    /// A link, and only a link. Livelox keeps maps and routes deliberately — for copyright,
    /// attribution and privacy — and no API returns them. What is new here is the class: the
    /// event's answer has carried a url per class all along, and the page that knows which class
    /// the reader is in is this one.
    /// </remarks>
    private async Task LoadLiveloxAsync()
    {
        _liveloxLink = null;
        HasLivelox = false;

        if (_competition is null || IsFromCache)
            return;

        try
        {
            _liveloxLink = await _livelox.GetLiveloxAsync(_competition.Id);
        }
        catch (SourceUnavailableException)
        {
            return;
        }

        if (_liveloxLink is not { } link || (!link.HasMap && link.Participants == 0))
            return;

        HasLivelox = true;

        LiveloxText = ClassInScope() is { } className && link.Classes.Any(c => c.Name == className)
            ? $"Vägval i {className} på Livelox"
            : link.Participants > 0
                ? $"{link.Participants} löpares vägval i Livelox"
                : "Karta och banor i Livelox";
    }

    // ---------------------------------------------------------------- scope helpers

    /// <summary>The class to ask the source for, or null when the scope spans all of them.</summary>
    private string? ClassInScope() => Scope == ParticipantScope.MyGroup ? null : SelectedClass;

    private bool InScope(LiveEntry entry) => Scope switch
    {
        ParticipantScope.MyGroup => IsGroup(entry.Name, entry.Club) || IsMe(entry),
        _ => entry.Class == SelectedClass,
    };

    private bool IsMe(LiveEntry entry) => _meIdentity.Matches(RunnerIdentity.Of(entry.Name, entry.Club));

    private bool IsGroup(string name, string club)
    {
        var identity = RunnerIdentity.Of(name, club);
        return _group.Any(member => member.Matches(identity));
    }

    // ---------------------------------------------------------------- the split table's geometry

    private bool AdoptControls(LiveSnapshot snapshot)
    {
        bool changed =
            snapshot.Controls.Count != _controls.Count
            || snapshot.Controls.Any(pair =>
                !_controls.TryGetValue(pair.Key, out var known)
                || !known.Select(c => c.Code).SequenceEqual(pair.Value.Select(c => c.Code)));

        if (changed)
            _controls = snapshot.Controls;

        return changed;
    }

    /// <summary>
    /// Rebuilt whenever the rows are. The columns only exist in Live, so every other mode groups
    /// its classes with an empty heading row.
    /// </summary>
    private void Regroup()
    {
        Groups.Clear();

        foreach (var byClass in Rows.GroupBy(r => r.Class))
        {
            var group = new ParticipantClassGroup(byClass.Key, IsLive ? Columns(byClass.Key) : []);

            foreach (var row in byClass)
                group.Add(row);

            Groups.Add(group);
        }

        _widest = Groups.Count > 0 ? Groups.Max(g => Math.Max(1, g.Columns.Count)) : 1;
        Measure();
    }

    /// <summary>The width of the area the table is laid out in, once the view knows it.</summary>
    public void Fit(double available)
    {
        if (available <= 0 || Math.Abs(available - _available) < 0.5)
            return;

        _available = available;
        Measure();
    }

    private void Measure()
    {
        double room = _available > 0 ? (_available - FrozenWidth) / _widest : MinColumnWidth;
        double column = Math.Max(MinColumnWidth, room);

        CellWidth = column - ColumnGap;
        TableWidth = FrozenWidth + (_widest * column);
    }

    private IReadOnlyList<string> Columns(string className) =>
        [.. ControlsFor(className).Select(c => c.Name), "Mål"];

    private IReadOnlyList<LiveControl> ControlsFor(string className) =>
        _controls.TryGetValue(className, out var controls) ? controls : [];

    // ---------------------------------------------------------------- polling

    /// <summary>
    /// Polls only where the figures move: while the race is on, and while the result list on
    /// screen is still the preliminary one. A published list has stopped changing.
    /// </summary>
    private void StartPolling()
    {
        StopPolling();

        if (Mode != ParticipantMode.Live && !(Mode == ParticipantMode.Results && IsPreliminary))
            return;

        var cts = new CancellationTokenSource();
        _polling = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                // One tick a second, one fetch per PollInterval. The age is the only thing that
                // has to move between fetches: "Uppdaterad för 0 sek sedan" that never counts up
                // reads as a frozen page, which is the one thing a live view must not look like.
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                int elapsed = 0;

                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    elapsed++;

                    bool fetch = elapsed % (int)PollInterval.TotalSeconds == 0;

                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (fetch)
                            await LoadAsync(ShowModeAsync);
                        else
                            ShowAge();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // The page was left.
            }
        }, cts.Token);
    }

    private void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    /// <summary>How old the figures on screen are, counted from when the source generated them.</summary>
    private void ShowAge() =>
        UpdatedText = _lastUpdate == default
            ? string.Empty
            : $"Uppdaterad för {Format.Age(_clock.Now - _lastUpdate)} sedan";
}
