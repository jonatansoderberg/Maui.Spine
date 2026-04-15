using Plugin.Maui.Spine.Core;
using Microsoft.Maui.Controls.Shapes;
using SpineSafeArea = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// A <see cref="ContentView"/> that hosts a navigation stack and renders the Spine header bar.
/// Supports interactive back-swipe gestures on mobile.
/// Created and managed by Spine's DI infrastructure — you do not need to instantiate this directly.
/// Reference it via <see cref="SpineHostPage.RootNavigationRegion"/> or
/// <see cref="SpineHostPage.SheetNavigationRegion"/> when you need to inspect the current state.
/// </summary>
public sealed class NavigationRegion : ContentView
{
    private readonly HeaderBar _frameActionView;
    private readonly ContentView _contentHostFront;
    private readonly ContentView _contentHostBack;
    private readonly BoxView _backDragDimOverlay;
    private readonly ISpineTransitions _transitions;
    private readonly ISystemInsetsProvider _insetsProvider;
    private readonly Grid _container;

    private Geometry? _originalClip;
    private RectangleGeometry? _draggingClip;

    private double _gestureWidth;
    private double _gestureHeight;

    private double _lastBackTx = double.NaN;
    private double _lastOpacity = double.NaN;
    private double _lastClipWidth = double.NaN;

    private const double UpdateEpsilon = 0.5;

    private Color _originalFrontBackground = Colors.Transparent;
    private bool _dragAccepted = false;
    private double _dragStartX;
    private bool _isDragging;
    private const double DragEdgeThreshold = 0.25;
    private const double DragCompleteThreshold = 0.33;  // fraction of width to complete pop
    private const double BackDragDimStartOpacity = 0.2;

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

        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _container = new Grid();
        
        _insetsProvider.InsetsChanged += OnSystemInsetsChanged;

        _contentHostBack = new ContentView();
        _container.Children.Add(_contentHostBack);

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

        _contentHostBack.SetBinding(ContentView.ContentProperty, nameof(NavigationRegionViewModel.BackView));
        _contentHostFront.SetBinding(ContentView.ContentProperty, nameof(NavigationRegionViewModel.FrontView));

        _frameActionView = new HeaderBar
        {
            CloseCommand = viewModel.CloseCommand,
            BackCommand = viewModel.BackCommand,
            Presentation = presentation,
            VerticalOptions = LayoutOptions.Start
        };

        _frameActionView.SetBinding(HeaderBar.PrimaryPageActionProperty, new Binding(nameof(NavigationRegionViewModel.PrimaryPageAction), source: viewModel));
        _frameActionView.SetBinding(HeaderBar.DefaultPageActionProperty, new Binding(nameof(NavigationRegionViewModel.SecondaryPageAction), source: viewModel));

        _container.Children.Add(_frameActionView);

        _originalFrontBackground = _contentHostFront.BackgroundColor;

        var panGesture = new PanGestureRecognizer();
        panGesture.PanUpdated += OnPanUpdated;
        _contentHostFront.GestureRecognizers.Add(panGesture);

        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerReleased += OnPointerReleased;
        _contentHostFront.GestureRecognizers.Add(pointerGesture);

        UpdateContainerMargin();

