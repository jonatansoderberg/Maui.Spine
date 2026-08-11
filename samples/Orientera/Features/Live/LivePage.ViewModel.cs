using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Features.Events;
using Orientera.Presentation;
using Orientera.Services.Local;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Live;

public enum LiveScope
{
    MyGroup,
    MyClass,

    /// <summary>One class the user picked, which is the only way to reach the rest of the field.</summary>
    Class,
}

/// <summary>
/// One runner at one radio control: accumulated time, the standing at that control, and the
/// time behind whoever leads it. The finish is the last column and reads the same way.
/// </summary>
public sealed partial class LiveCell : ObservableObject
{
    /// <summary>The control as it is written in the forest, or "Mål" for the finish column.</summary>
    public required string Control { get; init; }

    [ObservableProperty] public partial string TimeText { get; set; } = "—";

    /// <summary>Place and time behind on one line: "(3) +1:07".</summary>
    [ObservableProperty] public partial string DetailText { get; set; } = string.Empty;

    /// <summary>The control's leader, marked in the accent colour.</summary>
    [ObservableProperty] public partial bool IsLeading { get; set; }

    /// <summary>
    /// A cell is its own element to a screen reader — a row of twelve unlabelled numbers is
    /// unreadable, so every cell says which control it belongs to.
    /// </summary>
    [ObservableProperty] public partial string Accessibility { get; set; } = string.Empty;

    public void Update(TimeSpan? time, int? place, TimeSpan? behind)
    {
        TimeText = time is { } t ? Format.Time(t) : "—";
        IsLeading = place == 1;

        DetailText = (place, behind) switch
        {
            (null, _) => string.Empty,
            ({ } p, { Ticks: > 0 } b) => $"({p}) {Format.Delta(b)}",
            ({ } p, _) => $"({p})",
        };

        Accessibility = time is null
            ? $"{Control}, ingen tid"
            : string.Join(", ", new[]
            {
                Control,
                Format.SpokenTime(time),
                place is null ? null : Format.SpokenPlace(place),
                behind is { Ticks: > 0 } ? Format.SpokenDelta(behind) : null,
            }.OfType<string>());
    }
}

public sealed partial class LiveRow : ObservableObject
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }

    /// <summary>The club's badge, or null for a club that has not uploaded one.</summary>
    public string? ClubLogo { get; init; }

    public bool HasClubLogo => !string.IsNullOrEmpty(ClubLogo);
    public required string Class { get; init; }

    /// <summary>The row for the user gets an accent tone, per the live-list design rule.</summary>
    public required bool IsMe { get; init; }

    public required bool IsInMyGroup { get; init; }

    /// <summary>
    /// One cell per column of the class' split table, in course order with the finish last.
    /// The collection is built once per row: a poll writes new values into the cells, so the
    /// table never relays under a finger that is scrolling it.
    /// </summary>
    public required IReadOnlyList<LiveCell> Cells { get; init; }

    /// <summary>Only what the table cannot say: a start time, a broken race, a mispunch.</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasStatus { get; set; }

    public string GroupGlyph => IsInMyGroup ? "★" : string.Empty;

    /// <summary>
    /// Who the row is, for a screen reader. The numbers are in the cells, each of which is read
    /// as its own element with the control it belongs to.
    /// </summary>
    [ObservableProperty]
    public partial string Accessibility { get; set; } = string.Empty;

    public void UpdateAccessibility()
    {
        var parts = new List<string>(5);

        if (IsMe)
            parts.Add("du");

        parts.Add(Name);

        if (IsInMyGroup)
            parts.Add("i min grupp");

        parts.Add($"{Club}, klass {Class}");

        if (HasStatus)
            parts.Add(StatusText);

        Accessibility = string.Join(", ", parts);
    }
}

/// <summary>One class' rows, with the class and its radio controls as the table's heading.</summary>
public sealed class LiveClassGroup(string _name, IReadOnlyList<string> _columns) : ObservableCollection<LiveRow>
{
    public string Name => _name;

    /// <summary>The column headings: each radio control, then the finish.</summary>
    public IReadOnlyList<string> Columns => _columns;

    public string Accessibility => $"Klass {_name}";
}

