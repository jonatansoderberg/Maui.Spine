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
}

public partial class LivePageViewModel(
    IClock _clock,
    ILiveSource _live,
    IPeopleSource _people) : ViewModelBase
{
    /// <summary>LiveResults caches for 15 seconds, so polling faster only wastes data.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _polling;
    private Competition? _competition;
    private Person? _me;
    private IReadOnlySet<PersonId> _groupIds = new HashSet<PersonId>();
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
        _me ??= await _people.GetMeAsync();

        var group = await _people.GetMyGroupAsync();
        _groupIds = group.Select(f => f.Person.Id).ToHashSet();

        await RefreshAsync();
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

        await RefreshAsync();
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
                    await MainThread.InvokeOnMainThreadAsync(RefreshAsync);
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

        var snapshot = await _live.GetSnapshotAsync(_competition.Id);
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

    private bool InScope(LiveEntry entry) => Scope switch
    {
        LiveScope.MyGroup => _groupIds.Contains(entry.Person) || entry.Person == _me!.Id,
        LiveScope.MyClass => entry.Class == _me!.DefaultClass,
        _ => true,
    };

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
            IsMe = entry.Person == _me!.Id,
            IsInMyGroup = _groupIds.Contains(entry.Person),
        };

        Apply(row, entry);
        return row;
    }

    private static void Apply(LiveRow row, LiveEntry entry)
    {
        row.IsFinished = entry.Status is LiveStatus.Finished or LiveStatus.Mispunch;
        row.IsRunning = entry.Status == LiveStatus.Running;
        row.HasNotStarted = entry.Status == LiveStatus.NotStarted;

        row.PositionText = entry.Status switch
        {
            LiveStatus.Finished when entry.FinalPlace is { } place => Format.Place(place),
            LiveStatus.Mispunch => "—",
            LiveStatus.NotStarted => "—",
            _ => entry.Position is { } position ? Format.Place(position) : "—",
        };

        row.ProgressText = entry.Status switch
        {
            LiveStatus.NotStarted => $"Start {Format.Clock(entry.StartTime)}",
            LiveStatus.Mispunch => "Felstämplat",
            LiveStatus.Finished => "I mål",
            _ => entry.LastControlNumber is { } control ? $"Kontroll {control}" : "Startat",
        };

        row.TimeText = entry.Status switch
        {
            LiveStatus.Finished => Format.Time(entry.FinishTime),
            LiveStatus.NotStarted => "—",
            _ => Format.Time(entry.ElapsedAtLastControl),
        };
    }
}
