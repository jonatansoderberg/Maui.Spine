namespace MauiSpineSampleApp.Pages.GeneratedImages;

public partial class GeneratedImagesPageViewModel : SampleViewModel
{
    /// <summary>The cities the messaging sample's PostcardPrompt knows, and one it does not.</summary>
    private static readonly string[] All = ["visby", "kiruna", "stockholm", "gothenburg", "atlantis"];

    [ObservableProperty]
    public partial IReadOnlyList<string> Cities { get; set; } = All;

    /// <summary>A new list builds the cards again, so each one looks its image up again.</summary>
    [RelayCommand]
    private void LookUpAgain() => Cities = [.. All];
}
