using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The root host page for a Spine application without tabs. It is a singleton
/// <see cref="ContentPage"/> that contains the <see cref="RootNavigationRegion"/> and manages
/// bottom-sheet presentation. Created and managed by <c>UseSpine</c> — you do not need to
/// instantiate or register it manually. When <see cref="NavigableTabAttribute"/> pages are
/// discovered, <see cref="SpineTabbedHostPage"/> is used as the window root instead.
/// </summary>
public partial class SpineHostPage : ContentPage, ISpineHost, IDisposable
{
    /// <summary>Application title forwarded to the Windows title bar subtitle.</summary>
    public string? AppTitle { get; internal set; }

    /// <summary>Backdrop material applied to the bottom sheet surface on Windows.</summary>
    internal WindowBackdrop BottomSheetBackdrop
    {
        get => _sheets.SheetBackdrop;
        set => _sheets.SheetBackdrop = value;
    }

    /// <summary>
    /// The primary navigation region that hosts stack navigation pages.
    /// This region is always visible and covers the full screen.
    /// </summary>
    public NavigationRegion RootNavigationRegion { get; }

    /// <summary>
    /// The navigation region used to host pages inside bottom sheets.
    /// Active only while a sheet is open.
    /// </summary>
    public NavigationRegion SheetNavigationRegion { get; }

    private readonly BottomSheetCoordinator _sheets;

    /// <summary>
    /// The <see cref="NavigationRegionViewModel"/> that is currently receiving navigation commands.
    /// Returns the sheet region's ViewModel while a bottom sheet is active, otherwise the root region's.
    /// </summary>
    internal NavigationRegionViewModel ActiveRegionViewModel =>
        (NavigationRegionViewModel)(_sheets.IsSheetActive ? SheetNavigationRegion.BindingContext : RootNavigationRegion.BindingContext);

    Page ISpineHost.HostPage => this;
    NavigationRegionViewModel ISpineHost.ActiveRegionViewModel => ActiveRegionViewModel;
    event Action? ISpineHost.ActiveRegionChanged { add { } remove { } }
    bool ISpineHost.CanHandleRootBack => false;
    bool ISpineHost.TryHandleRootBack() => false;

    /// <summary>
    /// Initializes the host page, wires up the navigation regions, and registers the
    /// bottom-sheet coordinator.
    /// </summary>
    internal SpineHostPage(
        NavigationRegistry registry,
        NavigationRegion rootFrameView,
        [FromKeyedServices("BottomSheet")] NavigationRegion bottomSheetFrameView,
        SpineHostProvider hostProvider)
    {
        // Spine manages safe-area padding per-page on the NavigationRegion content hosts.
        // The host page must not apply any inset itself — NavigationRegion.SafeAreaEdges = None
        // (set in NavigationRegion's constructor) also disables MAUI's automatic ISafeAreaView2
        // geometry, and ISystemInsetsProvider reports the real insets Spine applies explicitly.
        this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None;

        SheetNavigationRegion = bottomSheetFrameView;

        this.Content = RootNavigationRegion = rootFrameView;

        _sheets = new BottomSheetCoordinator(this, bottomSheetFrameView, hostProvider);
    }

    /// <inheritdoc/>
    protected override bool OnBackButtonPressed()
    {
        return base.OnBackButtonPressed();
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        if (RootNavigationRegion.BindingContext is NavigationRegionViewModel vmFrameView)
            vmFrameView.InvokeOnAppearing();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _sheets.Dispose();
    }
}
