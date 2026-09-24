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

        if (ReferenceEquals(source, _edgeSource) && _edgeInteraction is not null)
        {
            ApplyEdgeStyle();
            return;
        }

        RemoveEdgeInteraction();
        WatchEdgeHandlers(source);

        if (source is null
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26)
            || _titleBar.Handler?.PlatformView is not UIView container
            || source.Handler?.PlatformView is not UIView platformView
            || (platformView as UIScrollView ?? SpineExtensions.FindScrollView(platformView)) is not { } scrollView)
            return;

        _edgeScrollView = scrollView;
        ApplyEdgeStyle();

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

    private void ApplyEdgeStyle()
    {
        if (_edgeScrollView is not { } scrollView
            || !OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
            return;

        // Never the automatic style: UIKit resolved it to soft on the iOS 26.4 simulator, but it
        // looked hard on an iOS 26.5 iPhone, where the two values could not be told apart.
        scrollView.TopEdgeEffect.Style = BarBackground is HeaderBarBackground.ScrollEdgeHard
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
            _edgeScrollView.TopEdgeEffect.Style = UIScrollEdgeEffectStyle.AutomaticStyle;

        _edgeInteraction = null;
        _edgeContainer = null;
        _edgeScrollView = null;
    }
}

#endif
