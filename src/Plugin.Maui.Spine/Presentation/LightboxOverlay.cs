using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// A lightbox opened from a sheet: a region of its own over the whole window, the sheet and all,
/// as a photo opened from a sheet is shown in Maps or Mail. Its stack is an empty page with the
/// lightbox on it, so the lightbox opens and closes as it does on any stack: out of its thumbnail,
/// which is on the sheet's page under it, and back into it. It goes once the lightbox has.
/// </summary>
internal sealed partial class LightboxOverlay
{
    /// <summary>The overlay showing, if one is.</summary>
    public static LightboxOverlay? Current { get; private set; }

    private readonly NavigationRegion _region;
    private readonly Element _pageUnder;

    private LightboxOverlay(NavigationRegion region, Element pageUnder)
    {
        _region = region;
        _pageUnder = pageUnder;
    }

    /// <summary>The overlay's stack, which navigation from the lightbox acts on while it shows.</summary>
    public NavigationRegionViewModel ViewModel => (NavigationRegionViewModel)_region.BindingContext;

    /// <summary>Shows <paramref name="lightbox"/> over everything, opening out of its thumbnail on <paramref name="pageUnder"/>.</summary>
    public static async Task ShowAsync(IServiceProvider services, IMauiContext context, View lightbox, Element pageUnder)
    {
        var viewModel = services.GetRequiredService<NavigationRegionViewModel>();
        var region = new NavigationRegion(
            viewModel,
            NavigationPresentation.RegionPresentation,
            services.GetRequiredService<ISpineTransitions>(),
            services.GetRequiredService<ISystemInsetsProvider>())
        {
            FillsWindow = true,
            PageUnder = pageUnder,
        };

        var overlay = new LightboxOverlay(region, pageUnder);
        viewModel.SetRootWithoutTransition(new ContentView());
        viewModel.WentBackToRoot += overlay.Close;

        Current = overlay;
        overlay.Attach(context);

        await viewModel.NavigateToAsync(lightbox);
    }

    private void Close()
    {
        ViewModel.WentBackToRoot -= Close;

        if (ReferenceEquals(Current, this))
            Current = null;

        Detach();

        // The status bar the sheet's page asked for, which the lightbox's light one replaced.
        StatusBar.Apply(((_pageUnder as PagePresenter)?.Content?.BindingContext as ViewModelBase)?.StatusBarStyle ?? StatusBarStyle.Auto);
    }

    /// <summary>Puts the region's native view over the window, the sheet included.</summary>
    partial void Attach(IMauiContext context);

    /// <summary>Takes the region's native view away.</summary>
    partial void Detach();
}
