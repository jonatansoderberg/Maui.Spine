namespace MauiSpineSampleApp.Pages.Shimmer;

public sealed record PersonRow(string Name, string Role, string? Photo);

public partial class ShimmerPageViewModel(IThemeService _theme) : ViewModelBase
{
    // While loading, the list holds as many empty rows as a typical first page, so the skeleton
    // has the height the loaded list will have.
    private static readonly PersonRow[] Placeholders = [.. Enumerable.Repeat(new PersonRow("", "", null), 4)];

    private static readonly PersonRow[] Loaded =
    [
        new("Ada Lovelace", "Analyst", "dotnet_bot.png"),
        new("Grace Hopper", "Compiler author", "dotnet_bot.png"),
        new("Alan Turing", "Codebreaker", "dotnet_bot.png"),
        new("Katherine Johnson", "Trajectory analyst", "dotnet_bot.png"),
    ];

    private bool _loadedOnce;

    // Switches the app theme with the page on screen, to show the placeholders repaint.
    [ObservableProperty]
    public partial bool IsDark { get; set; } = _theme.Effective == AppTheme.Dark;

    partial void OnIsDarkChanged(bool value) => _theme.Current = value ? AppTheme.Dark : AppTheme.Light;

    [ObservableProperty]
    public partial bool IsWaveRunning { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    // Bound to Shimmer.WaveWidth and Shimmer.WaveOpacity; the defaults of ShimmerStyleOptions to start with.
    [ObservableProperty]
    public partial double WaveWidth { get; set; } = 0.22;

    [ObservableProperty]
    public partial double WaveOpacity { get; set; } = 0.3;

    [ObservableProperty]
    public partial IReadOnlyList<PersonRow> People { get; set; } = Placeholders;

    [ObservableProperty]
    public partial string? Photo { get; set; }

    [ObservableProperty]
    public partial string? PlaceName { get; set; }

    [ObservableProperty]
    public partial string? Bio { get; set; }

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (_loadedOnce)
            return;

        _loadedOnce = true;
        await Reload();
    }

    // What a page does with a real service: show the skeleton, await the data, show the content.
    [RelayCommand]
    private async Task Reload()
    {
        IsLoading = true;
        try
        {
            await Task.Delay(2500, PageLifetime);
            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnIsLoadingChanged(bool value)
    {
        People = value ? Placeholders : Loaded;
        Photo = value ? null : "mountain.png";
        PlaceName = value ? null : "Lake below the glacier";
        Bio = value ? null : "A short walk from the trailhead through old forest ends at a still lake with the snow-capped summit behind it. Best in the early morning, before the wind picks up and the reflection breaks.";
    }
}
