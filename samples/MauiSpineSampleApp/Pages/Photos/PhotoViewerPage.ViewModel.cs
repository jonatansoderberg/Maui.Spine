namespace MauiSpineSampleApp.Pages.Photos;

public partial class PhotoViewerPageViewModel : ViewModelBase, IReceivesNavigationParameter<PhotoSet>
{
    [ObservableProperty]
    public partial IReadOnlyList<LightboxImage> Images { get; set; } = [];

    [ObservableProperty]
    public partial int Index { get; set; }

    public Task OnNavigationParameterAsync(PhotoSet param)
    {
        Images = param.Images;
        Index = param.Index;
        ShowTitle();
        return Task.CompletedTask;
    }

    partial void OnIndexChanged(int value) => ShowTitle();

    private void ShowTitle() => Title = $"{Index + 1} of {Images.Count}";

    // The page's own action: a lightbox shows it in its toolbar, next to Share and Save.
    [PageAction(Svg = "info.svg")]
    [RelayCommand]
    private Task ShowInfo() =>
        Application.Current?.Windows[0].Page?.DisplayAlertAsync(
            Images[Index].Caption ?? "Photo",
            $"Photo {Index + 1} of {Images.Count} in the Spine Showcase.",
            "OK") ?? Task.CompletedTask;
}