public partial class LivePageViewModel(
    IClock _clock,
    ILiveSource _live,
    IEventSource _events,
    IPeopleSource _people,
    INavigationService _navigation,
    CompetitionClassStore _classes) : OrienteraViewModel
{
    /// <summary>LiveResults caches for 15 seconds, so polling faster only wastes data.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The name column stays put while the controls scroll past it, so how wide the table is
    /// depends on layout. These two are the same numbers the view lays the columns out with
    /// (<c>LivePage.View.xaml</c>); the table would scroll to the wrong place if they drifted.
    /// </summary>
    public const double FrozenWidth = 156;

    /// <summary>Narrowest a column may be — a finish time with its place and time behind under it.</summary>
    public const double MinColumnWidth = 82;

    /// <summary>The gap between columns, which the view carries as each cell's right margin.</summary>
    private const double ColumnGap = 10;

    private CancellationTokenSource? _polling;
    private Competition? _competition;
    private Person? _me;
    private IReadOnlyList<RunnerIdentity> _group = [];
    private RunnerIdentity _meIdentity;
    private DateTimeOffset _lastUpdate;
    private IReadOnlyDictionary<string, IReadOnlyList<LiveControl>> _controls =
        new Dictionary<string, IReadOnlyList<LiveControl>>();

    /// <summary>The competition whose classes have been read, so a poll does not read them again.</summary>
    private CompetitionId? _adopted;

    private IReadOnlyList<string> _classList = [];
    private int _widest = 1;
    private double _available;

    public ObservableCollection<LiveRow> Rows { get; } = [];

    /// <summary>
    /// The same rows, under the class they are placed in. Live spans classes in every scope but
    /// "min klass", and a placing has no meaning outside its own.
    /// </summary>
    public ObservableCollection<LiveClassGroup> Groups { get; } = [];

    [ObservableProperty] public partial LiveScope Scope { get; set; } = LiveScope.MyGroup;
    [ObservableProperty] public partial bool IsMyGroup { get; set; } = true;
    [ObservableProperty] public partial bool IsMyClass { get; set; }
    [ObservableProperty] public partial bool IsClass { get; set; }

    /// <summary>
    /// The picked class, or null before anything is picked. The competition's own classes are
    /// what the picker offers, so this is always one of them.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClassChipText))]
    public partial string? SelectedClass { get; set; }

    public string ClassChipText => SelectedClass ?? "Välj klass";

    /// <summary>
    /// False for a competition whose classes Eventor does not list — a chip that opens an empty
    /// picker is worse than no chip.
    /// </summary>
    [ObservableProperty] public partial bool CanPickClass { get; set; }

    [ObservableProperty] public partial string CompetitionName { get; set; } = string.Empty;
    [ObservableProperty] public partial string UpdatedText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasLive { get; set; }

    /// <summary>
    /// How wide the table is: every column laid out, but never narrower than the screen it sits
    /// on. A class with one column would otherwise leave a third of the row empty.
    /// </summary>
    [ObservableProperty] public partial double TableWidth { get; set; } = FrozenWidth + MinColumnWidth;

    /// <summary>
    /// What one cell may draw in, without its gap. Columns widen so that the class with the most
    /// controls fills the row; past that they keep their width and the table scrolls.
    /// </summary>
    [ObservableProperty] public partial double CellWidth { get; set; } = MinColumnWidth - ColumnGap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

    /// <summary>The table is a sheet of its own; it must not cover the empty state.</summary>
    public bool HasRows => !IsEmpty;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        await LoadAsync(async () =>
        {
            _me ??= await _people.GetMeAsync();

            var group = await _people.GetMyGroupAsync();

            // The live source has names and clubs, not person ids — it is a different system
            // with a different idea of who people are, so this is the only comparison that
            // works across both it and the seeded data (SP-04).
            _meIdentity = RunnerIdentity.Of(_me.Name, _me.Club);
            _group = [.. group.Select(f => RunnerIdentity.Of(f.Person.Name, f.Person.Club))];

            await RefreshAsync();
        });

        if (IsOffline)
            ShowOffline();

        StartPolling();
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        StopPolling();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SelectScope(string scope) => await ApplyScopeAsync(Enum.Parse<LiveScope>(scope));

    /// <summary>
    /// The class chip is a picker, not a filter: tapping it always asks which class, because the
    /// competition has forty of them and the answer is the whole point of the chip.
    /// </summary>
    [RelayCommand]
    private async Task PickClass()
    {
        if (_competition is not { } competition || _classList.Count == 0)
            return;

        var choice = await _navigation.NavigateToWithResultAsync<ChooseClassSheet, ClassChoice, string>(
            new ClassChoice(_classList, "Livelistan visar klassen du väljer."));

        if (choice is not { IsSuccess: true, Value: { } className })
            return;

        SelectedClass = className;
        _classes.Save(competition.Id, className);

        await ApplyScopeAsync(LiveScope.Class);
    }

    private async Task ApplyScopeAsync(LiveScope scope)
    {
        Scope = scope;
        IsMyGroup = scope == LiveScope.MyGroup;
        IsMyClass = scope == LiveScope.MyClass;
        IsClass = scope == LiveScope.Class;

        await LoadAsync(RefreshAsync);

        if (IsOffline)
            ShowOffline();

        // The list is replaced under the reader's cursor; say what it now shows.
        SemanticScreenReader.Default.Announce($"{ScopeLabel}, {Rows.Count} löpare");
    }

    private string ScopeLabel => Scope switch
    {
        LiveScope.MyGroup => "Min grupp",
        LiveScope.MyClass => "Min klass",
        _ => ClassChipText,
    };

    /// <summary>
    /// Polls on the same cadence as the upstream cache. Only the changing values on each row
    /// are written back, so the list never rebuilds under the user's finger.
    /// </summary>
    private void StartPolling()
    {
        StopPolling();

        var cts = new CancellationTokenSource();
        _polling = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(PollInterval);

                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        // A poll that fails must not take the app down mid-race.
                        await LoadAsync(RefreshAsync);

                        if (IsOffline)
                            ShowOffline();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Tab left the screen.
            }
        }, cts.Token);
    }

    private void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    private async Task RefreshAsync()
    {
        if (_me is null)
            return;

        var liveCompetitions = await _live.GetLiveCompetitionsAsync();
        _competition = liveCompetitions.FirstOrDefault();

        HasLive = _competition is not null;

        if (_competition is null)
        {
            Rows.Clear();
            IsEmpty = true;
            CompetitionName = string.Empty;
            EmptyMessage = "Ingen tävling pågår just nu. Flytta klockan i tidsmaskinen för att se live-läget.";
            return;
        }

        CompetitionName = _competition.Name;
        await AdoptCompetitionAsync(_competition);

        // One class is one upstream request; Min grupp is the only scope that has to look across
        // all of them, because a group does not run in a single class.
        var snapshot = await _live.GetSnapshotAsync(_competition.Id, ClassInScope());
        _lastUpdate = snapshot.GeneratedAt;
        bool columnsChanged = AdoptControls(snapshot);

        // A place means something inside its class, so the class orders the list and the
        // place orders the class.
        var visible = snapshot.Entries
            .Where(InScope)
            .OrderBy(e => e.Class, StringComparer.CurrentCulture)
            .ThenBy(e => e.Status == LiveStatus.NotStarted ? 1 : 0)
            .ThenBy(e => e.Position ?? int.MaxValue)
            // A runner the source has no start time for goes last, which is where their status
            // already puts them.
            .ThenBy(e => e.StartTime ?? DateTimeOffset.MaxValue)
            .ToList();

        if (Merge(visible, columnsChanged))
            Regroup();

        IsEmpty = Rows.Count == 0;
        EmptyMessage = "Ingen i det här urvalet är ute på banan.";
        UpdatedText = $"Uppdaterad för {Format.Age(_clock.Now - _lastUpdate)} sedan";
    }

    /// <summary>Live is the one screen a cached copy cannot stand in for — it is only useful now.</summary>
    private void ShowOffline()
    {
        Rows.Clear();
        HasLive = false;
        IsEmpty = true;
        CompetitionName = string.Empty;
        EmptyMessage = "Ingen anslutning. Live behöver nätverk — starttider för dig och Min grupp finns sparade på tävlingssidan.";
    }

    /// <summary>
    /// The competition's classes and the class the user last followed in it — read once per
    /// competition, not on every poll.
    /// </summary>
    /// <remarks>
    /// The live list only carries the calendar's projection of a competition, and that one has no
    /// classes; they come with the competition's own page. Both sides cache it, so asking costs
    /// nothing after the first time.
    /// </remarks>
    private async Task AdoptCompetitionAsync(Competition competition)
    {
        if (_adopted == competition.Id)
            return;

        _adopted = competition.Id;

        var detailed = await _events.GetCompetitionAsync(competition.Id);
        _classList = detailed?.Classes is { Count: > 0 } classes ? classes : competition.Classes;
        CanPickClass = _classList.Count > 0;

        // A remembered class the competition no longer lists is not a class any more.
        if (_classes.For(competition.Id) is not { } remembered || !_classList.Contains(remembered))
            return;

        SelectedClass = remembered;
        Scope = LiveScope.Class;
        IsMyGroup = false;
        IsClass = true;
    }

    /// <summary>The class to ask the source for, or null when the scope spans all of them.</summary>
    private string? ClassInScope() => Scope switch
    {
        LiveScope.MyClass => _me!.DefaultClass,
        LiveScope.Class => SelectedClass,
        _ => null,
    };

    private bool InScope(LiveEntry entry) => Scope switch
    {
        LiveScope.MyGroup => IsGroup(entry) || IsMe(entry),
        LiveScope.MyClass => entry.Class == _me!.DefaultClass,
        _ => entry.Class == SelectedClass,
    };

    private bool IsMe(LiveEntry entry) => _meIdentity.Matches(RunnerIdentity.Of(entry.Name, entry.Club));

    private bool IsGroup(LiveEntry entry)
    {
        var identity = RunnerIdentity.Of(entry.Name, entry.Club);
        return _group.Any(member => member.Matches(identity));
    }

    /// <summary>
    /// Takes the snapshot's radio controls and says whether the columns moved. They only do so
    /// when the competition changes or an organiser adds a radio mid-race, and a column that
    /// appears has to rebuild the rows that were built without it.
    /// </summary>
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
    /// Updates the existing rows in place where possible. Live updates animate the value,
    /// never the layout — rebuilding the collection would make the list jump on every poll.
    /// </summary>
    /// <summary>Returns true when the rows themselves changed, not just their values.</summary>
    private bool Merge(IReadOnlyList<LiveEntry> entries, bool columnsChanged)
    {
        if (columnsChanged
            || Rows.Count != entries.Count
            || !Rows.Select(r => r.Person).SequenceEqual(entries.Select(e => e.Person)))
        {
            Rows.Clear();

            foreach (var entry in entries)
                Rows.Add(CreateRow(entry));

            return true;
        }

        for (int i = 0; i < entries.Count; i++)
            Apply(Rows[i], entries[i]);

        return false;
    }

    /// <summary>
    /// Rebuilt only when the field changed. The row objects are reused, so a poll that only
    /// moves times and places updates through the bindings and never touches the layout.
    /// </summary>
    private void Regroup()
    {
        Groups.Clear();

        foreach (var byClass in Rows.GroupBy(r => r.Class))
        {
            var group = new LiveClassGroup(byClass.Key, Columns(byClass.Key));

            foreach (var row in byClass)
                group.Add(row);

            Groups.Add(group);
        }

        _widest = Groups.Count > 0 ? Groups.Max(g => g.Columns.Count) : 1;
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

    /// <summary>The class' radio controls as headings, with the finish as the last column.</summary>
    private IReadOnlyList<string> Columns(string className) =>
        [.. ControlsFor(className).Select(c => c.Name), "Mål"];

    private IReadOnlyList<LiveControl> ControlsFor(string className) =>
        _controls.TryGetValue(className, out var controls) ? controls : [];

    private LiveRow CreateRow(LiveEntry entry)
    {
        var row = new LiveRow
        {
            Person = entry.Person,
            Name = entry.Name,
            Club = entry.Club,
            ClubLogo = entry.ClubLogo,
            Class = entry.Class,
            IsMe = IsMe(entry),
            IsInMyGroup = IsGroup(entry),
            Cells = [.. Columns(entry.Class).Select(control => new LiveCell { Control = control })],
        };

        Apply(row, entry);
        return row;
    }

    private void Apply(LiveRow row, LiveEntry entry)
    {
        // The table carries the race; the row only says what the table cannot.
        row.StatusText = entry.Status switch
        {
            // No start time is the source saying this runner never started, not that they start
            // at midnight.
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
}
