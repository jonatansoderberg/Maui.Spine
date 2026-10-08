using SpineMotion = Plugin.Maui.Spine.Extensions.Motion;

namespace MauiSpineSampleApp.Pages.Motion;

public partial class MotionPageViewModel : SampleViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardDepth))]
    public partial double Depth { get; set; } = 16;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardDepth))]
    public partial bool UseMotion { get; set; } = true;

    public double CardDepth => UseMotion ? Depth : 0;

    public bool IsSupported => SpineMotion.IsSupported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasNotice => Notice is not null;

    [ObservableProperty]
    public partial string Availability { get; set; } = "";

    // Read on every appearance: the user can turn Reduce Motion on while the app runs.
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Notice = !SpineMotion.IsSupported
            ? "This device cannot follow the tilt, so nothing on this page moves. Motion.IsSupported is false."
            : !SpineMotion.IsEnabled
                ? DeviceInfo.Platform == DevicePlatform.Android
                    ? "Remove animations is on, so nothing on this page moves until it is turned off. Motion.IsEnabled is false."
                    : "Reduce Motion is on, so nothing on this page moves until it is turned off. Motion.IsEnabled is false."
                : null;
        Availability = $"Motion.IsSupported: {SpineMotion.IsSupported}\nMotion.IsEnabled: {SpineMotion.IsEnabled}";
        return base.OnAppearingAsync(navigationDirection);
    }
}
