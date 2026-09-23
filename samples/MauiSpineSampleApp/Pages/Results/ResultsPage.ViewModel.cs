namespace MauiSpineSampleApp.Pages.Results;

public partial class ResultsPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string SheetResult { get; set; } = "No result yet";

    // A typed parameter: the page implements INavigableWithParameter<PersonData>.
    [RelayCommand]
    private async Task ShowPersonDetail() =>
        await _navigation.NavigateToAsync<PersonDetailPage, PersonData>(new PersonData("Alice Smith", "alice@example.com", 30));

    // A typed result: the sheet implements INavigableWithResult<FullscreenSheetResult> and calls ReturnAsync.
    [RelayCommand]
    private async Task ShowFullscreenSheetWithResult()
    {
        var result = await _navigation.NavigateToWithResultAsync<FullscreenSheetPage, FullscreenSheetResult>();

        SheetResult = result switch
        {
            { IsSuccess: true, Value: { } value } => $"Result: {value.Message}",
            { IsSuccess: true } => "Result: (null)",
            _ => "Canceled",
        };
    }
}
