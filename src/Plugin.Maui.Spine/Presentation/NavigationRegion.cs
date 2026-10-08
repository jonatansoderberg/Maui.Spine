using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.Input;
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
    private readonly LightboxToolbar _lightboxToolbar;
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
    /// Whether the region is laid over the whole window by Spine itself (a lightbox's overlay), not
    /// placed in a page that the platform offsets by the system bars.
    /// </summary>
    internal bool FillsWindow
    {
        get;
        init
        {
            field = value;
            UpdateContainerMargin();
        }
    }

    /// <summary>
    /// The page under this region in another one (a sheet's page under a lightbox's overlay): the
    /// page its root stands for, where a lightbox's thumbnail is.
    /// </summary>
    internal Element? PageUnder { get; init; }

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

        _contentHostFront = new BackSwipeHost();
        _container.Children.Add(_contentHostFront);

        // The region can move or resize under a keyboard that stays put, as in a rotation.
        _contentHostFront.SizeChanged += (_, _) =>
        {
            ApplyCompactWidth(ViewModel.CurrentRegionViewModel);

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

        _lightboxToolbar = new LightboxToolbar();
        _container.Children.Add(_lightboxToolbar);

        _contentHostFront.PropertyChanged += (_, e) =>
        {
            if (_cutsBack && e.PropertyName == nameof(TranslationX))
                CutBackAt(_contentHostFront.TranslationX);
        };

#if ANDROID
        // The page's own scroll view takes every touch first on Android, so the layer watches them on
        // their way down instead (BackSwipeHost) and a pan recognizer here would never begin.
        var host = (BackSwipeHost)_contentHostFront;
        host.Accepts = x =>
        {
            var width = GetEffectiveWidth();
            return width > 0 && x < width * DragEdgeThreshold && ViewModel.BackEnabled() && !FrontIsLightbox;
        };
        host.Swiped = (status, x, y) =>
        {
            _dragAccepted = true;
            OnBackSwipe(status, x, y);
        };
#else
        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        _contentHostFront.GestureRecognizers.Add(panGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        _contentHostFront.GestureRecognizers.Add(pointerGesture);
#endif

        RestrictBackSwipeOnPlatform();

        viewModel.PlayTransition = PlayTransitionAsync;

        UpdateContainerMargin();

        Content = _container;
    }

    /// <summary>
    /// Computes the container margin from measured system bar insets.
    /// </summary>
    /// <remarks>
    /// On iOS the platform offsets the page content by the system bars whatever the page asked for,
    /// so the margin counteracts that offset and lets <see cref="ApplySafeAreaPadding"/> put the
    /// insets back per page. On the other platforms the margin is zero.
    /// </remarks>
    private void UpdateContainerMargin()
    {
        var insets = _insetsProvider.SystemBarInsets;

#if ANDROID
        // Nothing offsets an overlay's window on Android, so there is nothing to counteract; the bars
        // are its padding. On iOS the view is offset by the window's safe area like any page.
        if (FillsWindow)
        {
            _container.Margin = Thickness.Zero;
            _frameActionView.Margin = new Thickness(insets.Left, insets.Top, insets.Right, 0);
            return;
        }
#endif

#if IOS
        // ContentPage's safe area cannot be disabled on iOS — MAUI always offsets the page
        // content by UIView.safeAreaInsets regardless of SafeAreaEdges = None on the host page.
        // Counteract that offset with a negative margin so _container fills the full window,
        // allowing Spine to apply insets explicitly via ApplySafeAreaPadding.
        _container.Margin = new Thickness(-insets.Left, -insets.Top, -insets.Right, -insets.Bottom);
#endif

#if ANDROID
        // Nothing offsets the container on Android: SystemInsetsProvider consumes the bars and the
        // display cutout above it, so MAUI's layouts get no insets to pad for. A margin that
        // counteracted an offset held only where the cutout happened to be as tall as the status
        // bar, and on a tablet it pulled the header bar under the status bar.
        _container.Margin = Thickness.Zero;

        // A sheet is a dialog window of its own, not under the status bar.
        if (ViewModel.Presentation is NavigationPresentation.Sheet)
            insets.Top = 0;
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

            if (FrontIsLightbox)
                UpdateLightboxToolbar();
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

        // Under a floating header the content must keep the status bar, the bar itself and a search row below it clear.
        var top = overlay
            ? insets.Top + (vm.IsHeaderBarVisible ? vm.HeaderBarHeight + vm.SearchRowHeight : 0)
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
            ApplyCompactWidth(ViewModel.CurrentRegionViewModel);

#if ANDROID
            SearchChanged?.Invoke();
#endif

            // Apply safe-area padding for the new page on both content hosts.
            if (ViewModel.CurrentRegionViewModel is { } vm)
            {
                // A page comes back as its search left it: a page left in the middle of one shows
                // the field in the header bar's place, as UIKit keeps a search controller active.
                this.AbortAnimation(SearchAnimation);
                vm.SearchProgress = vm.SearchHidesHeaderBar ? 1 : 0;
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

    // A narrow region keeps the header bar's room for the title; see SearchField.TrailingMinRegionWidth.
    private void ApplyCompactWidth(ViewModelBase? page)
    {
        if (page is not null && _contentHostFront.Width > 0)
            page.IsCompactWidth = _contentHostFront.Width < SearchField.TrailingMinRegionWidth;
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
            or nameof(ViewModelBase.EffectiveHeaderBarBackground) or nameof(ViewModelBase.SearchLayout)
            or nameof(ViewModelBase.SearchProgress))
            ApplyHeaderPage();
    }

    private void ApplyHeaderPage()
    {
        var page = _watchedHeaderPage;
        _frameActionView.OverContent = page?.HeaderBarMode == HeaderBarMode.Overlay;
        _frameActionView.Search = page is { SearchLayout: SearchLayout.Trailing } ? page.Search : null;
        _frameActionView.SearchProgress = page?.SearchProgress ?? 0;

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
        else if (e.PropertyName is nameof(ViewModelBase.SearchHidesHeaderBar))
        {
            AnimateSearch(page);
#if ANDROID
            SearchChanged?.Invoke();
#endif
        }
    }

#if ANDROID
    /// <summary>Raised when a shown page's search starts or stops hiding the header bar: back ends such a search first.</summary>
    internal static event Action? SearchChanged;
#endif

    private const string SearchAnimation = "SpineSearchHeader";

    /// <summary>How long the header bar takes to give way to a search, or to come back, in milliseconds.</summary>
    private const uint SearchDuration = 300;

    /// <summary>
    /// Has the header bar give way to a search that starts in the row below it, and brings it back
    /// when the search ends, as a navigation bar does under a <c>UISearchController</c>: the bar's
    /// buttons and the title slide up and fade, and the field and the content under it move up
    /// into the bar's place. Under Reduce Motion the change cross-fades on Apple platforms and is
    /// immediate elsewhere.
    /// </summary>
    private void AnimateSearch(ViewModelBase page)
    {
        var target = page.SearchHidesHeaderBar ? 1.0 : 0.0;
        this.AbortAnimation(SearchAnimation);

        if (Math.Abs(page.SearchProgress - target) < 0.001)
            return;

        void Apply(double progress)
        {
            page.SearchProgress = progress;
            page.SafeAreaInsets = SafeAreaInsetsFor(page, page.SystemBarInsets);
        }

        ClipForSearch(true);

#if IOS || MACCATALYST
        AnimateSearchOnPlatform(() => Apply(target), () => ClipForSearch(false));
#else
        if (ReducedMotion.IsOn)
        {
            Apply(target);
            ClipForSearch(false);
            return;
        }

        new Animation(Apply, page.SearchProgress, target)
            .Commit(this, SearchAnimation, length: SearchDuration, easing: Easing.CubicInOut, finished: (_, _) => ClipForSearch(false));
#endif
    }

    private int _searchClips;

    /// <summary>
    /// Clips the header bar and the page to their own bounds while the bar slides away or back, so
    /// its buttons and the title go under the edge they slide past (a sheet's top, the status bar)
    /// rather than over it. Counted, as animations may overlap.
    /// </summary>
    private void ClipForSearch(bool clip)
    {
        _searchClips = Math.Max(0, _searchClips + (clip ? 1 : -1));
        var clips = _searchClips > 0;
        _frameActionView.IsClippedToBounds = clips;
        ViewModel.FrontView.IsClippedToBounds = clips;
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
            // on iOS, and the page then kept the old theme's colour after a theme change. A
            // lightbox carries nothing but its image: its black is the dim under it.
            _contentHostFront.BackgroundColor = lift && !FrontIsLightbox ? ViewModel.FrontView.PageBackground() : Colors.Transparent;
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
        _transitioning = true;

        // A lightbox arriving brings its toolbar, which fades in with the rest of its chrome, and
        // takes the tab bar away; one leaving gives it back as it goes.
        if (push && FrontIsLightbox)
        {
            UpdateLightboxToolbar();
            _lightboxToolbar.Opacity = 0;
            CoverTabBar(true);
        }
        else if (!push && FrontIsLightbox)
        {
            GiveBackTabBarEarly();
        }

        // Under an overlay, the page the root stands for is the other region's.
        var leaving = push ? PageUnder ?? transition.OutgoingPage : transition.OutgoingPage;
        var arriving = push ? transition.IncomingPage : PageUnder ?? transition.IncomingPage;
        var flight = await SharedElementFlight.FindAsync(_container, _contentHostFront, leaving, arriving, push);

        try
        {
            if (flight is { IsZoom: true })
                await ZoomAsync(flight, push);
            else if (IsLightbox(push ? transition.IncomingPage : transition.OutgoingPage))
                await FadeLightboxAsync(push);
            else
                await Task.WhenAll(
                    push ? _transitions.AnimatePushAsync(transition) : _transitions.AnimatePopAsync(transition),
                    flight?.FlyAsync(_transitions.InteractiveGestureDuration, _transitions.InteractiveGestureEasing) ?? Task.CompletedTask);
        }
        finally
        {
            commit();
            UpdateLightboxToolbar();
            CoverTabBar(FrontIsLightbox);
            SetChrome(1);

            // A lightbox closed with its chrome hidden took the status bar with it.
            if (IsLightbox(transition.OutgoingPage))
                StatusBar.SetHidden(false);

            foreach (var layer in (View[])[_contentHostFront, _contentHostBack])
            {
                layer.TranslationX = 0;
                layer.TranslationY = 0;
                layer.Opacity = 1;
            }

            _backDragDimOverlay.Opacity = RestingDim;
            LiftFront(false);

            // The views land where their pictures are, now that the layers are back at rest.
            flight?.Dispose();
            _transitioning = false;
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
        var flight = await SharedElementFlight.FindAsync(_container, _contentHostFront, ViewModel.FrontView, PageUnder ?? ViewModel.BackView, push: false);
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
    /// <remarks>
    /// A lightbox's black is the dim, at full strength, so that it fades in and out around the
    /// image rather than growing out of the thumbnail with it.
    /// </remarks>
    private Task ZoomAsync(SharedElementFlight flight, bool push)
    {
        var dim = FrontIsLightbox ? 1 : BackDimOpacity;

        if (!push)
            _backDragDimOverlay.Opacity = dim;

        _frameActionView.Opacity = 0;

        return Task.WhenAll(
            flight.ZoomAsync(push, ZoomDuration),
            _backDragDimOverlay.SpineFadeToAsync(push ? dim : 0, ZoomDuration, Easing.CubicOut),
            _frameActionView.SpineFadeToAsync(1, ZoomDuration, Easing.CubicOut),
            FrontIsLightbox ? FadeLightboxChromeAsync(push, push ? ZoomDuration : LightboxFadeDuration / 2) : Task.CompletedTask);
    }

    /// <summary>
    /// Fades a lightbox's own chrome in as it arrives, or out as it leaves: its title and its
    /// toolbar. They lie over the image and are no part of the picture that zooms.
    /// </summary>
    private Task FadeLightboxChromeAsync(bool push, uint length)
    {
        var title = ViewModel.FrontView.TitleBar;
        if (push)
        {
            title.Opacity = 0;
            _lightboxToolbar.Opacity = 0;
        }

        return Task.WhenAll(
            title.SpineFadeToAsync(push ? 1 : 0, length, Easing.CubicOut),
            _lightboxToolbar.SpineFadeToAsync(push ? 1 : 0, length, Easing.CubicOut));
    }

    /// <summary>How long a lightbox takes to fade in or out when it has no thumbnail to zoom from, in milliseconds.</summary>
    private const uint LightboxFadeDuration = 250;

    /// <summary>
    /// A lightbox with no thumbnail on screen to grow out of (or shrink into) fades in on its black,
    /// or out of it: a page sliding in would carry a black panel across the page under it.
    /// </summary>
    private Task FadeLightboxAsync(bool push)
    {
        _contentHostFront.Opacity = push ? 0 : 1;
        _backDragDimOverlay.Opacity = push ? 0 : 1;

        return Task.WhenAll(
            _contentHostFront.SpineFadeToAsync(push ? 1 : 0, LightboxFadeDuration, Easing.CubicOut),
            _backDragDimOverlay.SpineFadeToAsync(push ? 1 : 0, LightboxFadeDuration, Easing.CubicOut),
            _lightboxToolbar.SpineFadeToAsync(push ? 1 : 0, LightboxFadeDuration, Easing.CubicOut));
    }

    private static bool IsLightbox(Element? page) => (page as PagePresenter)?.Content?.BindingContext is ViewModelBase { Lightbox: not null };

    /// <summary>
    /// Raised with <see langword="true"/> when a lightbox comes to the front, and
    /// <see langword="false"/> as it starts to leave: a tab host hides its tab bar meanwhile.
    /// </summary>
    internal event Action<bool>? CoversTabBarChanged;

    private bool _coversTabBar;

    private void CoverTabBar(bool covers)
    {
        if (covers == _coversTabBar)
            return;

        _coversTabBar = covers;
        CoversTabBarChanged?.Invoke(covers);
    }

    /// <summary>
    /// Brings the tab bar back as a lightbox starts to leave, where that moves nothing but the bar:
    /// on iOS it slides in by its transform. On Android the bar takes its own room, and the region
    /// would shrink under the zoom, so it comes back once the pages have swapped.
    /// </summary>
    private void GiveBackTabBarEarly()
    {
#if IOS || MACCATALYST
        CoverTabBar(false);
#endif
    }

    /// <summary>Whether the front page is a lightbox, which shows no edge back-swipe and sits on black.</summary>
    private bool FrontIsLightbox => IsLightbox(ViewModel.FrontView);

    /// <summary>The dim at rest: none, or under a lightbox, black.</summary>
    private double RestingDim => FrontIsLightbox ? 1 : 0;

    /// <summary>The lightbox on the front page, if it is a lightbox page.</summary>
    private Lightbox? FrontLightbox =>
        FrontIsLightbox && ViewModel.FrontView.Content is IVisualTreeElement page
            ? page.GetVisualTreeDescendants().OfType<Lightbox>().FirstOrDefault()
            : null;

    /// <summary>How much of a lightbox's chrome shows at rest: none once a tap has hidden it.</summary>
    private double ChromeOpacity => FrontLightbox is { IsChromeVisible: false } ? 0 : 1;

    /// <summary>
    /// Fades a lightbox's chrome: the header bar, the page's title and the caption. The bar takes
    /// no touches while it is hidden.
    /// </summary>
    private void SetChrome(double opacity)
    {
        _frameActionView.Opacity = opacity;
        _frameActionView.IsChromeHidden = opacity <= 0;
        _lightboxToolbar.Opacity = opacity;
        _lightboxToolbar.InputTransparent = opacity <= 0;
        ViewModel.FrontView.TitleBar.Opacity = opacity;
        FrontLightbox?.FadeChrome?.Invoke(opacity);
    }

    // The lightbox page whose actions the toolbar shows, watched for actions it adds or removes.
    private ViewModelBase? _toolbarPage;
    private static bool _warnedSave;

    /// <summary>
    /// Fills the toolbar for the lightbox in front, or hides it: Share and Save as its attribute
    /// asks, then the page's own <see cref="PageActionPlacement.Secondary"/> actions, which a
    /// lightbox shows here rather than in the header bar.
    /// </summary>
    private void UpdateLightboxToolbar()
    {
        var page = FrontIsLightbox ? ViewModel.CurrentRegionViewModel : null;
        if (!ReferenceEquals(page, _toolbarPage))
        {
            if (_toolbarPage is not null)
                _toolbarPage.PageActionsChanged -= UpdateLightboxToolbar;

            _toolbarPage = page;

            if (page is not null)
                page.PageActionsChanged += UpdateLightboxToolbar;
        }

        var actions = new List<PageAction>();
        var lightbox = FrontLightbox;

        if (page?.Lightbox is { } meta && lightbox is not null)
        {
            if (meta.Share && Lightbox.CanShare)
            {
                PageAction? share = null;
                share = new PageAction(null, new AsyncRelayCommand(() => lightbox.ShareAsync(_lightboxToolbar.ViewOf(share!))))
                {
                    Svg = "share.svg",
                    Description = Common.SpineStrings.Current["Spine.Lightbox.Share"],
                };
                actions.Add(share);
            }

            if (meta.Save && Lightbox.CanSaveToPhotos)
            {
                PageAction? save = null;
                save = new PageAction(null, new AsyncRelayCommand(async () =>
                {
                    if (!await lightbox.SaveAsync())
                        return;

                    // A tick in place of the button for a moment says it went into the library.
                    save!.Svg = "check.svg";
                    save.Description = Common.SpineStrings.Current["Spine.Lightbox.Saved"];
                    await Task.Delay(1500);
                    save.Svg = "download.svg";
                    save.Description = Common.SpineStrings.Current["Spine.Lightbox.Save"];
                }))
                {
                    Svg = "download.svg",
                    Description = Common.SpineStrings.Current["Spine.Lightbox.Save"],
                };
                actions.Add(save);
            }
            else if (meta.Save && !_warnedSave && (DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst))
            {
                _warnedSave = true;
                Console.WriteLine("[Spine] Lightbox: Save needs <key>NSPhotoLibraryAddUsageDescription</key> in Info.plist; the button is left out.");
            }

            actions.AddRange(page.PageActions.Where(action => action is { IsVisible: true, Placement: PageActionPlacement.Secondary }));
        }

        // A lightbox covers the tab bar, so it keeps clear of the window's edge rather than the bar.
        var bottom = (_insetsProvider as TabInsetsProvider)?.WindowInsets.Bottom ?? _insetsProvider.SystemBarInsets.Bottom;

        // With nothing at the foot of the window (a Mac, a sheet), the buttons stand off its edge
        // as they do off the sides.
        if (bottom <= 0)
            bottom = _frameActionView.SideMargin;

        // The same side margins as the header bar's buttons above it.
        var side = _frameActionView.SideMargin;
        var insets = _insetsProvider.SystemBarInsets;
        _lightboxToolbar.Show(actions, _frameActionView);
        _lightboxToolbar.Margin = new Thickness(insets.Left + side, 0, insets.Right + side, bottom);

        if (lightbox is not null)
            lightbox.BottomInset = bottom + (_lightboxToolbar.IsVisible ? HeaderBarConstants.Height : 0);
    }

    /// <summary>How long a lightbox's chrome takes to fade in or out on a tap, in milliseconds.</summary>
    private const uint ChromeFadeDuration = 200;

    /// <summary>Shows or hides a lightbox's chrome, and the status bar with it, after a tap on the image.</summary>
    internal void ShowChrome(bool visible)
    {
        if (!FrontIsLightbox || _dismissing)
            return;

        StatusBar.SetHidden(!visible);
        this.AbortAnimation(ChromeAnimation);
        new Animation(SetChrome, _frameActionView.Opacity, visible ? 1 : 0)
            .Commit(this, ChromeAnimation, length: ChromeFadeDuration, easing: Easing.CubicOut);
    }

    private const string ChromeAnimation = "SpineLightboxChrome";

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
            _dragAccepted = pos.Value.X < edgeLimit && !FrontIsLightbox;
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
        _backDragDimOverlay.Opacity = RestingDim;
        _contentHostBack.TranslationX = 0;

        LiftFront(false);

        _lastBackTx = double.NaN;
        _lastOpacity = double.NaN;
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e) => OnBackSwipe(e.StatusType, e.TotalX, e.TotalY);

    /// <summary>The back-swipe as it goes: how far it has moved sideways and down since it began.</summary>
    private async void OnBackSwipe(GestureStatus status, double totalX, double totalY)
    {
        if (!_dragAccepted)
            return;

        var vm = ViewModel;
        if (!vm.BackEnabled())
            return;

        switch (status)
        {
            case GestureStatus.Started:
                _dragStartX = totalX;
                _isDragging = false;

                _gestureWidth = GetEffectiveWidth();

                // Ensure any previous interactive state doesn't leak into non-interactive back animations
                ResetInteractiveState();

                if (totalX < 0)
                    _dragAccepted = false;
                break;

            case GestureStatus.Running:
            {
                var deltaX = totalX - _dragStartX;
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
                    zoom.Follow(deltaX, totalY, progress);
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

                    if (_gestureWidth > 0 && _zoomDragX > _gestureWidth * DragCompleteThreshold && status == GestureStatus.Completed)
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

                if (_gestureWidth > 0 && currentX > _gestureWidth * DragCompleteThreshold && status == GestureStatus.Completed)
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

    // A lightbox's drag down to close while it lasts, and the zoom it drives: none when the
    // thumbnail of the image showing is not on screen.
    private bool _dismissing;
    private SharedElementFlight? _dismissZoom;

    /// <summary>How far down a lightbox has to be dragged to close when let go slowly.</summary>
    private const double DismissDistance = 100;

    /// <summary>How fast a lightbox let go of moving down closes however far it went, in units a second.</summary>
    private const double DismissVelocity = 800;

    /// <summary>
    /// The region's height for a drag down: its own, or, for an overlay whose root the platform
    /// lays out rather than MAUI (an Android dialog), the screen's.
    /// </summary>
    private double DragHeight =>
        Height > 0 ? Height
        : _container.Height > 0 ? _container.Height
        : DeviceDisplay.Current.MainDisplayInfo is { Density: > 0 } display ? display.Height / display.Density : 0;

    /// <summary>Whether the front page can be dragged down to close: something is under it, and no other gesture is moving it.</summary>
    internal bool CanDismissByDrag => ViewModel.BackEnabled() && !_isDragging && !_dismissing && !_transitioning;

    // A push or a pop is moving the layers; a drag would take hold of them half-way.
    private bool _transitioning;

    /// <summary>
    /// A lightbox's drag down to close, as it goes: where the finger came down on the page, how far
    /// it has moved since, and on release how fast it was moving down, in units a second. The
    /// image stays under the finger and shrinks as it is drawn down, as in Photos, and the black
    /// fades; let go, it flies into the thumbnail of the image showing.
    /// </summary>
    internal async void OnDismissDrag(GestureStatus status, Point start, double x, double y, double velocityY)
    {
        var vm = ViewModel;

        switch (status)
        {
            case GestureStatus.Started:
                if (!CanDismissByDrag)
                    return;

                _dismissing = true;
                vm.StartInteractiveBack();
                LiftFront(true);
                _dismissZoom = await FindZoomDragAsync();
                break;

            case GestureStatus.Running:
            {
                if (!_dismissing)
                    return;

                // The black is gone once the image is halfway down.
                var height = DragHeight;
                var progress = height > 0 ? Math.Clamp(y / (height / 2), 0, 1) : 0;

                if (_dismissZoom is { } zoom)
                {
                    zoom.Carry(start, x, y, height > 0 ? Math.Clamp(y / height, 0, 1) : 0);
                }
                else
                {
                    _contentHostFront.TranslationX = x;
                    _contentHostFront.TranslationY = y;
                }

                _backDragDimOverlay.Opacity = 1 - progress;
                SetChrome(ChromeOpacity * (1 - Math.Min(1, progress * 4)));
                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                if (!_dismissing)
                    return;

                var zoom = _dismissZoom;
                _dismissZoom = null;

                if (status == GestureStatus.Completed && (y > DismissDistance || velocityY > DismissVelocity))
                {
                    GiveBackTabBarEarly();

                    // Without a thumbnail to land in, the image carries on down and fades.
                    await Task.WhenAll(
                        zoom?.ZoomAsync(push: false, ZoomDuration)
                            ?? Task.WhenAll(
                                _contentHostFront.SpineTranslateToAsync(x, Math.Max(y, 0) + DragHeight, LightboxFadeDuration, Easing.CubicIn),
                                _contentHostFront.SpineFadeToAsync(0, LightboxFadeDuration, Easing.CubicIn)),
                        _backDragDimOverlay.SpineFadeToAsync(0, ZoomDuration, Easing.CubicOut));

                    LiftFront(false);
                    await vm.CompleteInteractiveBackAsync();
                    CoverTabBar(FrontIsLightbox);
                    StatusBar.SetHidden(false);

                    // The page under it shows its own header, which comes in as the lightbox's went.
                    UpdateLightboxToolbar();
                    ViewModel.FrontView.TitleBar.Opacity = 1;
                    _frameActionView.IsChromeHidden = false;
                    _frameActionView.SpineFadeToAsync(1, LightboxFadeDuration, Easing.CubicOut).SafeFireAndForget();
                }
                else
                {
                    var chrome = ChromeOpacity;
                    var from = _frameActionView.Opacity;
                    var restore = new TaskCompletionSource();
                    new Animation(v => SetChrome(from + (chrome - from) * v), 0, 1)
                        .Commit(this, ChromeAnimation, length: ZoomDuration, easing: Easing.CubicOut, finished: (_, _) => restore.TrySetResult());

                    await Task.WhenAll(
                        zoom?.RestoreAsync(ZoomDuration) ?? _contentHostFront.SpineTranslateToAsync(0, 0, ZoomDuration, Easing.CubicOut),
                        _backDragDimOverlay.SpineFadeToAsync(1, ZoomDuration, Easing.CubicOut),
                        restore.Task);

                    vm.CancelInteractiveBack();
                }

                zoom?.Dispose();
                _contentHostFront.TranslationX = 0;
                _contentHostFront.TranslationY = 0;
                _contentHostFront.Opacity = 1;
                ResetInteractiveState();
                _dismissing = false;
                break;
            }
        }
    }
}
