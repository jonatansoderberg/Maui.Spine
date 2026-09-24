namespace MauiSpineSampleApp.Pages.Sheets;

public partial class EditSheetPageViewModel(INavigationService _navigation) : ViewModelBase
{
    public IReadOnlyList<EditSheetOption> Options { get; } =
    [
        new("New messages", true),
        new("Replies to you", true),
        new("Mentions", true),
        new("Reactions", false),
        new("New followers", false),
        new("Weekly summary", true),
        new("Product news", false),
        new("Security alerts", true),
        new("Reminders", true),
        new("Calendar invites", false),
    ];

    [PageAction("Cancel", Placement = PageActionPlacement.Primary)]
    [RelayCommand]
    private async Task Cancel() => await _navigation.CloseAsync();

    [PageAction("Save")]
    [RelayCommand]
    private async Task Save() => await _navigation.CloseAsync();
}

public partial class EditSheetOption(string name, bool isOn) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsOn { get; set; } = isOn;
}
