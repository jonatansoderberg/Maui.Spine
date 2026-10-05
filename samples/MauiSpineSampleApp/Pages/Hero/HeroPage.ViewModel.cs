using System.Collections.ObjectModel;
using System.ComponentModel;
using MauiSpineSampleApp.Pages.HeaderBar;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Hero;

public partial class HeroPageViewModel : SampleViewModel
{
    static readonly string[] Palette = ["#29335C", "#669BBC", "#A8C686", "#F3A712", "#E4572E", "#7B2D26"];

    readonly ChoiceGroup _image, _overlay, _height, _reorder;

    public HeroPageViewModel()
    {
        _image = new("Image",
        [
            new("Center", "The photo stays centred in the part of the header that is still showing, so the compact header shows its middle. The default.", () => ImageCollapse = HeroImageCollapse.Center),
            new("Slide", "The photo moves up with the header, so the compact header shows its bottom edge.", () => ImageCollapse = HeroImageCollapse.Slide),
        ]);

        _overlay = new("Overlay",
        [
            new("Blur", "HeaderOverlayContent is a Border with a blur material. It fades in once the header has shrunk by about a third, and blurs the photo behind the compact header.", () => Blur = true),
            new("None", "No overlay: the photo stays sharp in the compact header.", () => Blur = false),
        ]);

        _height = new("Height",
        [
            new("Short", "HeaderMaxHeight 220.", () => HeaderMaxHeight = 220),
            new("Medium", "HeaderMaxHeight 320.", () => HeaderMaxHeight = 320),
            new("Tall", "HeaderMaxHeight 440.", () => HeaderMaxHeight = 440),
        ]);

        _reorder = new("Reorder",
        [
            new("Off", "Rows stay where they are.", () => ReorderMode = ReorderMode.Off),
            new("On", "Reorder.Mode=\"LongPress\": hold a row until it lifts and drag it to a new place. Spine moves the item in the ObservableCollection.", () => ReorderMode = ReorderMode.LongPress),
        ]);

        _image.Select(0);
        _overlay.Select(0);
        _height.Select(1);
        _reorder.Select(0);

        // The first row is the page itself: the example card with its options and code.
        Rows = [this, .. Enumerable.Range(1, 30).Select(i => new Swatch(
            $"Row {i}",
            "Scroll up and watch the photo as the header collapses.",
            Color.FromArgb(Palette[i % Palette.Length])))];
    }

    // A collection the list can move items in when a row is dragged.
    public ObservableCollection<object> Rows { get; }

    public IReadOnlyList<ChoiceGroup> Groups => [_image, _overlay, _height, _reorder];

    [ObservableProperty]
    public partial HeroImageCollapse ImageCollapse { get; set; }

    [ObservableProperty]
    public partial bool Blur { get; set; } = true;

    [ObservableProperty]
    public partial ReorderMode ReorderMode { get; set; }

    [ObservableProperty]
    public partial double HeaderMaxHeight { get; set; } = 320;

    // The compact header is as tall as the status bar and the header bar that lie over it.
    public double HeaderMinHeight => SafeAreaInsets.Top;

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("HeroCollectionView", [.. Groups]);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SafeAreaInsets))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderMinHeight)));

        if (e.PropertyName is nameof(ImageCollapse) or nameof(Blur) or nameof(HeaderMaxHeight) or nameof(ReorderMode))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Code)));
    }

    /// <summary>The XAML for the combination on screen.</summary>
    public string Code
    {
        get
        {
            var collapse = ImageCollapse == HeroImageCollapse.Slide ? "\n    HeaderImageCollapse=\"Slide\"" : "";
            var reorder = ReorderMode == ReorderMode.LongPress ? "\n    Reorder.Mode=\"LongPress\"" : "";
            var overlay = Blur
                ? "\n\n  <HeroCollectionView.HeaderOverlayContent>\n    <Border Material.Preset=\"BlurUltraThin\"\n            StrokeThickness=\"0\" />\n  </HeroCollectionView.HeaderOverlayContent>"
                : "";

            return $"<HeroCollectionView\n    ItemsSource=\"{{Binding Rows}}\"\n    HeaderImageSource=\"mountain.png\"\n    HeaderMaxHeight=\"{HeaderMaxHeight}\"\n    HeaderMinHeight=\"{{Binding SafeAreaInsets.Top}}\"{collapse}{reorder}>{overlay}\n\n  <HeroCollectionView.ItemTemplate>\n    ...\n  </HeroCollectionView.ItemTemplate>\n</HeroCollectionView>\n\n// The header bar lies over the photo:\n[NavigableRegion(Title = \"Trips\",\n  HeaderBar = HeaderBarMode.Overlay,\n  HeaderBarBackground =\n    HeaderBarBackground.Transparent,\n  HeaderBarForeground = \"#FFFFFF\",\n  HeaderBarGlass = HeaderBarGlass.Clear)]";
        }
    }
}

/// <summary>The example card for the page's own row, a swatch for the rest.</summary>
public sealed class HeroRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Intro { get; set; }

    public DataTemplate? Row { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        (item is HeroPageViewModel ? Intro : Row)!;
}
