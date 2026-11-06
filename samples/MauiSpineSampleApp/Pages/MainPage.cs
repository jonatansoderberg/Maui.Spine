using System.ComponentModel;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages;

[NavigableRegion(Title = "Main page", IsHeaderBarVisible = false, SafeAreaEdges = SafeAreaEdges.None, ScrollInset = SafeAreaEdges.Bottom)]
public partial class MainPage
{
    // How open the header still is when the filter button comes close enough to settings for their glass
    // to merge: below it the button has a surface of its own, above it it is a bare icon on the photo.
    private const double MergeAt = 0.25;

    private bool _filterHasGlass;

    public MainPage()
    {
        InitializeComponent();

        var viewModel = (MainPageViewModel)BindingContext;

        // Where the header bar groups its actions on one piece of glass, the filter button's glass melts into
        // the settings button's as it arrives beside it, and pulls away again as the header grows.
        if (viewModel.GroupsButtons)
        {
            Hero.HeaderTopContent = null;
            Hero.HeaderTopContent = new MaterialContainer
            {
                Spacing = 16,
                InputTransparent = true,
                CascadeInputTransparent = false,
                Content = HeaderButtons,
            };
        }

        viewModel.FilterChanged += (_, _) => Hero.ScrollToTop();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainPageViewModel.FilterSlide))
                PlaceFilterButton();
        };
        Hero.PropertyChanged += OnLayoutChanged;
        TitleLabel.PropertyChanged += OnLayoutChanged;
        FilterGlass.PropertyChanged += OnLayoutChanged;
        PlaceFilterButton();
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HeroCollectionView.HeaderHeight) or nameof(HeroCollectionView.HeaderMinHeight)
            or nameof(HeroCollectionView.HeaderMaxHeight) or nameof(Y) or nameof(Height))
            PlaceFilterButton();
    }

    // The filter button is laid out beside the settings button, where it sits once the header has collapsed.
    // While the header is open it rides on the header's bottom edge, centred on the title's line at the
    // page's edge, below settings. It rises straight up with the header and slides in sideways only over
    // the last part of the collapse, when its glass appears and melts into the settings button's.
    private void PlaceFilterButton()
    {
        var zone = Hero.HeaderMaxHeight - Hero.HeaderMinHeight;
        var drop = Hero.HeaderHeight - Hero.HeaderMinHeight;
        var open = zone > 0 ? Math.Clamp(drop / zone, 0, 1) : 1;

        // The bottom content is as tall as the open header and moves up by the whole zone as it collapses;
        // the title's line, once collapsed, is not quite the settings button's.
        var titleLine = TitleLabel.Height > 0 ? TitleLabel.Y + TitleLabel.Height / 2 + TitleLabel.TranslationY - zone : 0;
        var buttonLine = FilterGlass.Y + FilterGlass.Height / 2;
        var toTitle = TitleLabel.Height > 0 && FilterGlass.Height > 0 ? titleLine - buttonLine : 0;

        FilterGlass.TranslationY = drop + toTitle * open;

        // Straight up below settings until it is about to merge, then into its place beside it.
        var viewModel = (MainPageViewModel)BindingContext;
        var t = Math.Min(open / MergeAt, 1);
        FilterGlass.TranslationX = viewModel.FilterSlide * t * t * (3 - 2 * t);

        var hasGlass = viewModel.GroupsButtons && open < MergeAt;
        if (hasGlass != _filterHasGlass)
        {
            _filterHasGlass = hasGlass;
            ShowFilterGlass(hasGlass);
        }
    }

    // The glass materialises as it arrives rather than popping in: UIKit animates an effect set inside an
    // animation.
    private void ShowFilterGlass(bool show)
    {
        void Apply() => Material.SetPreset(FilterGlass, show ? MaterialPreset.GlassClear : MaterialPreset.None);
#if IOS || MACCATALYST
        UIKit.UIView.Animate(0.25, Apply);
#else
        Apply();
#endif
    }
}
