using CommunityToolkit.Mvvm.Messaging;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Services;
using Plugin.Maui.Spine.Sheets;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The root host page for a Spine application. It is a singleton <see cref="ContentPage"/> that
/// contains the <see cref="RootNavigationRegion"/> and manages bottom-sheet presentation.
/// Created and managed by <c>UseSpine</c> — you do not need to instantiate or register it manually.
/// </summary>
public partial class SpineHostPage : ContentPage, IDisposable
{
    /// <summary>Application title forwarded to the Windows title bar subtitle.</summary>
    public string? AppTitle { get; internal set; }

    /// <summary>Backdrop material applied to the bottom sheet surface on Windows.</summary>
    internal WindowBackdrop BottomSheetBackdrop { get; set; }

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

    bool _isBottomSheetActive = false;

    /// <summary>
    /// The <see cref="NavigationRegionViewModel"/> that is currently receiving navigation commands.
    /// Returns the sheet region's ViewModel while a bottom sheet is active, otherwise the root region's.
    /// </summary>
    internal NavigationRegionViewModel ActiveRegionViewModel =>
        (NavigationRegionViewModel)(_isBottomSheetActive ? SheetNavigationRegion.BindingContext : RootNavigationRegion.BindingContext);

    /// <summary>
    /// Initializes the host page, wires up the navigation regions, and registers the
    /// <see cref="ShowBottomSheetMessage"/> messenger handler.
    /// </summary>
    internal SpineHostPage(
        NavigationRegistry registry,
        NavigationRegion rootFrameView,
        [FromKeyedServices("BottomSheet")] NavigationRegion bottomSheetFrameView)
    {
        // Edge-to-edge: Spine manages safe-area padding per-page on the NavigationRegion
        // content hosts, so the host page must not apply any system-bar padding itself.
        this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None;

        SheetNavigationRegion = bottomSheetFrameView;

        this.Content = RootNavigationRegion = rootFrameView;

        WeakReferenceMessenger.Default.Register(
            this,
            (MessageHandler<object, ShowBottomSheetMessage>)(async (recipient, message) =>
            {
                _isBottomSheetActive = true;

                if (bottomSheetFrameView.BindingContext is not NavigationRegionViewModel vm)
                    return;

                if (message.Content is null)
                    return;

                await vm.ResetAsync(message.Content);

#if WINDOWS || ANDROID
                var bottomSheetTask = this.DisplayBottomSheet(
                    () => bottomSheetFrameView,
                    (b) =>
                    {
                        foreach (var detent in message.AllowedDetents)
                            b.AddDetent(detent);
                        b.SetSelectedDetent(message.SelectedDetent);
                        b.SetBackgroundPageOverlay(message.BackgroundPageOverlay);
                        b.SetSheetBackdrop(BottomSheetBackdrop);
                    });

                message.Reply(bottomSheetTask);

                await bottomSheetTask;
                _isBottomSheetActive = false;
#else
                await Task.CompletedTask;
#endif

            }));

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
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
