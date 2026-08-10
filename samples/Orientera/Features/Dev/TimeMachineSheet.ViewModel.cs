using System.Collections.ObjectModel;
using Orientera.Domain;
using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Dev;

/// <summary>One stop on the competition journey, with the instant that produces it.</summary>
public sealed partial class LifecycleStop : ObservableObject
{
    public required string Label { get; init; }
    public required DateTimeOffset Instant { get; init; }
    public required ContextState ExpectedState { get; init; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public string TimeLabel => Instant.ToString("d MMM HH:mm");
}

/// <summary>
/// The dev tool behind the M0 DoD requirement that context state can be simulated across a
/// competition's whole lifecycle: move "now" and watch the tracked competition walk through
/// all eleven states and their CTAs.
/// </summary>
public partial class TimeMachineSheetViewModel(
    TimeMachineClock _clock,
    IEventSource _events,
    CompetitionContextService _context) : ViewModelBase
{
    private Competition? _tracked;

    public ObservableCollection<LifecycleStop> Stops { get; } = [];

    [ObservableProperty]
    public partial string NowLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrackedName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StateLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsShifted { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _tracked = await _events.GetCompetitionAsync(FakeDataset.NmLongId);

        if (_tracked is null)
            return;

        TrackedName = _tracked.Name;

        if (Stops.Count == 0)
            foreach (var stop in BuildStops(_tracked))
                Stops.Add(stop);

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task MoveTo(LifecycleStop stop)
    {
        _clock.MoveTo(stop.Instant);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Advance(string hours)
    {
        _clock.Advance(TimeSpan.FromHours(double.Parse(hours)));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Reset()
    {
        _clock.Reset();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        NowLabel = _clock.Now.ToString("dddd d MMMM yyyy, HH:mm");
        IsShifted = _clock.IsShifted;

        if (_tracked is null)
            return;

        var decision = await _context.EvaluateAsync(_tracked);
        StateLabel = decision.StateText;
        ActionLabel = decision.PrimaryActionText;

        foreach (var stop in Stops)
            stop.IsCurrent = stop.ExpectedState == decision.State;
    }

    /// <summary>
    /// One stop per context state, positioned just after the timestamp that unlocks it.
    /// Reading this list top to bottom is the whole journey: Upptäck → … → Analys.
    /// </summary>
    private static IEnumerable<LifecycleStop> BuildStops(Competition competition)
    {
        var schedule = competition.Schedule;
        var minute = TimeSpan.FromMinutes(1);

        yield return new LifecycleStop
        {
            Label = "Upptäck",
            Instant = schedule.RegistrationOpensAt!.Value - TimeSpan.FromDays(1),
            ExpectedState = ContextState.Discovered,
        };
        yield return new LifecycleStop
        {
            Label = "Anmälan öppen",
            Instant = schedule.RegistrationOpensAt!.Value + minute,
            ExpectedState = ContextState.RegistrationOpen,
        };
        yield return new LifecycleStop
        {
            Label = "Anmäld",
            Instant = FakeDataset.Instance.Entries
                .First(e => e.Competition == competition.Id && e.Person == FakeDataset.MeId)
                .RegisteredAt + minute,
            ExpectedState = ContextState.Registered,
        };
        yield return new LifecycleStop
        {
            Label = "PM publicerat",
            Instant = schedule.PmPublishedAt!.Value + minute,
            ExpectedState = ContextState.PmPublished,
        };
        yield return new LifecycleStop
        {
            Label = "Startlista publicerad",
            Instant = schedule.StartListPublishedAt!.Value + minute,
            ExpectedState = ContextState.StartListPublished,
        };
        yield return new LifecycleStop
        {
            Label = "Tävlingsdag",
            Instant = competition.FirstStart - TimeSpan.FromHours(2),
            ExpectedState = ContextState.RaceDay,
        };
        yield return new LifecycleStop
        {
            Label = "Live",
            Instant = FakeDataset.DefaultNow,
            ExpectedState = ContextState.Live,
        };
        yield return new LifecycleStop
        {
            Label = "I mål, preliminärt",
            Instant = competition.LastFinish + minute,
            ExpectedState = ContextState.Finished,
        };
        yield return new LifecycleStop
        {
            Label = "Resultat publicerat",
            Instant = schedule.ResultsPublishedAt!.Value + minute,
            ExpectedState = ContextState.ResultsPublished,
        };
        yield return new LifecycleStop
        {
            Label = "Sträcktider",
            Instant = schedule.SplitsPublishedAt!.Value + minute,
            ExpectedState = ContextState.SplitsAvailable,
        };
        yield return new LifecycleStop
        {
            Label = "Karta och analys",
            Instant = schedule.MapPublishedAt!.Value + minute,
            ExpectedState = ContextState.MapAndAnalysisAvailable,
        };
    }
}
