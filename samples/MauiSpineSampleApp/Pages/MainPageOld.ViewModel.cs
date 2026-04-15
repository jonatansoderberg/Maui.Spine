namespace MauiSpineSampleApp.Pages;

public partial class MainPageOldViewModel(INavigationService _navigation) : ViewModelBase
{
    [ObservableProperty]
    public partial string? SheetResult { get; set; }

    [ObservableProperty]
    public partial string DynamicText { get; set; } = "Hello, World!";

    private static readonly string[] _latinWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet",
        "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor",
        "incididunt", "ut", "labore", "et",
        "dolore", "magna", "aliqua"
    ];

    private static readonly Random _random = new();

    [RelayCommand]
    private Task ChangeDynamicText()
    {
        int wordCount = _random.Next(3, 20);

        var words = Enumerable.Range(0, wordCount)
            .Select(_ => _latinWords[_random.Next(_latinWords.Length)])
            .ToList();

        if (words.Count > 0)
            words[0] = char.ToUpper(words[0][0]) + words[0][1..];

        DynamicText = string.Join(" ", words) + ".";

        return Task.CompletedTask;
    }

    [RelayCommand] private async Task OpenSettings() => await _navigation.NavigateToAsync<Settings.SettingsPage>();
    [RelayCommand] private async Task ShowBottomSheet() => await _navigation.NavigateToAsync<SamplePage>();
    [RelayCommand] private async Task ShowSimpleBottomSheet() => await _navigation.NavigateToAsync<SimpleBottomSheetPage>();
    [RelayCommand] private async Task ShowFullscreenSheet() => await _navigation.NavigateToAsync<FullscreenSheetPage>();
    [RelayCommand] private async Task ShowSmallSheet() => await _navigation.NavigateToAsync<SmallSheetPage>();
    [RelayCommand] private async Task ShowPersonDetail() =>
        await _navigation.NavigateToAsync<PersonDetailPage, PersonData>(new PersonData("Alice Smith", "alice@example.com", 30));

    [RelayCommand]
    private async Task ShowFullscreenSheetWithResult()
    {
        var result = await _navigation.NavigateToWithResultAsync<FullscreenSheetPage, FullscreenSheetResult>();

        SheetResult = result is { IsSuccess: true, Value: not null and var value }
            ? $"Result: {value.Message}"
            : "Canceled";
    }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (PageActions.Count == 0)
        {
            PageActions.Add(new PageAction(text: null, command: OpenSettingsCommand)
            {
                Svg = "settings.svg"
            });
        }

        return base.OnAppearingAsync(navigationDirection);
    }
}
