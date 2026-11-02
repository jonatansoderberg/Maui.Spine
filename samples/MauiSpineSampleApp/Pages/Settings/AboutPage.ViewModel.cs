namespace MauiSpineSampleApp.Pages.Settings;

public partial class AboutPageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    // A sheet waits for OnAppearingAsync, so the file is read after the sheet is up.
    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (Text.Length == 0)
            _ = LoadAsync();
        return base.OnAppearingAsync(navigationDirection);
    }

    private async Task LoadAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("about.md");
        using var reader = new StreamReader(stream);
        Text = await reader.ReadToEndAsync(PageLifetime);
    }
}
