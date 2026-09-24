#if IOS || MACCATALYST

using Plugin.Maui.Spine.Extensions;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class PagePresenter
{
    private UIScrollEdgeElementContainerInteraction? _edgeInteraction;
    private UIView? _edgeContainer;
    private View? _edgeSource;

    /// <summary>
    /// Tells the page's scroll view that the title row floats over its top edge, so UIKit draws
    /// the same soft edge effect behind it as behind a navigation bar. The title row is the
    /// container because it spans the bar's width at the bar's height; the actions float in the
    /// same band and are covered with it.
    /// </summary>
    partial void UpdateSystemScrollEdge()
    {
        var source = UsesSystemScrollEdge ? _page?.HeaderBarScrollSource : null;

        if (ReferenceEquals(source, _edgeSource) && _edgeInteraction is not null)
            return;

        RemoveEdgeInteraction();
        WatchEdgeHandlers(source);

        if (source is null
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26)
            || _titleBar.Handler?.PlatformView is not UIView container
            || source.Handler?.PlatformView is not UIView platformView
            || (platformView as UIScrollView ?? SpineExtensions.FindScrollView(platformView)) is not { } scrollView)
            return;

        scrollView.TopEdgeEffect.Style = UIScrollEdgeEffectStyle.SoftStyle;

        _edgeInteraction = new UIScrollEdgeElementContainerInteraction
        {
            ScrollView = scrollView,
            Edge = UIRectEdge.Top,
        };
        container.AddInteraction(_edgeInteraction);
        _edgeContainer = container;
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

        _titleBar.HandlerChanged -= OnEdgeHandlerChanged;
        if (source is not null)
            _titleBar.HandlerChanged += OnEdgeHandlerChanged;
    }

    private void OnEdgeHandlerChanged(object? sender, EventArgs e)
    {
        RemoveEdgeInteraction();
        UpdateSystemScrollEdge();
    }

    private void RemoveEdgeInteraction()
    {
        if (_edgeInteraction is not null)
            _edgeContainer?.RemoveInteraction(_edgeInteraction);

        _edgeInteraction = null;
        _edgeContainer = null;
    }
}

#endif
