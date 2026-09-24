using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Presentation;
using SafeAreaEdges = Plugin.Maui.Spine.Core.SafeAreaEdges;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Attached properties, set on the page, for a header that follows the page's scroll: a large title
/// (<see cref="NavigableAttribute.LargeTitle"/>) or a floating header with a solid background
/// (<see cref="HeaderBarBackground.Solid"/>). Which view it follows, and how far that view scrolls
/// before a large title has collapsed.
/// </summary>
/// <example>
/// <code>
/// [NavigableTab(Title = "Inbox", LargeTitle = true)]
/// </code>
/// <code>
/// &lt;SpinePage HeaderBar.ScrollSource="{x:Reference List}" …&gt;
///     &lt;CollectionView x:Name="List" …&gt;
///         &lt;CollectionView.Header&gt;
///             &lt;Label Text="{Binding Title}"
///                    FontSize="{x:Static HeaderBarConstants.LargeTitleFontSize}"
///                    FontAttributes="{x:Static HeaderBarConstants.LargeTitleFontAttributes}"
///                    HeightRequest="{x:Static HeaderBarConstants.LargeTitleHeight}"
///                    Margin="{x:Static HeaderBarConstants.LargeTitleMargin}"
///                    VerticalTextAlignment="Center" /&gt;
/// </code>
/// </example>
public static class HeaderBar
{
    /// <summary>
    /// The <see cref="ScrollView"/> or <see cref="CollectionView"/> whose offset drives the header. Unset, Spine follows the page's first one. Spine adds <c>Top</c> to its
    /// <c>SafeArea.ScrollInset</c> so its content starts under the header bar.
    /// </summary>
    public static readonly BindableProperty ScrollSourceProperty = BindableProperty.CreateAttached(
        "ScrollSource", typeof(View), typeof(HeaderBar), null,
        propertyChanged: static (bindable, _, _) => GetTracker(bindable)?.FindSource());

    /// <summary>Gets the view whose scroll offset collapses the header of <paramref name="page"/>.</summary>
    public static View? GetScrollSource(BindableObject page) => (View?)page.GetValue(ScrollSourceProperty);

    /// <summary>Sets the view whose scroll offset collapses the header of <paramref name="page"/>.</summary>
    public static void SetScrollSource(BindableObject page, View? value) => page.SetValue(ScrollSourceProperty, value);

    /// <summary>
    /// The scroll offset at which the header has collapsed: the bar's title is fully in. Defaults
    /// to <see cref="HeaderBarConstants.LargeTitleCollapseDistance"/>, the offset at which the text
    /// of a large title laid out with <see cref="HeaderBarConstants"/> has passed under the bar. A
    /// page whose large text sits lower, in a hero for instance, sets where that text has gone.
    /// </summary>
    public static readonly BindableProperty CollapseDistanceProperty = BindableProperty.CreateAttached(
        "CollapseDistance", typeof(double), typeof(HeaderBar), HeaderBarConstants.LargeTitleCollapseDistance,
        propertyChanged: static (bindable, _, _) => GetTracker(bindable)?.Update());

    /// <summary>Gets the scroll offset at which the header of <paramref name="page"/> has collapsed.</summary>
    public static double GetCollapseDistance(BindableObject page) => (double)page.GetValue(CollapseDistanceProperty);

    /// <summary>Sets the scroll offset at which the header of <paramref name="page"/> has collapsed.</summary>
    public static void SetCollapseDistance(BindableObject page, double value) => page.SetValue(CollapseDistanceProperty, value);

    static readonly BindableProperty TrackerProperty = BindableProperty.CreateAttached(
        "Tracker", typeof(CollapseTracker), typeof(HeaderBar), null);

    static CollapseTracker? GetTracker(BindableObject page) => (CollapseTracker?)page.GetValue(TrackerProperty);

    /// <summary>Starts following the page's scroll source; called by Spine for a page whose header follows it.</summary>
    internal static void Track(View page, ViewModelBase viewModel)
    {
        if (GetTracker(page) is not null)
            return;

        var tracker = new CollapseTracker(page, viewModel);
        page.SetValue(TrackerProperty, tracker);
        tracker.FindSource();
    }

