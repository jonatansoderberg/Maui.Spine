using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using SpineSafeArea = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// A <see cref="ContentView"/> that hosts a navigation stack and renders the Spine header bar.
/// Supports interactive back-swipe gestures on mobile.
/// Created and managed by Spine's DI infrastructure � you do not need to instantiate this directly.
/// Reference it via <see cref="SpineHostPage.RootNavigationRegion"/> or
/// <see cref="SpineHostPage.SheetNavigationRegion"/> when you need to inspect the current state.
/// </summary>
public sealed partial class NavigationRegion : ContentView
{
    private readonly HeaderBarView _frameActionView;
    private readonly ContentView _contentHostFront;
    private readonly ContentView _contentHostBack;
    private readonly ContentView _backLayer;
    private readonly BoxView _backDragDimOverlay;
    private readonly ISpineTransitions _transitions;
    private readonly ISystemInsetsProvider _insetsProvider;
    private readonly Grid _container;

    private double _gestureWidth;

    private double _lastBackTx = double.NaN;
    private double _lastOpacity = double.NaN;

    private const double UpdateEpsilon = 0.5;

    private bool _dragAccepted = false;
    private double _dragStartX;
    private bool _isDragging;
    private const double DragEdgeThreshold = 0.25;
    private const double DragCompleteThreshold = 0.33;  // fraction of width to complete pop
    private const double BackParallax = 0.25;

    /// <summary>The opacity of the dim over a covered page.</summary>
    internal const double BackDimOpacity = 0.2;

    private NavigationRegionViewModel ViewModel => (NavigationRegionViewModel)BindingContext;

