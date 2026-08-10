using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Presentation;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Live;

public enum LiveScope
{
    MyGroup,
    MyClass,
    Everyone,
}

public sealed partial class LiveRow : ObservableObject
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string Class { get; init; }

    /// <summary>The row for the user gets an accent tone, per the live-list design rule.</summary>
    public required bool IsMe { get; init; }

    public required bool IsInMyGroup { get; init; }

    [ObservableProperty] public partial string PositionText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProgressText { get; set; } = string.Empty;
    [ObservableProperty] public partial string TimeText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsFinished { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool HasNotStarted { get; set; }

    public string GroupGlyph => IsInMyGroup ? "★" : string.Empty;

    /// <summary>
    /// The row as one spoken line. Times are spelled out — "40:43" is read as a clock time,
    /// which is wrong for an elapsed race time.
    /// </summary>
    [ObservableProperty]
    public partial string Accessibility { get; set; } = string.Empty;

    public void UpdateAccessibility(int? place, TimeSpan? time)
    {
        var parts = new List<string>(6);

        if (IsMe)
            parts.Add("du");

        parts.Add(Name);

        if (IsInMyGroup)
            parts.Add("i min grupp");

        parts.Add($"{Club}, klass {Class}");

        if (place is not null)
            parts.Add(Format.SpokenPlace(place));

        parts.Add(ProgressText);

        if (time is not null)
            parts.Add(Format.SpokenTime(time));

        Accessibility = string.Join(", ", parts);
    }
}

public partial class LivePageViewModel(
    IClock _clock,
    ILiveSource _live,
    IPeopleSource _people) : OrienteraViewModel
{
    /// <summary>LiveResults caches for 15 seconds, so polling faster only wastes data.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _polling;
    private Competition? _competition;
    private Person? _me;
    private IReadOnlyList<RunnerIdentity> _group = [];
    private RunnerIdentity _meIdentity;
    private DateTimeOffset _lastUpdate;

    public ObservableCollection<LiveRow> Rows { get; } = [];

    public IReadOnlyList<string> ScopeLabels { get; } = ["Min grupp", "Min klass", "Alla"];

    [ObservableProperty] public partial LiveScope Scope { get; set; } = LiveScope.MyGroup;
    [ObservableProperty] public partial bool IsMyGroup { get; set; } = true;
    [ObservableProperty] public partial bool IsMyClass { get; set; }
    [ObservableProperty] public partial bool IsEveryone { get; set; }

    [ObservableProperty] public partial string CompetitionName { get; set; } = string.Empty;
    [ObservableProperty] public partial string UpdatedText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasLive { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }
    [ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

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
    private async Task SelectScope(string scope)
    {
        Scope = Enum.Parse<LiveScope>(scope);
        IsMyGroup = Scope == LiveScope.MyGroup;
        IsMyClass = Scope == LiveScope.MyClass;
        IsEveryone = Scope == LiveScope.Everyone;

        await LoadAsync(RefreshAsync);

        if (IsOffline)
            ShowOffline();

        // The list is replaced under the reader's cursor; say what it now shows.
        SemanticScreenReader.Default.Announce($"{ScopeLabels[(int)Scope]}, {Rows.Count} löpare");
    }

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

        // One class is one upstream request; the other scopes have to look across all of them,
        // because Min grupp does not run in a single class.
        var snapshot = await _live.GetSnapshotAsync(
            _competition.Id,
            Scope == LiveScope.MyClass ? _me.DefaultClass : null);
        _lastUpdate = snapshot.GeneratedAt;

        var visible = snapshot.Entries
            .Where(InScope)
            .OrderBy(e => e.Status == LiveStatus.NotStarted ? 1 : 0)
            .ThenBy(e => e.Position ?? int.MaxValue)
            .ThenBy(e => e.StartTime)
            .ToList();

        Merge(visible);

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

    private bool InScope(LiveEntry entry) => Scope switch
    {
        LiveScope.MyGroup => IsGroup(entry) || IsMe(entry),
        LiveScope.MyClass => entry.Class == _me!.DefaultClass,
        _ => true,
    };

    private bool IsMe(LiveEntry entry) => _meIdentity.Matches(RunnerIdentity.Of(entry.Name, entry.Club));

    private bool IsGroup(LiveEntry entry)
    {
        var identity = RunnerIdentity.Of(entry.Name, entry.Club);
        return _group.Any(member => member.Matches(identity));
    }

    /// <summary>
    /// Updates the existing rows in place where possible. Live updates animate the value,
    /// never the layout — rebuilding the collection would make the list jump on every poll.
    /// </summary>
    private void Merge(IReadOnlyList<LiveEntry> entries)
    {
        if (Rows.Count != entries.Count || !Rows.Select(r => r.Person).SequenceEqual(entries.Select(e => e.Person)))
        {
            Rows.Clear();

            foreach (var entry in entries)
                Rows.Add(CreateRow(entry));

            return;
        }

        for (int i = 0; i < entries.Count; i++)
            Apply(Rows[i], entries[i]);
    }

    private LiveRow CreateRow(LiveEntry entry)
    {
        var row = new LiveRow
        {
            Person = entry.Person,
            Name = entry.Name,
            Club = entry.Club,
            Class = entry.Class,
            IsMe = IsMe(entry),
            IsInMyGroup = IsGroup(entry),
        };

        Apply(row, entry);
        return row;
    }

    private static void Apply(LiveRow row, LiveEntry entry)
    {
        row.IsFinished = entry.Status is LiveStatus.Finished or LiveStatus.Mispunch;
        row.IsRunning = entry.Status == LiveStatus.Running;
        row.HasNotStarted = entry.Status == LiveStatus.NotStarted;

        int? place = entry.Status switch
        {
            LiveStatus.Finished => entry.FinalPlace,
            LiveStatus.Mispunch or LiveStatus.NotStarted => null,
            _ => entry.Position,
        };

        row.PositionText = place is { } p ? Format.Place(p) : "—";

        row.ProgressText = entry.Status switch
        {
            LiveStatus.NotStarted => $"Start {Format.Clock(entry.StartTime)}",
            LiveStatus.Mispunch => "Felstämplat",
            LiveStatus.Finished => "I mål",
            _ => entry.LastControlNumber is { } control ? $"Kontroll {control}" : "Startat",
        };

        var time = entry.Status switch
        {
            LiveStatus.Finished => entry.FinishTime,
            LiveStatus.NotStarted => null,
            _ => entry.ElapsedAtLastControl,
        };

        row.TimeText = time is { } t ? Format.Time(t) : "—";
        row.UpdateAccessibility(place, time);
    }
}
