#if IOS || MACCATALYST

using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class PagePresenter
{
    private UIScrollEdgeElementContainerInteraction? _edgeInteraction;
    private UIView? _edgeContainer;
    private View? _edgeSource;
    private UIScrollView? _edgeScrollView;
    private SoftEdgeStretch? _softEdgeStretch;

    /// <summary>How far below the header bar a stretched soft edge (iOS 27) fades out.</summary>
    private const double SoftEdgeTail = 36;

    // The container a SoftStatusBar effect is sized to: as tall as the status bar, holding a label,
    // because UIKit ignores a container with no labels, images or controls in it.
    private ContentView? _statusBarEdge;

    partial void AddStatusBarEdge()
    {
        _statusBarEdge = new ContentView
        {
            InputTransparent = true,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 2,
            Content = new Label { Text = " ", VerticalOptions = LayoutOptions.Fill },
        };
        Children.Add(_statusBarEdge);
    }

    partial void ApplyStatusBarEdge()
    {
        if (_statusBarEdge is null)
            return;

        _statusBarEdge.IsVisible = BarBackground is HeaderBarBackground.SoftStatusBar && UsesSystemScrollEdge;
        _statusBarEdge.HeightRequest = _page?.SystemBarInsets.Top ?? 0;
    }

    /// <summary>The view the interaction goes on: the status-bar element, or the title row.</summary>
    private View EdgeContainerView =>
        BarBackground is HeaderBarBackground.SoftStatusBar && _statusBarEdge is not null ? _statusBarEdge : _titleBar;

    /// <summary>
    /// Tells the page's scroll view that the title row floats over its top edge, so UIKit draws
    /// the same edge effect behind it as behind a navigation bar. UIKit sizes the effect to the
    /// elements in the container, not to the container: it reaches from the top of the scroll view
    /// to below the lowest of them. The title label fills the bar's height for that reason, so the
    /// effect covers the status bar and the whole bar; the actions float in the same band.
    /// </summary>
    partial void UpdateSystemScrollEdge()
    {
        var source = UsesSystemScrollEdge ? _page?.HeaderBarScrollSource : null;

        if (ReferenceEquals(source, _edgeSource) && _edgeInteraction is not null
            && ReferenceEquals(EdgeContainerView.Handler?.PlatformView, _edgeContainer))
        {
            ApplyEdgeStyle();
            UpdateSoftEdgeStretch();
            return;
        }

        RemoveEdgeInteraction();
        WatchEdgeHandlers(source);

        if (source is null
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26)
            || EdgeContainerView.Handler?.PlatformView is not UIView container
            || source.Handler?.PlatformView is not UIView platformView
            || (platformView as UIScrollView ?? SpineExtensions.FindScrollView(platformView)) is not { } scrollView)
            return;

        _edgeScrollView = scrollView;
        ApplyEdgeStyle();
        ApplySystemScrollEdgeRest();

        _edgeInteraction = new UIScrollEdgeElementContainerInteraction
        {
            ScrollView = scrollView,
            Edge = UIRectEdge.Top,
        };
        container.AddInteraction(_edgeInteraction);
        _edgeContainer = container;
        UpdateSoftEdgeStretch();
    }

    partial void RefreshSoftEdge() => _softEdgeStretch?.Apply();

    // From iOS 27 UIKit's soft edge only covers the status bar; SoftEdge asks for the whole header.
    private void UpdateSoftEdgeStretch()
    {
        var wanted = BarBackground is HeaderBarBackground.SoftEdge
            && _edgeScrollView is not null
            && (OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27));

        if (!wanted)
        {
            _softEdgeStretch?.Dispose();
            _softEdgeStretch = null;
            return;
        }

        _softEdgeStretch ??= new SoftEdgeStretch(_edgeScrollView!, () => RowDefinitions[0].Height.Value + SoftEdgeTail);
        _softEdgeStretch.Apply();
    }

    // Either side may not have a platform view yet (the page is built before it is shown), or may
    // get a new one; the interaction is installed once both exist.
    private void WatchEdgeHandlers(View? source)
    {
        if (_edgeSource is not null)
            _edgeSource.HandlerChanged -= OnEdgeHandlerChanged;

        _edgeSource = source;

        if (source is not null)
            source.HandlerChanged += OnEdgeHandlerChanged;

        foreach (var container in (ReadOnlySpan<View?>)[_titleBar, _statusBarEdge])
        {
            if (container is null)
                continue;

            container.HandlerChanged -= OnEdgeHandlerChanged;
            if (source is not null)
                container.HandlerChanged += OnEdgeHandlerChanged;
        }
    }

    private void OnEdgeHandlerChanged(object? sender, EventArgs e)
    {
        RemoveEdgeInteraction();
        UpdateSystemScrollEdge();
    }

    // A navigation bar shows no edge until content scrolls under it, but on iOS 27 the interaction's
    // hard band shows over a scroll view still at its top, and an Overlay page's top is under the
    // bar at rest and would get any style's edge there.
    partial void ApplySystemScrollEdgeRest()
    {
        if (_edgeScrollView is not { } scrollView
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
            return;

        var hidden = _page is not { ScrollEdgeProgress: > 0 };
        if (scrollView.TopEdgeEffect.Hidden != hidden)
            scrollView.TopEdgeEffect.Hidden = hidden;
    }

    private void ApplyEdgeStyle()
    {
        if (_edgeScrollView is not { } scrollView
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
            return;

        // Never the automatic style: UIKit resolves it to soft on iOS 26.4 and to hard on iOS 27.
        scrollView.TopEdgeEffect.Style = BarBackground is HeaderBarBackground.HardEdge
            ? UIScrollEdgeEffectStyle.HardStyle
            : UIScrollEdgeEffectStyle.SoftStyle;
    }

    private void RemoveEdgeInteraction()
    {
        if (_edgeInteraction is not null)
            _edgeContainer?.RemoveInteraction(_edgeInteraction);

        // What the scroll view shows under the status bar without the interaction is the system's own.
        if (_edgeScrollView is not null
            && (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)))
        {
            _edgeScrollView.TopEdgeEffect.Style = UIScrollEdgeEffectStyle.AutomaticStyle;
            _edgeScrollView.TopEdgeEffect.Hidden = false;
        }

        _softEdgeStretch?.Dispose();
        _softEdgeStretch = null;
        _edgeInteraction = null;
        _edgeContainer = null;
        _edgeScrollView = null;
    }
}

#endif
