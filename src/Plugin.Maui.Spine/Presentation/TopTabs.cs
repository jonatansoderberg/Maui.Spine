using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// One tab of a <see cref="TopTabs"/>: its segment's title and icon, and the view shown while it is
/// picked, as <see cref="Content"/> or, built on first show, <see cref="ContentTemplate"/>.
/// </summary>
[ContentProperty(nameof(Content))]
public class TopTab : Element
{
    /// <summary>Identifies the <see cref="Title"/> bindable property.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(TopTab), null);

    /// <summary>Identifies the <see cref="Svg"/> bindable property.</summary>
    public static readonly BindableProperty SvgProperty = BindableProperty.Create(
        nameof(Svg), typeof(string), typeof(TopTab), null);

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled), typeof(bool), typeof(TopTab), true);

    /// <summary>Identifies the <see cref="Content"/> bindable property.</summary>
    public static readonly BindableProperty ContentProperty = BindableProperty.Create(
        nameof(Content), typeof(View), typeof(TopTab), null);

    /// <summary>Identifies the <see cref="ContentTemplate"/> bindable property.</summary>
    public static readonly BindableProperty ContentTemplateProperty = BindableProperty.Create(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(TopTab), null);

    /// <inheritdoc cref="Segment.Title"/>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc cref="Segment.Svg"/>
    public string? Svg
    {
        get => (string?)GetValue(SvgProperty);
        set => SetValue(SvgProperty, value);
    }

    /// <inheritdoc cref="Segment.IsEnabled"/>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// The tab's view. It gets its handler, and so its native views, the first time the tab is
    /// shown; the view object itself is built with the page.
    /// </summary>
    public View? Content
    {
        get => (View?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>Builds the tab's view the first time the tab is shown. Used when <see cref="Content"/> is not set.</summary>
    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    /// <summary>The view made for the tab, once it has been shown.</summary>
    internal View? Realized { get; set; }
}

/// <summary>
/// In-page tabs: a <see cref="SegmentedControl"/> over the content of the picked <see cref="TopTab"/>.
/// A tab's content is created when it is first picked and kept afterwards, so its scroll position
/// and state survive a switch.
/// </summary>
/// <remarks>
/// When the page has not set <see cref="HeaderBar.ScrollSourceProperty"/> itself, the header bar
/// follows the first <see cref="ScrollView"/> or <see cref="CollectionView"/> of the tab showing,
/// and a tab's list built after the page was set up gets the page's scroll inset.
/// </remarks>
/// <example>
/// <code>
/// &lt;TopTabs SelectedIndex="{Binding Section}"&gt;
///     &lt;TopTab Title="Class"&gt; … &lt;/TopTab&gt;
///     &lt;TopTab Title="Club"&gt; … &lt;/TopTab&gt;
///     &lt;TopTab Title="Me" Svg="person.svg"&gt;
///         &lt;TopTab.ContentTemplate&gt;
///             &lt;DataTemplate&gt; … &lt;/DataTemplate&gt;
///         &lt;/TopTab.ContentTemplate&gt;
///     &lt;/TopTab&gt;
/// &lt;/TopTabs&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Tabs))]
public partial class TopTabs : ContentView
{
    /// <summary>Identifies the <see cref="SelectedIndex"/> bindable property.</summary>
    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(TopTabs), 0, BindingMode.TwoWay,
        coerceValue: static (_, value) => Math.Max(-1, (int)value),
        propertyChanged: static (bindable, oldValue, newValue) => ((TopTabs)bindable).OnSelectedIndexChanged((int)oldValue, (int)newValue));

    /// <summary>Identifies the <see cref="SelectedSegmentColor"/> bindable property.</summary>
    public static readonly BindableProperty SelectedSegmentColorProperty = BindableProperty.Create(
        nameof(SelectedSegmentColor), typeof(Color), typeof(TopTabs), null,
        propertyChanged: static (bindable, _, value) => ((TopTabs)bindable)._bar.SelectedSegmentColor = (Color?)value);

    /// <summary>Identifies the <see cref="BarMargin"/> bindable property.</summary>
    public static readonly BindableProperty BarMarginProperty = BindableProperty.Create(
        nameof(BarMargin), typeof(Thickness), typeof(TopTabs), new Thickness(16, 8),
        propertyChanged: static (bindable, _, value) => ((TopTabs)bindable)._bar.Margin = (Thickness)value);

    private readonly ObservableCollection<TopTab> _tabs = [];
    private readonly List<TopTab> _attached = [];
    private readonly SegmentedControl _bar;
    private readonly Grid _host = [];
    private View? _scrollSource;

    /// <summary>Tabs with no tabs yet.</summary>
    public TopTabs()
    {
        _bar = new SegmentedControl { Margin = BarMargin };
        _bar.SetBinding(SegmentedControl.SelectedIndexProperty, new Binding(nameof(SelectedIndex), BindingMode.TwoWay, source: this));

        Content = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            Children = { _bar, _host },
        };
        Grid.SetRow(_host, 1);

        _tabs.CollectionChanged += OnTabsChanged;
    }

    /// <summary>The tabs, in order.</summary>
    public IList<TopTab> Tabs => _tabs;

    /// <summary>The index of the tab showing; -1 for none. Two-way.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <inheritdoc cref="SegmentedControl.SelectedSegmentColor"/>
    public Color? SelectedSegmentColor
    {
        get => (Color?)GetValue(SelectedSegmentColorProperty);
        set => SetValue(SelectedSegmentColorProperty, value);
    }

    /// <summary>The space around the segment bar. Default 16 at the sides, 8 above and below.</summary>
    public Thickness BarMargin
    {
        get => (Thickness)GetValue(BarMarginProperty);
        set => SetValue(BarMarginProperty, value);
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes, by a tap or from code.</summary>
    public event EventHandler<SegmentSelectedEventArgs>? SelectionChanged;

    /// <inheritdoc/>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        UpdateScrollSource();
    }

    private void OnSelectedIndexChanged(int oldIndex, int newIndex)
    {
        Show();
        SelectionChanged?.Invoke(this, new SegmentSelectedEventArgs(oldIndex, newIndex));
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var tab in _attached.Except(_tabs).ToList())
        {
            if (tab.Realized is { } view)
                _host.Remove(view);

            tab.Realized = null;
            RemoveLogicalChild(tab);
            _attached.Remove(tab);
        }

        foreach (var tab in _tabs.Except(_attached).ToList())
        {
            AddLogicalChild(tab);
            _attached.Add(tab);
        }

        _bar.Segments.Clear();
        foreach (var tab in _tabs)
        {
            var segment = new Segment();
            segment.SetBinding(Segment.TitleProperty, new Binding(nameof(TopTab.Title), source: tab));
            segment.SetBinding(Segment.SvgProperty, new Binding(nameof(TopTab.Svg), source: tab));
            segment.SetBinding(Segment.IsEnabledProperty, new Binding(nameof(TopTab.IsEnabled), source: tab));
            _bar.Segments.Add(segment);
        }

        Show();
    }

    /// <summary>Builds the picked tab's view if it has none yet, and shows it alone.</summary>
    private void Show()
    {
        var index = SelectedIndex;
        var picked = index >= 0 && index < _tabs.Count ? _tabs[index] : null;

        if (picked is { Realized: null } && (picked.Content ?? picked.ContentTemplate?.CreateContent() as View) is { } view)
        {
            picked.Realized = view;
            _host.Add(view);
            ApplyScrollInset(view);
        }

        foreach (var tab in _tabs)
        {
            if (tab.Realized is { } realized)
                realized.IsVisible = ReferenceEquals(tab, picked);
        }

        UpdateScrollSource();
    }

    /// <summary>
    /// Points the page's header bar at the tab showing, unless the page chose its scroll source
    /// itself.
    /// </summary>
    private void UpdateScrollSource()
    {
        if (Page() is not { } page)
            return;

        var current = HeaderBar.GetScrollSource(page);
        if (current is not null && !ReferenceEquals(current, _scrollSource))
            return;

        var index = SelectedIndex;
        var source = index >= 0 && index < _tabs.Count && _tabs[index].Realized is { } view
            ? FirstScrollable(view)
            : null;

        if (ReferenceEquals(source, current))
            return;

        _scrollSource = source;
        HeaderBar.SetScrollSource(page, source);
    }

    /// <summary>
    /// Spine gives the page's first list the page's scroll inset when the page is set up; a tab
    /// built later takes the same.
    /// </summary>
    private void ApplyScrollInset(View view)
    {
        if (Page() is { BindingContext: ViewModelBase { ScrollInset: var inset } } && inset != Core.SafeAreaEdges.None
            && FirstScrollable(view) is { } scrollable
            && !scrollable.IsSet(SafeArea.ScrollInsetProperty))
        {
            SafeArea.SetScrollInset(scrollable, inset);
        }
    }

    private static View? FirstScrollable(View view) =>
        view is ScrollView or CollectionView ? view : NavigableMeta.FindFirstScrollable(view);

    private Element? Page()
    {
        for (var element = Parent; element is not null; element = element.Parent)
        {
            if (element is INavigable)
                return element;
        }

        return null;
    }
}