    /// <summary>
    /// Initializes the <see cref="NavigationRegion"/> with the provided ViewModel and presentation style.
    /// </summary>
    /// <param name="viewModel">The ViewModel that drives this region.</param>
    /// <param name="presentation">Whether this region hosts region pages or sheet pages.</param>
    /// <param name="transitions">The transition strategy used for clip-reveal gesture animations.</param>
    /// <param name="insetsProvider">Provides measured system bar insets for edge-to-edge layout.</param>
    internal NavigationRegion(NavigationRegionViewModel viewModel, NavigationPresentation presentation, ISpineTransitions transitions, ISystemInsetsProvider insetsProvider)
    {
        _transitions = transitions;
        _insetsProvider = insetsProvider;
        viewModel.Presentation = presentation;
        BindingContext = viewModel;

        // Disable MAUI's automatic ISafeAreaView2 geometry on this ContentView.
        // Spine owns the safe-area contract: ISystemInsetsProvider supplies the real
        // platform insets, UpdateContainerMargin positions the header, and
        // ApplySafeAreaPadding pads each content host per the page's SafeAreaEdges.
        this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _container = new Grid();
        
        _insetsProvider.InsetsChanged += OnSystemInsetsChanged;
        SoftKeyboard.Changed += () => OnKeyboardChanged(animate: true);

        _contentHostBack = new ContentView();

        // The back host moves with its page; the layer around it stays put, so a cut at the front
        // page's edge depends on the front page alone.
        _backLayer = new ContentView { Content = _contentHostBack };
        _container.Children.Add(_backLayer);

        _backDragDimOverlay = new BoxView
        {
            BackgroundColor = Colors.Black,
            Opacity = 0,
            Margin = new Thickness(0, -12 /* top handle height */, 0, 0),
            InputTransparent = true
        };
        _container.Children.Add(_backDragDimOverlay);

        _contentHostFront = new ContentView();
        _container.Children.Add(_contentHostFront);

        // The region can move or resize under a keyboard that stays put, as in a rotation.
        _contentHostFront.SizeChanged += (_, _) =>
        {
            if (SoftKeyboard.Top is not null || _keyboardOverlap > 0)
                OnKeyboardChanged(animate: false);
        };

        _contentHostBack.SetBinding(ContentView.ContentProperty, nameof(NavigationRegionViewModel.BackView));
        _contentHostFront.SetBinding(ContentView.ContentProperty, nameof(NavigationRegionViewModel.FrontView));

        _frameActionView = new HeaderBarView
        {
            CloseCommand = viewModel.CloseCommand,
            BackCommand = viewModel.BackCommand,
            Presentation = presentation,
            VerticalOptions = LayoutOptions.Start
        };

        _frameActionView.SetBinding(HeaderBarView.PrimaryPageActionProperty, new Binding(nameof(NavigationRegionViewModel.PrimaryPageAction), source: viewModel));
        _frameActionView.SetBinding(HeaderBarView.DefaultPageActionProperty, new Binding(nameof(NavigationRegionViewModel.SecondaryPageAction), source: viewModel));

        // The header bar is the only thing that knows how wide its actions ended up. The title
        // lives in the page content, one visual tree away, so the measurement is routed through
        // the region view model that both of them already share.
        _frameActionView.ActionSlotsChanged += () =>
        {
            viewModel.PrimaryActionSlot = _frameActionView.PrimaryActionSlot;
            viewModel.SecondaryActionSlot = _frameActionView.SecondaryActionSlot;
        };

        _container.Children.Add(_frameActionView);

        _contentHostFront.PropertyChanged += (_, e) =>
        {
            if (_cutsBack && e.PropertyName == nameof(TranslationX))
                CutBackAt(_contentHostFront.TranslationX);
        };

        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        _contentHostFront.GestureRecognizers.Add(panGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        _contentHostFront.GestureRecognizers.Add(pointerGesture);

        RestrictBackSwipeOnPlatform();

        viewModel.PlayTransition = PlayTransitionAsync;

        UpdateContainerMargin();

        Content = _container;
    }

    /// <summary>
    /// Computes the container margin from measured system bar insets.
    /// </summary>
    /// <remarks>
    /// On iOS and Android the platform offsets the page content by the system bars whatever the
    /// page asked for, so the margin counteracts that offset and lets
    /// <see cref="ApplySafeAreaPadding"/> put the insets back per page. On the other platforms the
    /// margin is zero.
    /// </remarks>
    private void UpdateContainerMargin()
    {
        var insets = _insetsProvider.SystemBarInsets;

#if IOS
        // ContentPage's safe area cannot be disabled on iOS — MAUI always offsets the page
        // content by UIView.safeAreaInsets regardless of SafeAreaEdges = None on the host page.
        // Counteract that offset with a negative margin so _container fills the full window,
        // allowing Spine to apply insets explicitly via ApplySafeAreaPadding.
        _container.Margin = new Thickness(-insets.Left, -insets.Top, -insets.Right, -insets.Bottom);
#endif

#if ANDROID
        // Same story as iOS at the top: the platform offsets the page content by the status bar
        // whatever the page asked for, so a page that excluded the top edge never got to draw
        // behind it. Counteract the offset and let ApplySafeAreaPadding put it back for the
        // pages that did ask for it.
        //
        // The top only. The bottom belongs to the Material bar: it applies the navigation inset
        // to itself and reports its own height as each tab's bottom inset (ApplyTabBarInset), so
        // pulling the container down there would drag content under a bar that is not painted to
        // be drawn under.
        //
        // Not in a sheet: it is a dialog window of its own, which nothing offsets by the status bar,
        // so pulling it up hid the top of a page that excluded Top above the sheet's edge, and the
        // scanner centred its aim in a frame a status bar taller than the part that showed.
        if (ViewModel.Presentation is NavigationPresentation.Sheet)
        {
            _container.Margin = Thickness.Zero;
            insets.Top = 0;
        }
        else
        {
            _container.Margin = new Thickness(0, -insets.Top, 0, 0);
        }
#endif

#if MACCATALYST
        // After fullSizeContentView, MAUI's safeAreaInsets offset already positions
        // NavigationRegion below the title bar for normal pages. Only full-bleed pages
        // (no header bar, no safe area) need the negative counteraction so their content
        // extends behind the title bar. SystemBarInsets stays zero on Mac Catalyst so
        // ApplySafeAreaPadding on other pages is unaffected.
        {
            var vm = ViewModel.CurrentRegionViewModel;
            var titleBarH = (_insetsProvider as SystemInsetsProvider)?.MacTitleBarHeight ?? 0;
            bool fullBleed = titleBarH > 0
                && vm is not null
                && !vm.IsHeaderBarVisible
                && vm.SafeAreaEdges == SpineSafeArea.None;
            bool overlay = titleBarH > 0 && vm is not null && ViewModel.Presentation is NavigationPresentation.Region && OverlaysTitleBar(vm);
            _container.Margin = fullBleed || overlay
                ? new Thickness(0, -titleBarH, 0, 0)
                : Thickness.Zero;

            // The page starts under the title bar; its header bar stays below it.
            if (overlay)
                insets.Top = titleBarH;

            // A full-bleed page draws its own top, so the window's title would sit on the page.
            if (ViewModel.Presentation is NavigationPresentation.Region
                && (Window?.Handler?.PlatformView as UIKit.UIWindow)?.WindowScene?.Titlebar is { } titlebar)
            {
                titlebar.TitleVisibility = fullBleed ? UIKit.UITitlebarTitleVisibility.Hidden : UIKit.UITitlebarTitleVisibility.Visible;
            }
        }
#endif

        var topMargin = insets.Top;
        if (ViewModel.Presentation is NavigationPresentation.Sheet)
            topMargin += HeaderBarConstants.SheetTopPadding;

        // The bar's buttons stay inside the safe area at the sides too, clear of the Dynamic Island
        // and the rounded corners in landscape, as UIKit's navigation bar keeps them.
        _frameActionView.Margin = new Thickness(insets.Left, topMargin, insets.Right, 0);
    }

    /// <summary>
    /// Reported by the Android sheet presenter: how far this region reaches below the visible edge
    /// of the sheet, in device-independent units. The iOS and Windows sheets resize the region
    /// instead, so there it stays zero.
    /// </summary>
    internal void SetSheetOverhang(double overhang)
    {
        ViewModel.FrontView.SetSheetOverhang(overhang);
        ViewModel.BackView.SetSheetOverhang(overhang);
    }

    /// <summary>
    /// The system bar insets as a page sees them. On Mac Catalyst a region page whose header bar floats
    /// over its content (an overlay, a large title, or any background but Solid) starts under the
    /// window's title bar, which is then its top bar, as the status bar is on a phone. The page's
    /// background then reaches the top of the window.
    /// </summary>
    internal static Thickness SystemBarInsetsFor(ViewModelBase vm, ISystemInsetsProvider provider, bool region)
    {
        var insets = provider.SystemBarInsets;
#if MACCATALYST
        if (region && OverlaysTitleBar(vm) && provider is SystemInsetsProvider mac)
            insets.Top = mac.MacTitleBarHeight;
#endif
        return insets;
    }

    private static bool OverlaysTitleBar(ViewModelBase vm) =>
        vm.IsHeaderBarVisible && vm.HeaderBarFloats;

    private void OnSystemInsetsChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateContainerMargin();

            // Re-apply safe-area padding for the current page now that insets are known.
            if (ViewModel.CurrentRegionViewModel is { } vm)
            {
                ApplySafeAreaPadding(_contentHostFront, vm);

                // Push the updated insets to the ViewModel so bindings that depend on
                // SystemBarInsets / SafeAreaInsets reflect the measured values.
                var insets = SystemBarInsetsFor(vm, _insetsProvider, ViewModel.Presentation is NavigationPresentation.Region);
                vm.SystemBarInsets = insets;
                vm.SafeAreaInsets = SafeAreaInsetsFor(vm, insets);
            }
        });
    }

    /// <summary>
    /// Sets padding on a content host based on the page's <see cref="SafeAreaEdges"/> and the
    /// measured system bar insets. Edges included in the page's <see cref="ViewModelBase.SafeAreaEdges"/> are padded;
    /// excluded edges allow content to extend behind the system bar.
    /// </summary>
    internal void ApplySafeAreaPadding(ContentView host, ViewModelBase vm)
    {
        var insets = _insetsProvider.SystemBarInsets;
        var safeAreaEdges = vm.SafeAreaEdges;

        // An overlay or collapsing header floats over content that starts at the top of the screen.
        if (vm.HeaderBarFloats)
            safeAreaEdges &= ~SpineSafeArea.Top;

#if ANDROID
        // The sheet's wrapper view already pads its bottom for the navigation bar (and drops that
        // padding while the keyboard is up), so padding the content as well left a second
        // navigation bar's worth of empty sheet under it — under a footer most visibly.
        // Nor is the top padded: the sheet is not under the status bar (see UpdateContainerMargin).
        if (ViewModel.Presentation is NavigationPresentation.Sheet)
            safeAreaEdges &= ~(SpineSafeArea.Bottom | SpineSafeArea.Top);
#endif

        // A sheet starts below its own drag handle, not at the card's edge: the same offset the
        // header bar gets has to reach the content too — otherwise the title (page content) and
        // the actions (header overlay) sit on different rows. Not a safe-area edge, so it applies
        // even when the page excludes Top.
        var sheetTop = ViewModel.Presentation is NavigationPresentation.Sheet
            ? HeaderBarConstants.SheetTopPadding
            : 0;

#if ANDROID
        // Except under a floating header on a page that excludes Top, such as a camera: it runs
        // to the card's edge under the drag handle, while the header bar stays below the handle.
        if (RunsToSheetEdge(vm))
            sheetTop = 0;
#endif

        var bottom = (safeAreaEdges & SpineSafeArea.Bottom) != 0 ? insets.Bottom : 0;

        // The keyboard ends the page where it begins, footer included, the way adjustResize and
        // UIKit's keyboard layout guide do.
        if (vm.KeyboardAvoidance)
            bottom = Math.Max(bottom, _keyboardOverlap);

        host.Padding = new Thickness(
            (safeAreaEdges & SpineSafeArea.Left)   != 0 ? insets.Left   : 0,
            ((safeAreaEdges & SpineSafeArea.Top)   != 0 ? insets.Top    : 0) + sheetTop,
            (safeAreaEdges & SpineSafeArea.Right)  != 0 ? insets.Right  : 0,
            bottom);
    }

    private double _keyboardOverlap;

    /// <summary>
    /// Follows the on-screen keyboard: how far it covers this region becomes the page's
    /// <see cref="ViewModelBase.KeyboardInset"/> and, unless the page opted out, its content host's
    /// bottom padding. Only the region that holds the focused field takes it, so a page under a
    /// sheet, or in another tab, stays where it is.
    /// </summary>
    private void OnKeyboardChanged(bool animate)
    {
        var overlap = 0.0;
        if (SoftKeyboard.Top is { } top && ContainsFocus())
            overlap = Math.Max(0, VisibleBottomOnScreen() - top);

        if (Math.Abs(overlap - _keyboardOverlap) < UpdateEpsilon)
            return;

        _keyboardOverlap = overlap;

        if (ViewModel.CurrentRegionViewModel is not { } vm)
            return;

        void Apply()
        {
            ApplyKeyboardInset(vm);
            ApplySafeAreaPadding(_contentHostFront, vm);
        }

        if (animate)
            AnimateWithKeyboard(Apply);
        else
            Apply();
    }

    private void ApplyKeyboardInset(ViewModelBase vm)
    {
        vm.KeyboardInset = _keyboardOverlap;
        vm.SafeAreaInsets = SafeAreaInsetsFor(vm, vm.SystemBarInsets);
    }

