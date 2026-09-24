namespace MauiSpineSampleApp.Pages.SvgIcons;

public partial class SvgIconsPageViewModel : ViewModelBase
{
    // A few from Plugin.Maui.Spine.Svg.Icons; the set has 218.
    public IReadOnlyList<string> Icons { get; } =
    [
        "fish.svg", "bell.svg", "settings.svg", "clock.svg", "house.svg", "user.svg", "key.svg", "lock.svg",
        "car.svg", "bus.svg", "location.svg", "pin.svg", "play.svg", "pause.svg", "refresh.svg", "edit.svg",
        "search.svg", "filter.svg", "share.svg", "copy.svg", "link.svg", "image.svg", "heart.svg", "star.svg",
        "info.svg", "warning.svg", "help.svg", "timer.svg", "cloud.svg", "globe.svg", "battery50.svg", "qrcode.svg",
    ];
}
