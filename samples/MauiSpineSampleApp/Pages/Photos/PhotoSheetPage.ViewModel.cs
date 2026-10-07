namespace MauiSpineSampleApp.Pages.Photos;

public partial class PhotoSheetPageViewModel(INavigationService navigation) : ViewModelBase
{
    public IReadOnlyList<Photo> Photos => Photo.All;

    // The same lightbox page as from the grid: from a sheet, Spine shows it over the whole screen.
    [RelayCommand]
    private Task Open(Photo photo) =>
        navigation.NavigateToAsync<PhotoViewerPage, PhotoSet>(new(Photo.Images(), Photo.All.ToList().IndexOf(photo)));
}
