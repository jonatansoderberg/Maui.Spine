#if WINDOWS
using Microsoft.Maui.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    // ————————————————————————————————————————————————————————————————————————
    // Windows drag-region implementation
    // ————————————————————————————————————————————————————————————————————————

    private AppWindow? _dragAppWindow;
    private WinUIWindow? _dragWinUIWindow;
    private InputNonClientPointerSource? _nonClientPointerSource;
    private bool _dragRegionHooked;
    private bool _dragUpdatePending;
    private bool _dragSetupDeferred;

    // Cached last computed rectangles; skip SetRegionRects when unchanged.
    private RectInt32[]? _lastCaptionRects;
    private RectInt32[]? _lastPassthroughRects;

    // Reusable lists to avoid allocations on every update.
    private readonly List<RectInt32> _passthroughRectBuffer = new();
    private readonly List<Rect> _excludeRectBuffer = new();

    // WinUI LayoutUpdated handler reference (for teardown).
    private EventHandler<object>? _layoutUpdatedHandler;

    partial void SetupDragRegion()
    {
        if (_dragRegionHooked || _headerBorder == null)
            return;

        var mauiWindow = Application.Current?.Windows.FirstOrDefault();

        if (mauiWindow?.Handler?.PlatformView is not WinUIWindow winUIWindow)
        {
            // Window is not ready yet — defer until this control is Loaded,
            // which fires after the MAUI window/handler infrastructure is built.
            if (!_dragSetupDeferred)
            {
                _dragSetupDeferred = true;
                this.Loaded += OnLoadedRetrySetup;
            }
            return;
        }

        // Cancel any outstanding deferred hook.
        if (_dragSetupDeferred)
        {
            _dragSetupDeferred = false;
            this.Loaded -= OnLoadedRetrySetup;
        }

        var hWnd = WindowNative.GetWindowHandle(winUIWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var nonClientSource = InputNonClientPointerSource.GetForWindowId(windowId);

        if (appWindow == null || nonClientSource == null)
            return;

        // Do NOT set ExtendsContentIntoTitleBar here — the Spine framework
        // (SpineApplication) already manages the title bar and will set this
        // when appropriate.  Forcing it here would conflict with MAUI's
        // TitleBar control if one is present.

        // Collapse the OS-level title bar so the system no longer reserves
        // ~32 px of built-in caption area.  Our SetRegionRects(Caption) calls
        // become the sole source of drag regions, avoiding conflicts with the
        // implicit OS caption zone and the MAUI TitleBar handler.
        appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

        _dragAppWindow = appWindow;
        _dragWinUIWindow = winUIWindow;
        _nonClientPointerSource = nonClientSource;
        _dragRegionHooked = true;

        _headerBorder.SizeChanged += OnDragRegionInvalidated;
        _headerBorder.Loaded += OnDragRegionInvalidated;

        if (_headerTopActionsLayout != null)
            _headerTopActionsLayout.SizeChanged += OnDragRegionInvalidated;
        if (_headerBottomActionsLayout != null)
            _headerBottomActionsLayout.SizeChanged += OnDragRegionInvalidated;

        // Hook WinUI LayoutUpdated to re-apply regions after any layout pass.
        // The MAUI TitleBar handler also calls SetRegionRects (last-writer-wins),
        // so we must synchronously enforce our regions after every layout pass
        // to prevent the TitleBar handler from overwriting them.
        if (winUIWindow.Content is Microsoft.UI.Xaml.FrameworkElement rootContent)
        {
            _layoutUpdatedHandler = (_, _) => EnforceDragRegions();
            rootContent.LayoutUpdated += _layoutUpdatedHandler;
        }

        ScheduleDragRegionUpdate();
    }

    private void OnLoadedRetrySetup(object? sender, EventArgs e)
    {
        this.Loaded -= OnLoadedRetrySetup;
        _dragSetupDeferred = false;

        if (EnableHeaderAsDragRegionOnWindows && !_dragRegionHooked)
            SetupDragRegion();
    }

    partial void TeardownDragRegion()
    {
        if (_dragSetupDeferred)
        {
            _dragSetupDeferred = false;
            this.Loaded -= OnLoadedRetrySetup;
        }

        if (!_dragRegionHooked) return;
        _dragRegionHooked = false;

        if (_headerBorder != null)
        {
            _headerBorder.SizeChanged -= OnDragRegionInvalidated;
            _headerBorder.Loaded -= OnDragRegionInvalidated;
        }
        if (_headerTopActionsLayout != null)
            _headerTopActionsLayout.SizeChanged -= OnDragRegionInvalidated;
        if (_headerBottomActionsLayout != null)
            _headerBottomActionsLayout.SizeChanged -= OnDragRegionInvalidated;

        // Unhook WinUI LayoutUpdated.
        if (_layoutUpdatedHandler != null && _dragWinUIWindow?.Content is Microsoft.UI.Xaml.FrameworkElement rootContent)
        {
            rootContent.LayoutUpdated -= _layoutUpdatedHandler;
            _layoutUpdatedHandler = null;
        }

        try
        {
            _nonClientPointerSource?.ClearRegionRects(NonClientRegionKind.Caption);
            _nonClientPointerSource?.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch { /* window may already be torn down */ }

        _dragAppWindow = null;
        _dragWinUIWindow = null;
        _nonClientPointerSource = null;
        _lastCaptionRects = null;
        _lastPassthroughRects = null;
    }

    partial void ScheduleDragRegionUpdate()
    {
        if (!_dragRegionHooked || !EnableHeaderAsDragRegionOnWindows)
            return;
        if (_dragUpdatePending) return;

        _dragUpdatePending = true;

        // Debounce: coalesce rapid events into a single update (one frame).
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
        {
            _dragUpdatePending = false;
            UpdateDragRegions();
        });
    }

    private void OnDragRegionInvalidated(object? sender, EventArgs e) => ScheduleDragRegionUpdate();

    private void UpdateDragRegions()
    {
        if (_nonClientPointerSource == null || _dragWinUIWindow == null || _headerBorder == null)
            return;

        var xamlRoot = _dragWinUIWindow.Content?.XamlRoot;
        if (xamlRoot == null)
            return;

        double scale = xamlRoot.RasterizationScale;

        // Header visible bounds: the Border has a fixed HeightRequest but is
        // translated upward to collapse.  Visible height = max + translationY.
        double visibleHeight = _currentHeight > 0 ? _currentHeight : HeaderMaxHeight;
        double headerWidth = _headerBorder.Width;

        if (headerWidth <= 0 || visibleHeight <= 0)
            return;

        // Get header position relative to the MAUI window content area.
        var headerScreenPos = GetPositionRelativeToWindow(_headerBorder);

        // The visible portion starts at the bottom of the full header minus
        // visible height (because TranslationY moves it upward).
        double visibleTop = headerScreenPos.Y + (HeaderMaxHeight - visibleHeight);
        double visibleLeft = headerScreenPos.X;

        // Caption rect: the entire visible header area (draggable).
        var captionRect = ToScaledRect(new Rect(visibleLeft, visibleTop, headerWidth, visibleHeight), scale);
        var captionRects = new[] { captionRect };

        // Build passthrough rects from interactive children (buttons, entries, etc.).
        _excludeRectBuffer.Clear();
        CollectInteractiveElementBounds(_headerTopActionsLayout, visibleLeft, visibleTop, _excludeRectBuffer);
        CollectInteractiveElementBounds(_headerBottomActionsLayout, visibleLeft, visibleTop, _excludeRectBuffer);
        CollectInteractiveElementBounds(_headerOverlayLayout, visibleLeft, visibleTop, _excludeRectBuffer);

        _passthroughRectBuffer.Clear();
        foreach (var ex in _excludeRectBuffer)
            _passthroughRectBuffer.Add(ToScaledRect(ex, scale));

        var passthroughRects = _passthroughRectBuffer.ToArray();

        // Compare with last result to avoid unnecessary API calls.
        if (RectArraysEqual(_lastCaptionRects, captionRects) &&
            RectArraysEqual(_lastPassthroughRects, passthroughRects))
            return;

        _lastCaptionRects = captionRects;
        _lastPassthroughRects = passthroughRects;

        try
        {
            _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, captionRects);
            _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, passthroughRects);
        }
        catch
        {
            // Window may already be torn down.
        }
    }

    /// <summary>
    /// Synchronously checks whether the MAUI TitleBar handler (or any other
    /// caller) has overwritten our InputNonClientPointerSource regions since
    /// the last <see cref="UpdateDragRegions"/> call.  If the current regions
    /// differ from our cached values, immediately re-applies them.
    /// </summary>
    private void EnforceDragRegions()
    {
        if (!_dragRegionHooked || !EnableHeaderAsDragRegionOnWindows)
            return;
        if (_nonClientPointerSource == null || _lastCaptionRects == null || _lastPassthroughRects == null)
            return;

        try
        {
            var currentPassthrough = _nonClientPointerSource.GetRegionRects(NonClientRegionKind.Passthrough);
            if (!RectArraysEqual(currentPassthrough, _lastPassthroughRects))
            {
                _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, _lastCaptionRects);
                _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, _lastPassthroughRects);
            }
        }
        catch
        {
            // Window may be torn down or regions not yet available.
        }
    }

    // ————————————————————————————————————————————————————————————————————————
    // Helpers
    // ————————————————————————————————————————————————————————————————————————

    private static Point GetPositionRelativeToWindow(VisualElement element)
    {
        if (element.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement nativeView)
        {
            try
            {
                var transform = nativeView.TransformToVisual(null);
                var pos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                return new Point(pos.X, pos.Y);
            }
            catch
            {
                // Element might not be in the WinUI visual tree yet; fall through.
            }
        }

        double x = 0, y = 0;
        var current = element;
        while (current != null)
        {
            x += current.X + current.TranslationX;
            y += current.Y + current.TranslationY;
            current = current.Parent as VisualElement;
        }
        return new Point(x, y);
    }

    private void CollectInteractiveElementBounds(
        Layout? container,
        double headerLeft,
        double headerTop,
        List<Rect> results)
    {
        if (container == null)
            return;

        foreach (var child in container.Children)
        {
            if (child is not View view) continue;
            CollectFromViewTree(view, headerLeft, headerTop, results);
        }
    }

    private void CollectFromViewTree(View view, double headerLeft, double headerTop, List<Rect> results)
    {
        if (!view.IsVisible || view.Width <= 0 || view.Height <= 0)
            return;

        if (IsInteractiveElement(view))
        {
            var pos = GetPositionRelativeToWindow(view);
            results.Add(new Rect(pos.X, pos.Y, view.Width, view.Height));
            return; // children are covered by the parent exclusion
        }

        if (view is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is View childView)
                    CollectFromViewTree(childView, headerLeft, headerTop, results);
            }
        }
        else if (view is ContentView contentView && contentView.Content is View inner)
        {
            CollectFromViewTree(inner, headerLeft, headerTop, results);
        }
    }

    private bool IsInteractiveElement(View view)
    {
        if (IsInteractiveElementOverride is { } callback)
            return callback(view);

        return view is Button
            or ImageButton
            or Entry
            or SearchBar
            or Editor
            or Picker
            or DatePicker
            or TimePicker
            or Stepper
            or Slider
            or Switch
            or CheckBox
            or RadioButton
            or CollectionView;
    }

    private static RectInt32 ToScaledRect(Rect rect, double scale) =>
        new(
            (int)(rect.X * scale),
            (int)(rect.Y * scale),
            (int)(rect.Width * scale),
            (int)(rect.Height * scale));

    private static bool RectArraysEqual(RectInt32[]? a, RectInt32[]? b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].X != b[i].X || a[i].Y != b[i].Y ||
                a[i].Width != b[i].Width || a[i].Height != b[i].Height)
                return false;
        }
        return true;
    }
}
#endif