        Content = _container;
    }

    /// <summary>
    /// Computes the container margin from measured system bar insets.
    /// On Android this offsets the container so it renders behind the status bar (edge-to-edge).
    /// On other platforms the margin is zero.
    /// </summary>
    private void UpdateContainerMargin()
    {
        var insets = _insetsProvider.SystemBarInsets;
        _container.Margin = new Thickness(0, -insets.Top, 0, 0);
        _frameActionView.Margin = new Thickness(0, insets.Top, 0, 0);
    }

    private void OnSystemInsetsChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateContainerMargin();

            // Re-apply safe-area padding for the current page now that insets are known.
            if (ViewModel.CurrentRegionViewModel is { } vm)
            {
                ApplySafeAreaPadding(_contentHostFront, vm.SafeAreaEdges);

                // Push the updated insets to the ViewModel so bindings that depend on
                // SystemBarInsets / SafeAreaInsets reflect the measured values.
                var insets = _insetsProvider.SystemBarInsets;
                vm.SystemBarInsets = insets;
                vm.SafeAreaInsets = GetSafeAreaInsets(vm.SafeAreaEdges);
            }
        });
    }

    /// <summary>
    /// Sets padding on a content host based on the page's <see cref="SafeAreaEdges"/> and the
    /// measured system bar insets. Edges included in <paramref name="safeAreaEdges"/> are padded;
    /// excluded edges allow content to extend behind the system bar.
    /// </summary>
    internal void ApplySafeAreaPadding(ContentView host, SpineSafeArea safeAreaEdges)
    {
        var insets = _insetsProvider.SystemBarInsets;
        host.Padding = new Thickness(
            (safeAreaEdges & SpineSafeArea.Left)   != 0 ? insets.Left   : 0,
            (safeAreaEdges & SpineSafeArea.Top)    != 0 ? insets.Top    : 0,
            (safeAreaEdges & SpineSafeArea.Right)  != 0 ? insets.Right  : 0,
            (safeAreaEdges & SpineSafeArea.Bottom) != 0 ? insets.Bottom : 0);
    }

    /// <summary>
    /// Computes the <see cref="Thickness"/> that a page should apply to its own content for the
    /// edges that Spine is <em>not</em> padding (i.e., edges excluded from <paramref name="safeAreaEdges"/>).
    /// </summary>
    internal Thickness GetSafeAreaInsets(SpineSafeArea safeAreaEdges)
    {
        var insets = _insetsProvider.SystemBarInsets;
        return new Thickness(
            (safeAreaEdges & SpineSafeArea.Left)   != 0 ? 0 : insets.Left,
            (safeAreaEdges & SpineSafeArea.Top)    != 0 ? 0 : insets.Top,
            (safeAreaEdges & SpineSafeArea.Right)  != 0 ? 0 : insets.Right,
            (safeAreaEdges & SpineSafeArea.Bottom) != 0 ? 0 : insets.Bottom);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.CurrentRegionViewModel))
        {
            _frameActionView?.SetBinding(HeaderBar.IsHeaderBarVisibleProperty, new Binding("IsHeaderBarVisible", source: ViewModel.CurrentRegionViewModel));
            _frameActionView?.SetBinding(HeaderBar.IsBackButtonVisibleProperty, new Binding("IsBackButtonVisible", source: ViewModel.CurrentRegionViewModel));
            _frameActionView?.SetBinding(HeaderBar.IsTitleBarVisibleProperty, new Binding("IsTitleBarVisible", source: ViewModel.CurrentRegionViewModel));

            // Actions are now computed by the region view model (including back/close fallbacks)
            _frameActionView?.SetBinding(HeaderBar.PrimaryPageActionProperty, new Binding(nameof(NavigationRegionViewModel.PrimaryPageAction), source: ViewModel));
            _frameActionView?.SetBinding(HeaderBar.DefaultPageActionProperty, new Binding(nameof(NavigationRegionViewModel.SecondaryPageAction), source: ViewModel));

            // Apply safe-area padding for the new page on both content hosts.
            if (ViewModel.CurrentRegionViewModel is { } vm)
                ApplySafeAreaPadding(_contentHostFront, vm.SafeAreaEdges);

            ApplySafeAreaPaddingForPresenter(_contentHostBack, ViewModel.BackView);
        }

        // Pre-apply safe-area padding when the front or back view is swapped so the
        // correct insets are in place before the transition animation starts.
        if (e.PropertyName == nameof(NavigationRegionViewModel.FrontView))
            ApplySafeAreaPaddingForPresenter(_contentHostFront, ViewModel.FrontView);

        if (e.PropertyName == nameof(NavigationRegionViewModel.BackView))
            ApplySafeAreaPaddingForPresenter(_contentHostBack, ViewModel.BackView);
    }

    private void ApplySafeAreaPaddingForPresenter(ContentView host, PagePresenter? presenter)
    {
        if (presenter?.Content?.BindingContext is ViewModelBase vm)
            ApplySafeAreaPadding(host, vm.SafeAreaEdges);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e) => _dragAccepted = false;

    private double GetEffectiveWidth()
    {
        var width = Width;

        if (width <= 0 && Application.Current is { Windows.Count: > 0 } app)
            width = app.Windows[0]?.Page?.Width ?? 0;

        return width;
    }

    private double GetEffectiveHeight()
    {
        var height = Height;

        if (height <= 0 && Application.Current is { Windows.Count: > 0 } app)
            height = app.Windows[0]?.Page?.Height ?? 0;

        if (height <= 0)
            height = _contentHostBack.Height;

        return height;
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
        if (width <= 0 || _draggingClip is null)
            return;

        var clamped = Math.Clamp(deltaX, 0, width);
        var dragProgress = clamped / width;

        var backTx = -width * 0.25 * (1.0 - dragProgress);
        var opacity = BackDragDimStartOpacity * (1.0 - dragProgress);

        if (double.IsNaN(_lastBackTx) || Math.Abs(backTx - _lastBackTx) >= UpdateEpsilon)
        {
            _contentHostBack.TranslationX = backTx;
            _backDragDimOverlay.TranslationX = backTx;
            _lastBackTx = backTx;
        }

        if (double.IsNaN(_lastOpacity) || Math.Abs(opacity - _lastOpacity) >= 0.01)
        {
            _backDragDimOverlay.Opacity = opacity;
            _lastOpacity = opacity;
        }

        var clipWidth = Math.Max(0, clamped - backTx);
        if (double.IsNaN(_lastClipWidth) || Math.Abs(clipWidth - _lastClipWidth) >= UpdateEpsilon)
        {
            _draggingClip.Rect = new Rect(0, 0, clipWidth, _gestureHeight);
            _lastClipWidth = clipWidth;
        }
    }

    private Task AnimateRevealClipAsync(double fromDeltaX, double toDeltaX, uint length, Easing easing)
    {
        if (_draggingClip is null || _gestureWidth <= 0)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource();

        this.AbortAnimation("InteractiveBackRevealClip");

        var animation = new Animation(progress =>
        {
            var deltaX = fromDeltaX + ((toDeltaX - fromDeltaX) * progress);
            ApplyBackReveal(deltaX);
        });

        animation.Commit(
            this,
            "InteractiveBackRevealClip",
            16,
            length,
            easing,
            (v, c) => tcs.TrySetResult());

        return tcs.Task;
    }

    private void ResetInteractiveState()
    {
        this.AbortAnimation("InteractiveBackRevealClip");

        _backDragDimOverlay.Opacity = 0;
        _backDragDimOverlay.TranslationX = 0;

        if (_originalClip is not null)
        {
            _contentHostBack.Clip = _originalClip;
            _backDragDimOverlay.Clip = _originalClip;
            _originalClip = null;
        }

        _draggingClip = null;

        _contentHostBack.TranslationX = 0;

        _lastBackTx = double.NaN;
        _lastOpacity = double.NaN;
        _lastClipWidth = double.NaN;
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
                _gestureHeight = GetEffectiveHeight();

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

                    _originalFrontBackground = _contentHostFront.BackgroundColor;
                    _contentHostFront.BackgroundColor = _originalFrontBackground;

                    if (_gestureWidth > 0)
                    {
                        _contentHostBack.TranslationX = 0;

                        _originalClip ??= _contentHostBack.Clip;

                        _draggingClip = new RectangleGeometry
                        {
                            Rect = new Rect(0, 0, 0, Math.Max(0, _gestureHeight))
                        };

                        _contentHostBack.Clip = _draggingClip;
                        _backDragDimOverlay.Clip = _draggingClip;
                    }

                    _backDragDimOverlay.Opacity = BackDragDimStartOpacity;
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

                if (_gestureWidth > 0 && currentX > _gestureWidth * DragCompleteThreshold && e.StatusType == GestureStatus.Completed)
                {
                    var animateClipTask = AnimateRevealClipAsync(currentX, _gestureWidth, _transitions.InteractiveGestureDuration, _transitions.InteractiveGestureEasing);
                    await vm.CompleteInteractiveBackAnimationAsync(_contentHostFront, _contentHostBack, currentX);
                    await animateClipTask;

                    _contentHostFront.TranslationX = 0;
                    await vm.CompleteInteractiveBackAsync();
                }
                else
                {
                    if (_gestureWidth > 0)
                    {
                        var animateClipTask = AnimateRevealClipAsync(currentX, 0, _transitions.InteractiveGestureDuration, _transitions.InteractiveGestureEasing);
                        await vm.CancelInteractiveBackAnimationAsync(_contentHostFront, _contentHostBack);
                        await animateClipTask;
                    }
                    else
                    {
                        await _contentHostFront.TranslateToAsync(0, 0, _transitions.InteractiveGestureDuration, _transitions.InteractiveGestureEasing);
                    }

                    vm.CancelInteractiveBack();
                }

                _contentHostFront.BackgroundColor = _originalFrontBackground;

                // Fully reset interactive artifacts so subsequent programmatic BackAsync is smooth
                ResetInteractiveState();

                _isDragging = false;
                break;
            }
        }
    }
}

