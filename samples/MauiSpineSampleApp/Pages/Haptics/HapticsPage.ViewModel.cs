using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Haptics;

public partial class HapticsPageViewModel : SampleViewModel
{
    private readonly SpineOptions _options = IPlatformApplication.Current!.Services.GetRequiredService<SpineOptions>();
    private int _follows;

    [ObservableProperty]
    public partial string Played { get; set; } = "Tap a row to feel it.";

    [ObservableProperty]
    public partial string FollowText { get; set; } = "Follow";

    [ObservableProperty]
    public partial string Saved { get; set; } = "Tap the checkmark at the top right.";

    public HapticsPageViewModel()
    {
        // The header bar has one trailing slot; Save takes it from the base class's theme menu.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
    }

    public bool IsUnsupported => !Plugin.Maui.Spine.Extensions.Haptics.IsSupported;

    public bool UseVibrator
    {
        get => _options.Android.HapticEngine == AndroidHapticEngine.Vibrator;
        set
        {
            _options.Android.HapticEngine = value ? AndroidHapticEngine.Vibrator : AndroidHapticEngine.View;
            OnPropertyChanged();
        }
    }

    // A success buzz on the header action, with no code in the command.
    [PageAction("Save", Role = PageActionRole.Confirm, Haptic = Haptic.Success)]
    [RelayCommand]
    private void Save() => Saved = $"Saved at {DateTime.Now:HH:mm:ss}";

    [RelayCommand]
    private void Play(Haptic haptic)
    {
        Plugin.Maui.Spine.Extensions.Haptics.Play(haptic);
        Played = $"Played {haptic}";
    }

    [RelayCommand]
    private void Follow() => FollowText = ++_follows % 2 == 1 ? "Following" : "Follow";
}
