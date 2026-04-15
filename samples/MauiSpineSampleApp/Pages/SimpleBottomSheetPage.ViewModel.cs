namespace MauiSpineSampleApp.Pages;

public partial class SimpleBottomSheetPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool PreventDismiss { get; set; } = false;

    public override Task<bool> OnCloseRequestedAsync() => Task.FromResult(!PreventDismiss);
}
