namespace MauiSpineSampleApp.Pages.Guards;

public partial class GuardsPageViewModel(INavigationService _navigation) : SampleViewModel
{
    [ObservableProperty]
    public partial string Log { get; set; } = "Nothing deleted yet";

    private int _deleted;

    [RelayCommand]
    private Task OpenVault() => _navigation.NavigateToAsync<VaultPage>();

    public MenuItems DraftMenu => [new MenuAction("Delete", "delete.svg", DeleteCommand) { IsDestructive = true }];

    [Confirm("Delete the draft?")]
    [RelayCommand]
    private void Delete() => Log = $"Deleted {++_deleted} time{(_deleted == 1 ? "" : "s")}";

    [RelayCommand]
    private Task ShowActions() => _navigation.ShowActionsAsync(new ActionSheet("Draft")
    {
        Actions = [new("Delete", "delete.svg", DeleteCommand) { IsDestructive = true }],
    });
}