#if !IOS && !ANDROID
    private bool ContainsFocus() => false;
    private double VisibleBottomOnScreen() => 0;
#endif

#if !IOS
    private static void AnimateWithKeyboard(Action apply) => apply();
#endif

#if ANDROID
    private bool RunsToSheetEdge(ViewModelBase vm) =>
        ViewModel.Presentation is NavigationPresentation.Sheet
        && vm.HeaderBarFloats
        && (vm.SafeAreaEdges & SpineSafeArea.Top) == 0;

    /// <summary>
    /// Whether the Android sheet pads its bottom for the navigation bar under the current page:
    /// not for a page that excludes Bottom, which runs to the screen's edge.
    /// </summary>
    internal bool PadsSheetBottom =>
        ViewModel.CurrentRegionViewModel is not { } vm || (vm.SafeAreaEdges & SpineSafeArea.Bottom) != 0;

    /// <summary>Raised when the page whose edges the sheet follows changes.</summary>
    internal event Action? CurrentPageChanged;
#endif

    /// <summary>
    /// Computes the <see cref="Thickness"/> that a page should apply to its own content for the
    /// edges that Spine is <em>not</em> padding (i.e., edges excluded from the page's <see cref="ViewModelBase.SafeAreaEdges"/>).
    /// </summary>
    internal static Thickness SafeAreaInsetsFor(ViewModelBase vm, Thickness insets)
    {
        var edges = vm.SafeAreaEdges;
        var overlay = vm.HeaderBarFloats;

        // Under a floating header the content must keep the status bar and the bar itself clear.
        var top = overlay
            ? insets.Top + (vm.IsHeaderBarVisible ? HeaderBarConstants.BarHeight : 0)
            : (edges & SpineSafeArea.Top) != 0 ? 0 : insets.Top;

        // Above the keyboard the page no longer reaches the screen's bottom edge.
        var bottom = (edges & SpineSafeArea.Bottom) != 0 || (vm.KeyboardAvoidance && vm.KeyboardInset > 0) ? 0 : insets.Bottom;

        return new Thickness(
            (edges & SpineSafeArea.Left)   != 0 ? 0 : insets.Left,
            top,
            (edges & SpineSafeArea.Right)  != 0 ? 0 : insets.Right,
            bottom);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The header follows the page it shows, which while going back is already the page coming back.
        if (e.PropertyName == nameof(ViewModel.HeaderRegionViewModel))
        {
            var header = ViewModel.HeaderRegionViewModel;
            _frameActionView?.SetBinding(HeaderBarView.IsHeaderBarVisibleProperty, new Binding("IsHeaderBarVisible", source: header));
            _frameActionView?.SetBinding(HeaderBarView.IsBackButtonVisibleProperty, new Binding("IsBackButtonVisible", source: header));
            _frameActionView?.SetBinding(HeaderBarView.IsTitleBarVisibleProperty, new Binding("IsTitleBarVisible", source: header));
            _frameActionView?.SetBinding(HeaderBarView.ForegroundProperty, new Binding(nameof(ViewModelBase.HeaderBarForeground), source: header));
            _frameActionView?.SetBinding(HeaderBarView.GlassProperty, new Binding(nameof(ViewModelBase.HeaderBarGlass), source: header));
            WatchHeaderPage(header);
        }

        if (e.PropertyName == nameof(ViewModel.CurrentRegionViewModel))
        {
#if ANDROID
            CurrentPageChanged?.Invoke();
#endif
            // Mac Catalyst: container margin depends on the new page's SafeAreaEdges +
            // IsHeaderBarVisible, so recalculate whenever the page changes.
            UpdateContainerMargin();


            // A sheet keeps the status bar of the page under it.
            if (ViewModel.Presentation is NavigationPresentation.Region && ViewModel.CurrentRegionViewModel is { } shown)
                StatusBar.Apply(shown.StatusBarStyle);

            // Actions are now computed by the region view model (including back/close fallbacks)
            _frameActionView?.SetBinding(HeaderBarView.PrimaryPageActionProperty, new Binding(nameof(NavigationRegionViewModel.PrimaryPageAction), source: ViewModel));
            _frameActionView?.SetBinding(HeaderBarView.DefaultPageActionProperty, new Binding(nameof(NavigationRegionViewModel.SecondaryPageAction), source: ViewModel));

            WatchCurrentPage(ViewModel.CurrentRegionViewModel);

            // Apply safe-area padding for the new page on both content hosts.
            if (ViewModel.CurrentRegionViewModel is { } vm)
            {
                ApplyKeyboardInset(vm);
                ApplySafeAreaPadding(_contentHostFront, vm);
            }

            ApplySafeAreaPaddingForPresenter(_contentHostBack, ViewModel.BackView);
        }

        // Pre-apply safe-area padding when the front or back view is swapped so the
        // correct insets are in place before the transition animation starts.
        if (e.PropertyName == nameof(NavigationRegionViewModel.FrontView))
            ApplySafeAreaPaddingForPresenter(_contentHostFront, ViewModel.FrontView);

        if (e.PropertyName == nameof(NavigationRegionViewModel.BackView))
            ApplySafeAreaPaddingForPresenter(_contentHostBack, ViewModel.BackView);
    }

    private ViewModelBase? _watchedHeaderPage;

    // What the bar lies over decides whether its buttons need a container of their own: the page's
    // content under an Overlay header, until the bar's own background fades in over it. The progress is
    // internal to the view model, so it is followed here rather than bound.
    private void WatchHeaderPage(ViewModelBase? page)
    {
        if (!ReferenceEquals(page, _watchedHeaderPage))
        {
            if (_watchedHeaderPage is not null)
                _watchedHeaderPage.PropertyChanged -= OnHeaderPagePropertyChanged;

            _watchedHeaderPage = page;

            if (page is not null)
                page.PropertyChanged += OnHeaderPagePropertyChanged;
        }

        ApplyHeaderPage();
    }

    private void OnHeaderPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModelBase.HeaderBarMode) or nameof(ViewModelBase.ScrollEdgeProgress)
            or nameof(ViewModelBase.EffectiveHeaderBarBackground))
            ApplyHeaderPage();
    }

    private void ApplyHeaderPage()
    {
        var page = _watchedHeaderPage;
        _frameActionView.OverContent = page?.HeaderBarMode == HeaderBarMode.Overlay;

        // A transparent bar never draws a background, so its buttons keep their container.
        _frameActionView.BackgroundProgress = page is not null && page.EffectiveHeaderBarBackground != HeaderBarBackground.Transparent
            ? page.ScrollEdgeProgress
            : 0;
    }

    private ViewModelBase? _watchedPage;

    // A page that changes its header while shown may start or stop floating under the bar, which
    // moves its content to the top of the screen or back below the bar; and it may change the
    // status bar it wants.
    private void WatchCurrentPage(ViewModelBase? page)
    {
        if (ReferenceEquals(page, _watchedPage))
            return;

        if (_watchedPage is not null)
            _watchedPage.PropertyChanged -= OnCurrentPagePropertyChanged;

        _watchedPage = page;

        if (page is not null)
            page.PropertyChanged += OnCurrentPagePropertyChanged;
    }

    private void OnCurrentPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ViewModelBase page || !ReferenceEquals(page, ViewModel.CurrentRegionViewModel))
            return;

        if (e.PropertyName is nameof(ViewModelBase.HeaderBarMode) or nameof(ViewModelBase.EffectiveHeaderBarBackground)
            or nameof(ViewModelBase.LargeTitle))
        {
            ApplySafeAreaPadding(_contentHostFront, page);
#if MACCATALYST
            UpdateContainerMargin();
#endif
        }
        else if (e.PropertyName is nameof(ViewModelBase.KeyboardAvoidance))
        {
            ApplyKeyboardInset(page);
            ApplySafeAreaPadding(_contentHostFront, page);
        }
        else if (e.PropertyName is nameof(ViewModelBase.StatusBarStyle) && ViewModel.Presentation is NavigationPresentation.Region)
            StatusBar.Apply(page.StatusBarStyle);
    }

    private void ApplySafeAreaPaddingForPresenter(ContentView host, PagePresenter? presenter)
    {
        if (presenter?.Content?.BindingContext is ViewModelBase vm)
            ApplySafeAreaPadding(host, vm);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e) => _dragAccepted = false;

    /// <summary>
    /// Keeps the platform's pan recognizer from claiming touches that are not a back-swipe; see
    /// <c>NavigationRegion.Apple.cs</c>. A no-op on platforms whose pans leave child touches alone.
    /// </summary>
    partial void RestrictBackSwipeOnPlatform();

    /// <summary>
    /// Rounds the front layer to the screen's corners, as iOS 26 rounds a page while it moves. A
    /// no-op where the platform keeps pages square.
    /// </summary>
    partial void RoundFront(bool round);

    /// <summary>
    /// Shows the back layer only left of <paramref name="frontX"/>, the front page's leading edge,
    /// or the whole of it at <see langword="null"/>. On iOS it is called inside the animation that
    /// moves the front page, so the cut moves with it.
    /// </summary>
    partial void CutBackOnPlatform(double? frontX);

    // In a sheet the front page cannot carry the sheet's surface, so it stays see-through and the
    // page underneath is cut off where the front page begins, as UIKit cuts it.
    private bool _cutsBack;

    private void CutBackAt(double? frontX)
    {
#if IOS || MACCATALYST
        CutBackOnPlatform(frontX);
#else
        var width = _container.Width;
        var height = _container.Height;
        _backLayer.Clip = frontX is { } x && width > 0
            ? new Microsoft.Maui.Controls.Shapes.RectangleGeometry(new Rect(-width, -height, width + x, height * 3))
            : null;
#endif
    }

    /// <summary>
    /// Lifts the front layer off the one under it while it moves. Pages sit on a background painted
    /// behind the region, so the layer carries its page's colour to hide what is under it, and
    /// takes the screen's corners where the platform rounds pages. A sheet's pages sit on the
    /// sheet's surface instead, which a layer cannot carry: there the front page stays see-through
    /// and the page underneath is cut off at its edge, so only the sheet shows under it.
    /// </summary>
    private void LiftFront(bool lift)
    {
        if (ViewModel.Presentation is NavigationPresentation.Sheet)
        {
            _cutsBack = lift;
            CutBackAt(lift ? _contentHostFront.TranslationX : null);

            // A dim would only darken the sheet; iOS leaves the page underneath as it is.
            _backDragDimOverlay.IsVisible = !lift;
        }
        else
        {
            // Transparent rather than cleared: clearing the value leaves the native colour behind
            // on iOS, and the page then kept the old theme's colour after a theme change.
            _contentHostFront.BackgroundColor = lift ? ViewModel.FrontView.PageBackground() : Colors.Transparent;
        }

        RoundFront(lift);
    }

    /// <summary>
    /// Plays a push or a pop on the layers, then lets <paramref name="commit"/> swap the pages
    /// before the layers return to rest, so the page that moved away is never seen at rest.
    /// </summary>
    private async Task PlayTransitionAsync(NavigationDirection direction, Action commit)
    {
        var vm = ViewModel;
        var push = direction is NavigationDirection.NavigateTo;
        var transition = new SpineTransitionContext(
            _contentHostFront,
            _contentHostBack,
            _backDragDimOverlay,
            GetEffectiveWidth(),
            incomingPage: push ? vm.FrontView : vm.BackView,
            outgoingPage: push ? vm.BackView : vm.FrontView,
            flatten: () => LiftFront(false));

        LiftFront(true);

        var flight = await SharedElementFlight.FindAsync(_container, _contentHostFront, transition.OutgoingPage, transition.IncomingPage, push);

        try
        {
            if (flight is { IsZoom: true })
                await ZoomAsync(flight, push);
            else
                await Task.WhenAll(
                    push ? _transitions.AnimatePushAsync(transition) : _transitions.AnimatePopAsync(transition),
                    flight?.FlyAsync(_transitions.InteractiveGestureDuration, _transitions.InteractiveGestureEasing) ?? Task.CompletedTask);
        }
        finally
        {
            commit();
            _frameActionView.Opacity = 1;

            foreach (var layer in (View[])[_contentHostFront, _contentHostBack])
            {
                layer.TranslationX = 0;
                layer.TranslationY = 0;
                layer.Opacity = 1;
            }

            _backDragDimOverlay.Opacity = 0;
            LiftFront(false);

            // The views land where their pictures are, now that the layers are back at rest.
            flight?.Dispose();
        }
    }

    // The zoom a back-swipe drives while it lasts, and how far right the finger is.
    private SharedElementFlight? _zoomDrag;
    private double _zoomDragX;

    /// <summary>
    /// The zoom between the page being swiped away and its view on the page under it, when the
    /// page grew out of one that is on screen. A back-swipe moves no other shared element: the
    /// page slides away with it.
    /// </summary>
    private async Task<SharedElementFlight?> FindZoomDragAsync()
    {
        var flight = await SharedElementFlight.FindAsync(_container, _contentHostFront, ViewModel.FrontView, ViewModel.BackView, push: false);
        if (flight is { IsZoom: true })
            return flight;

        flight?.Dispose();
        return null;
    }

    /// <summary>How long a page takes to grow out of its view or shrink back into it, in milliseconds.</summary>
    private const uint ZoomDuration = 450;

    /// <summary>
    /// A zoom in place of the slide: the page grows out of its view (or shrinks back into it)
    /// while the page under it stays where it is and dims, and the header bar fades in with the
    /// page it now belongs to.
    /// </summary>
    private Task ZoomAsync(SharedElementFlight flight, bool push)
    {
        if (!push)
            _backDragDimOverlay.Opacity = BackDimOpacity;

        _frameActionView.Opacity = 0;

        return Task.WhenAll(
            flight.ZoomAsync(push, ZoomDuration),
            _backDragDimOverlay.SpineFadeToAsync(push ? BackDimOpacity : 0, ZoomDuration, Easing.CubicOut),
            _frameActionView.SpineFadeToAsync(1, ZoomDuration, Easing.CubicOut));
    }

    private double GetEffectiveWidth()
    {
        var width = Width;

        if (width <= 0 && Application.Current is { Windows.Count: > 0 } app)
            width = app.Windows[0]?.Page?.Width ?? 0;

        return width;
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var width = GetEffectiveWidth();

        if (width > 0 && pos.HasValue)
        {
            var edgeLimit = width * DragEdgeThreshold;
            _dragAccepted = pos.Value.X < edgeLimit;
        }
    }

    private void ApplyBackReveal(double deltaX)
    {
        var width = _gestureWidth;
        if (width <= 0)
            return;

        var dragProgress = Math.Clamp(deltaX, 0, width) / width;

        var backTx = -width * BackParallax * (1.0 - dragProgress);

        if (double.IsNaN(_lastBackTx) || Math.Abs(backTx - _lastBackTx) >= UpdateEpsilon)
        {
            _contentHostBack.TranslationX = backTx;
            _lastBackTx = backTx;
        }

        var opacity = BackDimOpacity * (1.0 - dragProgress);

        if (double.IsNaN(_lastOpacity) || Math.Abs(opacity - _lastOpacity) >= 0.01)
        {
            _backDragDimOverlay.Opacity = opacity;
            _lastOpacity = opacity;
        }
    }

    private void ResetInteractiveState()
    {
        _backDragDimOverlay.Opacity = 0;
        _contentHostBack.TranslationX = 0;

        LiftFront(false);

        _lastBackTx = double.NaN;
        _lastOpacity = double.NaN;
    }

    private async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!_dragAccepted)
            return;

        var vm = ViewModel;
        if (!vm.BackEnabled())
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragStartX = e.TotalX;
                _isDragging = false;

                _gestureWidth = GetEffectiveWidth();

                // Ensure any previous interactive state doesn't leak into non-interactive back animations
                ResetInteractiveState();

                if (e.TotalX < 0)
                    _dragAccepted = false;
                break;

            case GestureStatus.Running:
            {
                var deltaX = e.TotalX - _dragStartX;
                if (deltaX <= 0)
                    return;

                if (!_isDragging)
                {
                    _isDragging = true;
                    vm.StartInteractiveBack();
                    LiftFront(true);
                    _zoomDrag = await FindZoomDragAsync();
                }

                // A page that grew out of a view shrinks back towards it under the finger instead
                // of sliding away; the page under it stays where it is.
                if (_zoomDrag is { } zoom)
                {
                    _zoomDragX = deltaX;
                    var progress = _gestureWidth > 0 ? Math.Clamp(deltaX / _gestureWidth, 0, 1) : 0;
                    zoom.Follow(deltaX, e.TotalY, progress);
                    _backDragDimOverlay.Opacity = BackDimOpacity * (1 - progress);
                    break;
                }

                _contentHostFront.TranslationX = Math.Max(0, deltaX);
                ApplyBackReveal(deltaX);

                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                if (!_isDragging)
                    return;

                var currentX = _contentHostFront.TranslationX;
                var duration = _transitions.InteractiveGestureDuration;
                var easing = _transitions.InteractiveGestureEasing;

                if (_zoomDrag is { } zoom)
                {
                    _zoomDrag = null;

                    if (_gestureWidth > 0 && _zoomDragX > _gestureWidth * DragCompleteThreshold && e.StatusType == GestureStatus.Completed)
                    {
                        await Task.WhenAll(
                            zoom.ZoomAsync(push: false, ZoomDuration),
                            _backDragDimOverlay.SpineFadeToAsync(0, ZoomDuration, Easing.CubicOut));

                        // The view's picture covers the shrunk page until the pages swap and the zoom
                        // lets go of the layer.
                        LiftFront(false);
                        await vm.CompleteInteractiveBackAsync();
                    }
                    else
                    {
                        await Task.WhenAll(
                            zoom.RestoreAsync(ZoomDuration),
                            _backDragDimOverlay.SpineFadeToAsync(BackDimOpacity, ZoomDuration, Easing.CubicOut));

                        vm.CancelInteractiveBack();
                    }

                    zoom.Dispose();
                    ResetInteractiveState();
                    _isDragging = false;
                    break;
                }

                if (_gestureWidth > 0 && currentX > _gestureWidth * DragCompleteThreshold && e.StatusType == GestureStatus.Completed)
                {
                    await Task.WhenAll(
                        vm.CompleteInteractiveBackAnimationAsync(_contentHostFront, _contentHostBack, currentX),
                        _backDragDimOverlay.SpineFadeToAsync(0, duration, easing));

                    // The host is about to show the previous page in place, which stays square.
                    LiftFront(false);
                    _contentHostFront.TranslationX = 0;
                    await vm.CompleteInteractiveBackAsync();
                }
                else
                {
                    await Task.WhenAll(
                        vm.CancelInteractiveBackAnimationAsync(_contentHostFront, _contentHostBack),
                        _backDragDimOverlay.SpineFadeToAsync(BackDimOpacity, duration, easing));

                    vm.CancelInteractiveBack();
                }

                // Fully reset interactive artifacts so subsequent programmatic BackAsync is smooth
                ResetInteractiveState();

                _isDragging = false;
                break;
            }
        }
    }
}
