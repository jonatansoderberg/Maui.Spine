using Orientera.Features.Dev;
using Orientera.Features.Live;
using Orientera.Services.Context;
using Orientera.Services.FakeData;
using Orientera.Services.Sources;
using Orientera.Services.Time;

namespace Orientera.Features.Home;

public partial class HomePageViewModel(
    INavigationService _navigation,
    ITabBadgeService _tabBadges,
    TimeMachineClock _clock,
    IEventSource _events,
    CompetitionContextService _context) : ViewModelBase
{
    [ObservableProperty]
    public partial string TrackedName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StateLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NowLabel { get; set; } = string.Empty;

    [RelayCommand]
    private async Task OpenTimeMachine() => await _navigation.NavigateToAsync<TimeMachineSheet>();

    [RelayCommand]
    private async Task OpenDesignSystem() => await _navigation.NavigateToAsync<DesignSystemPage>();

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // M0 demo: "Live pågår"-dot on the Live tab until real live data drives it.
        _tabBadges.SetBadge<LivePage>("");

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var competition = await _events.GetCompetitionAsync(FakeDataset.NmLongId);

        if (competition is null)
            return;

        var decision = await _context.EvaluateAsync(competition);

        TrackedName = competition.Name;
        StateLabel = decision.StateText;
        ActionLabel = decision.PrimaryActionText;
        NowLabel = _clock.Now.ToString("dddd d MMMM yyyy, HH:mm");
    }
}
