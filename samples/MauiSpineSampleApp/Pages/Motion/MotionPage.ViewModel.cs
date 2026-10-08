namespace MauiSpineSampleApp.Pages.Motion;

public partial class MotionPageViewModel : SampleViewModel
{
    [ObservableProperty]
    public partial double Depth { get; set; } = 16;
}
