using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.Photos;

public partial class PhotosPageViewModel : SampleViewModel
{
    private readonly INavigationService _navigation;
    private readonly ChoiceGroup _kind;

    public PhotosPageViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _kind = new("Opening",
        [
            new("From the thumbnail", "The lightbox grows out of the thumbnail you tap and shrinks into the thumbnail of the photo showing when it closes. Each photo carries its thumbnail's tag.", () => Kind = PhotoOpening.Zoom),
            new("Fade", "The photos carry no tags, so the lightbox fades in on its black and out of it.", () => Kind = PhotoOpening.Fade),
        ]);
        _kind.Select(0);
    }

    public IReadOnlyList<Photo> Photos => Photo.All;

    public ChoiceGroup KindGroup => _kind;

    [ObservableProperty]
    public partial PhotoOpening Kind { get; set; }

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("Lightbox", _kind);

    [RelayCommand]
    private Task OpenSheet() => _navigation.NavigateToAsync<PhotoSheetPage>();

    [RelayCommand]
    private Task Open(Photo photo) =>
        _navigation.NavigateToAsync<PhotoViewerPage, PhotoSet>(new(Photo.Images(Kind is PhotoOpening.Zoom), Photos.ToList().IndexOf(photo)));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(Kind))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Code)));
    }

    /// <summary>The code for the opening chosen.</summary>
    public string Code => Kind switch
    {
        PhotoOpening.Zoom => """
            <!-- The grid -->
            <Image Source="{Binding File}"
                   Transition.Tag="{Binding Key}" ... />

            // Opening a photo: each image carries
            // its thumbnail's tag
            await navigation.NavigateToAsync<
                PhotoViewerPage, PhotoSet>(new(
                Photos.Select(p => new LightboxImage(
                    p.File, p.Key, p.Caption)).ToList(),
                index));

            // The viewer
            [NavigableLightbox]
            public partial class PhotoViewerPage { ... }

            <SpinePage ...>
              <Lightbox ItemsSource="{Binding Images}"
                        Position="{Binding Index}" />
            </SpinePage>
            """,
        _ => """
            // Images without tags fade in and out
            new LightboxImage(p.File, Caption: p.Caption)

            [NavigableLightbox]
            public partial class PhotoViewerPage { ... }

            <SpinePage ...>
              <Lightbox ItemsSource="{Binding Images}"
                        Position="{Binding Index}" />
            </SpinePage>
            """,
    };
}

public sealed record Photo(int Id, string File, string Caption)
{
    /// <summary>The Showcase's photos, in the order the lightbox pages through them.</summary>
    public static IReadOnlyList<Photo> All { get; } =
    [
        new(1, "lightbox_skerries.jpg", "Sunset over the skerries"),
        new(2, "lightbox_misty_lake.jpg", "Misty lake at sunrise"),
        new(3, "lightbox_moon_lake.jpg", "Moon over the lake"),
        new(4, "lightbox_aurora.jpg", "Northern lights over the frozen lake"),
        new(5, "lightbox_autumn_rapids.jpg", "Autumn rapids"),
        new(6, "lightbox_mountain_valley.jpg", "Mountain valley"),
        new(7, "lightbox_birch_lake.jpg", "Birches by the lake"),
        new(8, "lightbox_archipelago_dawn.jpg", "Archipelago at dawn"),
    ];

    /// <summary>The photos as a lightbox shows them, each with its thumbnail's tag.</summary>
    public static IReadOnlyList<LightboxImage> Images(bool tagged = true) =>
        [.. All.Select(p => new LightboxImage(p.File, tagged ? p.Key : null, p.Caption))];

    /// <summary>The thumbnail's transition tag, which the lightbox's image carries too.</summary>
    public string Key => $"photo-{Id}";
}

public enum PhotoOpening { Zoom, Fade }

/// <summary>The images a viewer pages through, and the one it opens on.</summary>
public sealed record PhotoSet(IReadOnlyList<LightboxImage> Images, int Index);