    /// <summary>
    /// The title progress for <paramref name="offset"/>: 0 until the large title's last
    /// <see cref="HeaderBarConstants.LargeTitleFadeLength"/> points reach the bar, 1 once they are
    /// under it. With Reduce Motion it switches at the middle of that stretch.
    /// </summary>
    internal static double TitleProgress(double offset, double distance, bool reduced)
    {
        var fade = Math.Min(HeaderBarConstants.LargeTitleFadeLength, Math.Max(distance, 0));
        return Progress(offset - (distance - fade), fade, reduced);
    }

    /// <summary>The bar background's progress: in over the first <see cref="HeaderBarConstants.ScrollEdgeFadeLength"/> points of scroll.</summary>
    internal static double ScrollEdgeProgress(double offset, bool reduced) =>
        Progress(offset, HeaderBarConstants.ScrollEdgeFadeLength, reduced);

    static double Progress(double travelled, double length, bool reduced)
    {
        if (length <= 0)
            return travelled >= 0 ? 1 : 0;

        if (reduced)
            return travelled >= length / 2 ? 1 : 0;

        // Hundredths: finer than the eye can tell, and the page is not told on every sub-point of scroll.
        return Math.Round(Math.Clamp(travelled / length, 0, 1), 2);
    }

    /// <summary>
    /// Follows one collapsing page: finds its scroll source (waiting for it when the page builds
    /// it later, as a state view does), and turns every scroll event into the view model's two
    /// progress values.
    /// </summary>
    sealed class CollapseTracker(View page, ViewModelBase viewModel)
    {
        View? _source;
        bool _waiting;

        public void FindSource()
        {
            var source = GetScrollSource(page) ?? Services.NavigableMeta.FindFirstScrollable(page);

            if (ReferenceEquals(source, _source))
                return;

            Detach();

            if (source is null)
            {
                if (!_waiting)
                {
                    page.DescendantAdded += OnDescendantAdded;
                    _waiting = true;
                }

                return;
            }

            if (_waiting)
            {
                page.DescendantAdded -= OnDescendantAdded;
                _waiting = false;
            }

            _source = source;
            SafeArea.SetScrollInset(source, SafeArea.GetScrollInset(source) | SafeAreaEdges.Top);

            switch (source)
            {
                case ScrollView scrollView:
                    scrollView.Scrolled += OnScrolled;
                    break;
                case ItemsView itemsView:
                    itemsView.Scrolled += OnItemsScrolled;
                    break;
            }

            Update();
        }

        void Detach()
        {
            switch (_source)
            {
                case ScrollView scrollView:
                    scrollView.Scrolled -= OnScrolled;
                    break;
                case ItemsView itemsView:
                    itemsView.Scrolled -= OnItemsScrolled;
                    break;
            }

            _source = null;
        }

        void OnDescendantAdded(object? sender, ElementEventArgs e)
        {
            if (e.Element is ScrollView or CollectionView)
                FindSource();
        }

        double _offset;

        void OnScrolled(object? sender, ScrolledEventArgs e) => Update(e.ScrollY);

        void OnItemsScrolled(object? sender, ItemsViewScrolledEventArgs e) => Update(e.VerticalOffset);

        public void Update() => Update(_source is ScrollView scrollView ? scrollView.ScrollY : _offset);

        void Update(double reported)
        {
            _offset = NativeOffset(_source) ?? reported;

            var reduced = ReducedMotion.IsOn;
            viewModel.HeaderBarCollapseProgress = TitleProgress(_offset, GetCollapseDistance(page), reduced);
            viewModel.ScrollEdgeProgress = ScrollEdgeProgress(_offset, reduced);
        }

#if IOS || MACCATALYST
        UIKit.UIScrollView? _native;
        IElementHandler? _nativeHandler;

        // MAUI reports the raw content offset, which starts at minus the content inset once the
        // scroll view has one; measured from the inset instead, the top is 0 on every platform.
        double? NativeOffset(View? source)
        {
            if (source?.Handler is not { PlatformView: UIKit.UIView platformView } handler)
                return null;

            if (!ReferenceEquals(handler, _nativeHandler))
            {
                _nativeHandler = handler;
                _native = platformView as UIKit.UIScrollView ?? SpineExtensions.FindScrollView(platformView);
            }

            return _native is { } scrollView
                ? (double)(scrollView.ContentOffset.Y + scrollView.AdjustedContentInset.Top)
                : null;
        }
#else
        static double? NativeOffset(View? source) => null;
#endif
    }
}
