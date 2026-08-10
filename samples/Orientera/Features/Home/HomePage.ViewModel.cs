using Orientera.Features.Live;

namespace Orientera.Features.Home;

public partial class HomePageViewModel(ITabBadgeService _tabBadges) : ViewModelBase
{
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        // M0 demo: "Live pågår"-dot on the Live tab until real live data drives it.
        _tabBadges.SetBadge<LivePage>("");
        return Task.CompletedTask;
    }
}
