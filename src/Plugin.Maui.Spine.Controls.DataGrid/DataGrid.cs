using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A responsive row grid on <see cref="CollectionView"/>: fixed-height rows, named layouts with
/// independent header and value placement, sorting, grouping, pull-to-refresh, load more and swipe
/// actions.
/// </summary>
/// <remarks>
/// Columns define WHAT the data is; <see cref="Layouts"/> define WHERE it renders, and
/// <see cref="LayoutMode"/> picks the active one. Each row is one generated <see cref="Grid"/> with
/// a fixed height, wrapped in a <see cref="SwipeView"/> only when swipe actions exist. Cells bind
/// by property path, since templates built in code cannot use compiled bindings; see
/// <see cref="TrimmingMessage"/>.
/// </remarks>
[ContentProperty(nameof(Columns))]
public partial class DataGrid : ContentView
{
    internal const string TrimmingMessage =
        "DataGrid reads row values by property path through reflection. The row type's public properties must survive trimming; "
        + "MAUI's default (partial) trimming keeps the app's own types whole.";

    private readonly Grid _root;
    private readonly ContentView _headerHost;
    private readonly RefreshView _refreshView;
    private readonly CollectionView _collectionView;
    private readonly Grid _statusRow;
    private readonly Label _statusLabel;
    private readonly Label _loadMoreLabel;
    private readonly ActivityIndicator _loadMoreIndicator;

    private IList? _sourceList;
    private INotifyCollectionChanged? _observedSource;
    private ObservableCollection<object> _displayItems = [];
    private bool _rebuildQueued;
    private bool _queuedRebuildIsAutoFitOnly;

    // The list width the realized rows were measured at; -1 while no row has been measured.
    private double _rowsMeasuredAtWidth = -1;

    public DataGrid()
    {
        _headerHost = new ContentView { IsVisible = false };

        _collectionView = new CollectionView
        {
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            SelectionMode = SelectionMode.None,
            RemainingItemsThreshold = -1,
        };
        _collectionView.RemainingItemsThresholdReached += OnRemainingItemsThresholdReached;
        _collectionView.Scrolled += (_, e) =>
        {
            _firstVisibleIndex = e.FirstVisibleItemIndex;
            // A finger that scrolls is not holding a row still.
            CancelRowPress();
        };

        // The CollectionView sits in the root directly and moves into the RefreshView only while
        // pull-to-refresh is available: a disabled RefreshView disables every row below it.
        _refreshView = new RefreshView { Content = new Grid() };
        _refreshView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == RefreshView.IsRefreshingProperty.PropertyName)
                IsRefreshing = _refreshView.IsRefreshing;
        };

        _statusLabel = new Label { VerticalTextAlignment = TextAlignment.Center };
        _loadMoreLabel = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            TextDecorations = TextDecorations.Underline,
            IsVisible = false,
        };
        var loadMoreTap = new TapGestureRecognizer();
        loadMoreTap.Tapped += (_, _) => RequestLoadMore();
        _loadMoreLabel.GestureRecognizers.Add(loadMoreTap);

        _loadMoreIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            HeightRequest = 20,
            WidthRequest = 20,
        };
        _statusRow = new Grid
        {
            IsVisible = false,
            Padding = new Thickness(0, 12, 0, 8),
            // iOS: a UICollectionView paints rows past its bounds and its platform view can end up
            // above later siblings. A ZIndex and an opaque background keep the status readable.
            ZIndex = 1,
        };
        _statusRow.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children = { _loadMoreIndicator, _statusLabel, _loadMoreLabel },
        });

        _root = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            ],
        };
        _root.Add(_headerHost, 0, 0);
        _root.Add(_collectionView, 0, 1);
        _root.Add(_statusRow, 0, 2);
        Content = _root;

        Columns.CollectionChanged += OnConfigCollectionChanged;
        Layouts.CollectionChanged += OnConfigCollectionChanged;
        LeftSwipeActions.CollectionChanged += OnConfigCollectionChanged;
        RightSwipeActions.CollectionChanged += OnConfigCollectionChanged;

        // Colours and text are assigned once per build, so a theme or culture change needs a rebuild.
        SpineTheme.Track(this, OnThemeChanged);

        // Auto widths measured before the grid had a handler are estimates; measure again once
        // attached, but only rebuild the rows when a width actually moved.
        HandlerChanged += (_, _) =>
        {
            if (Handler is not null)
            {
                ReattachSourceEvents();
                QueueRebuild(autoFitOnly: true);
            }
        };
        _collectionView.SizeChanged += OnCollectionViewSizeChanged;

        UpdateEmptyView();
    }

    /// <summary>
    /// Rebuilds the rows when the list's width changes after they were measured. MeasureFirstItem
    /// sizes every row once and does not measure again for a width change alone, so the rows would
    /// keep the old width while the header follows the new one. A width that arrives before any row
    /// was measured is only recorded; the rows are measured at it anyway.
    /// </summary>
    private void OnCollectionViewSizeChanged(object? sender, EventArgs e)
    {
        var width = _collectionView.Width;
        if (width <= 0 || Math.Abs(width - _rowsMeasuredAtWidth) < 0.5)
            return;

        var rowsAlreadyMeasured = _rowsMeasuredAtWidth > 0 && _collectionView.ItemTemplate is not null;
        _rowsMeasuredAtWidth = width;
        if (rowsAlreadyMeasured)
            QueueRebuild();
    }

    // ---------------------------------------------------------------- collections

    public ObservableCollection<DataGridColumn> Columns { get; } = [];

    public ObservableCollection<DataGridLayout> Layouts { get; } = [];

    public ObservableCollection<DataGridSwipeAction> LeftSwipeActions { get; } = [];

    public ObservableCollection<DataGridSwipeAction> RightSwipeActions { get; } = [];

    // ---------------------------------------------------------------- bindable properties

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).OnItemsSourceChanged());

    public static readonly BindableProperty LayoutModeProperty = BindableProperty.Create(
        nameof(LayoutMode), typeof(string), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).QueueRebuild());

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(DataGridStyleOptions), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).OnStyleOptionsChanged());

    public static readonly BindableProperty RowTappedCommandProperty = BindableProperty.Create(
        nameof(RowTappedCommand), typeof(ICommand), typeof(DataGrid));

    public static readonly BindableProperty IsCellCopyEnabledProperty = BindableProperty.Create(
        nameof(IsCellCopyEnabled), typeof(bool), typeof(DataGrid), true,
        propertyChanged: (b, _, _) => ((DataGrid)b).QueueRebuild());

    public static readonly BindableProperty RefreshCommandProperty = BindableProperty.Create(
        nameof(RefreshCommand), typeof(ICommand), typeof(DataGrid),
        propertyChanged: (b, _, value) =>
        {
            var grid = (DataGrid)b;
            grid._refreshView.Command = (ICommand?)value;
            grid.UpdateRefreshEnabled();
        });

    public static readonly BindableProperty IsRefreshingProperty = BindableProperty.Create(
        nameof(IsRefreshing), typeof(bool), typeof(DataGrid), false, BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((DataGrid)b)._refreshView.IsRefreshing = (bool)value);

    public static readonly BindableProperty IsRefreshEnabledProperty = BindableProperty.Create(
        nameof(IsRefreshEnabled), typeof(bool), typeof(DataGrid), true,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateRefreshEnabled());

    public static readonly BindableProperty LoadMoreCommandProperty = BindableProperty.Create(
        nameof(LoadMoreCommand), typeof(ICommand), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateLoadMoreState());

    public static readonly BindableProperty HasMoreItemsProperty = BindableProperty.Create(
        nameof(HasMoreItems), typeof(bool), typeof(DataGrid), false,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateLoadMoreState());

    public static readonly BindableProperty IsLoadingMoreProperty = BindableProperty.Create(
        nameof(IsLoadingMore), typeof(bool), typeof(DataGrid), false,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty LoadMoreThresholdProperty = BindableProperty.Create(
        nameof(LoadMoreThreshold), typeof(int), typeof(DataGrid), 5,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateLoadMoreState());

    public static readonly BindableProperty ShowLoadedStatusProperty = BindableProperty.Create(
        nameof(ShowLoadedStatus), typeof(bool), typeof(DataGrid), false,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty StatusTextFormatProperty = BindableProperty.Create(
        nameof(StatusTextFormat), typeof(string), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty StatusTextFormatWithTotalProperty = BindableProperty.Create(
        nameof(StatusTextFormatWithTotal), typeof(string), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty StatusTextFormatMoreProperty = BindableProperty.Create(
        nameof(StatusTextFormatMore), typeof(string), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty TotalItemCountProperty = BindableProperty.Create(
        nameof(TotalItemCount), typeof(int), typeof(DataGrid), -1,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateStatusRow());

    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(
        nameof(IsLoading), typeof(bool), typeof(DataGrid), false,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateEmptyView());

    public static readonly BindableProperty LoadingTextProperty = BindableProperty.Create(
        nameof(LoadingText), typeof(string), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateEmptyView());

    public static readonly BindableProperty EmptyTextProperty = BindableProperty.Create(
        nameof(EmptyText), typeof(string), typeof(DataGrid), null,
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateEmptyView());

    public static readonly BindableProperty LoadingViewProperty = BindableProperty.Create(
        nameof(LoadingView), typeof(View), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateEmptyView());

    public static readonly BindableProperty EmptyViewProperty = BindableProperty.Create(
        nameof(EmptyView), typeof(View), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).UpdateEmptyView());

    public static readonly BindableProperty SortColumnKeyProperty = BindableProperty.Create(
        nameof(SortColumnKey), typeof(string), typeof(DataGrid), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DataGrid)b).OnSortStateChanged());

    public static readonly BindableProperty SortDirectionProperty = BindableProperty.Create(
        nameof(SortDirection), typeof(DataGridSortDirection), typeof(DataGrid),
        DataGridSortDirection.None, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DataGrid)b).OnSortStateChanged());

    public static readonly BindableProperty SortChangedCommandProperty = BindableProperty.Create(
        nameof(SortChangedCommand), typeof(ICommand), typeof(DataGrid));

    public static readonly BindableProperty GroupByPathProperty = BindableProperty.Create(
        nameof(GroupByPath), typeof(string), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).OnGroupingConfigChanged());

    public static readonly BindableProperty GroupDisplayPathProperty = BindableProperty.Create(
        nameof(GroupDisplayPath), typeof(string), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).OnGroupingConfigChanged());

    public static readonly BindableProperty GroupHeaderTemplateProperty = BindableProperty.Create(
        nameof(GroupHeaderTemplate), typeof(DataTemplate), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).QueueRebuild());

    public static readonly BindableProperty GroupTappedCommandProperty = BindableProperty.Create(
        nameof(GroupTappedCommand), typeof(ICommand), typeof(DataGrid));

    public static readonly BindableProperty UngroupedItemsModeProperty = BindableProperty.Create(
        nameof(UngroupedItemsMode), typeof(DataGridUngroupedMode), typeof(DataGrid),
        DataGridUngroupedMode.Root,
        propertyChanged: (b, _, _) => ((DataGrid)b).OnGroupingConfigChanged());

    public static readonly BindableProperty UngroupedGroupTextProperty = BindableProperty.Create(
        nameof(UngroupedGroupText), typeof(string), typeof(DataGrid),
        propertyChanged: (b, _, _) => ((DataGrid)b).OnGroupingConfigChanged());

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Name of the active layout, e.g. "Wide" or "Narrow"; the first layout when unset or unknown.
    /// In a VisualStateManager setter the property must be type-qualified: <c>DataGrid.LayoutMode</c>.
    /// </summary>
    public string? LayoutMode
    {
        get => (string?)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    /// <summary>This grid's style; see <see cref="DataGridStyleOptions"/> for the resolution chain.</summary>
    public DataGridStyleOptions? StyleOptions
    {
        get => (DataGridStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>Run with the row item when a row is tapped. A cell's own command takes precedence.</summary>
    public ICommand? RowTappedCommand
    {
        get => (ICommand?)GetValue(RowTappedCommandProperty);
        set => SetValue(RowTappedCommandProperty, value);
    }

    /// <summary>Long-pressing a text cell copies what it shows. On by default.</summary>
    public bool IsCellCopyEnabled
    {
        get => (bool)GetValue(IsCellCopyEnabledProperty);
        set => SetValue(IsCellCopyEnabledProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public bool IsRefreshing
    {
        get => (bool)GetValue(IsRefreshingProperty);
        set => SetValue(IsRefreshingProperty, value);
    }

    public bool IsRefreshEnabled
    {
        get => (bool)GetValue(IsRefreshEnabledProperty);
        set => SetValue(IsRefreshEnabledProperty, value);
    }

    /// <summary>
    /// Run when the list nears its end while <see cref="HasMoreItems"/> is true, or when the load-more
    /// link in the status row is tapped.
    /// </summary>
    public ICommand? LoadMoreCommand
    {
        get => (ICommand?)GetValue(LoadMoreCommandProperty);
        set => SetValue(LoadMoreCommandProperty, value);
    }

    public bool HasMoreItems
    {
        get => (bool)GetValue(HasMoreItemsProperty);
        set => SetValue(HasMoreItemsProperty, value);
    }

    public bool IsLoadingMore
    {
        get => (bool)GetValue(IsLoadingMoreProperty);
        set => SetValue(IsLoadingMoreProperty, value);
    }

    /// <summary>How many rows before the end <see cref="LoadMoreCommand"/> runs.</summary>
    public int LoadMoreThreshold
    {
        get => (int)GetValue(LoadMoreThresholdProperty);
        set => SetValue(LoadMoreThresholdProperty, value);
    }

    /// <summary>Show the row count in the status row once everything is loaded.</summary>
    public bool ShowLoadedStatus
    {
        get => (bool)GetValue(ShowLoadedStatusProperty);
        set => SetValue(ShowLoadedStatusProperty, value);
    }

    /// <summary>Format of the loaded count, e.g. <c>"{0} orders"</c>; defaults to <c>DataGrid.Status.Loaded</c>.</summary>
    public string? StatusTextFormat
    {
        get => (string?)GetValue(StatusTextFormatProperty);
        set => SetValue(StatusTextFormatProperty, value);
    }

    /// <summary>Format used when <see cref="TotalItemCount"/> is set; defaults to <c>DataGrid.Status.LoadedOfTotal</c>.</summary>
    public string? StatusTextFormatWithTotal
    {
        get => (string?)GetValue(StatusTextFormatWithTotalProperty);
        set => SetValue(StatusTextFormatWithTotalProperty, value);
    }

    /// <summary>
    /// Format shown while more pages exist; defaults to <c>DataGrid.Status.More</c>. An empty string
    /// shows no count, only the load-more link.
    /// </summary>
    public string? StatusTextFormatMore
    {
        get => (string?)GetValue(StatusTextFormatMoreProperty);
        set => SetValue(StatusTextFormatMoreProperty, value);
    }

    /// <summary>Total number of items on the server, or -1 when unknown.</summary>
    public int TotalItemCount
    {
        get => (int)GetValue(TotalItemCountProperty);
        set => SetValue(TotalItemCountProperty, value);
    }

    /// <summary>Shows the loading view in place of the empty view while true.</summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>Text under the loading spinner; defaults to <c>DataGrid.Loading</c>.</summary>
    public string? LoadingText
    {
        get => (string?)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    /// <summary>Text of the empty view; defaults to <c>DataGrid.Empty</c>.</summary>
    public string? EmptyText
    {
        get => (string?)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public View? LoadingView
    {
        get => (View?)GetValue(LoadingViewProperty);
        set => SetValue(LoadingViewProperty, value);
    }

    public View? EmptyView
    {
        get => (View?)GetValue(EmptyViewProperty);
        set => SetValue(EmptyViewProperty, value);
    }

    /// <summary>Key of the sorted column, or null. Two-way.</summary>
    public string? SortColumnKey
    {
        get => (string?)GetValue(SortColumnKeyProperty);
        set => SetValue(SortColumnKeyProperty, value);
    }

    public DataGridSortDirection SortDirection
    {
        get => (DataGridSortDirection)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }

    /// <summary>
    /// When set, the grid does not sort locally; it reports the new order as a
    /// <see cref="DataGridSortDescriptor"/> and the view model supplies the sorted data.
    /// </summary>
    public ICommand? SortChangedCommand
    {
        get => (ICommand?)GetValue(SortChangedCommandProperty);
        set => SetValue(SortChangedCommandProperty, value);
    }

    /// <summary>
    /// Property path whose value groups the rows. Rows with equal keys render under an expandable
    /// header; rows with a null or empty key follow <see cref="UngroupedItemsMode"/>.
    /// </summary>
    public string? GroupByPath
    {
        get => (string?)GetValue(GroupByPathProperty);
        set => SetValue(GroupByPathProperty, value);
    }

    /// <summary>Property path, read from a group's first row, for the header text; the key when unset.</summary>
    public string? GroupDisplayPath
    {
        get => (string?)GetValue(GroupDisplayPathProperty);
        set => SetValue(GroupDisplayPathProperty, value);
    }

    /// <summary>
    /// A custom group header; its binding context is the <see cref="DataGridGroup"/>. The template
    /// owns all interaction: bind <see cref="DataGridGroup.IsExpanded"/> two-way to toggle.
    /// </summary>
    public DataTemplate? GroupHeaderTemplate
    {
        get => (DataTemplate?)GetValue(GroupHeaderTemplateProperty);
        set => SetValue(GroupHeaderTemplateProperty, value);
    }

    /// <summary>
    /// Run with the <see cref="DataGridGroup"/> when the default header's text is tapped (the chevron
    /// toggles). Not raised for the ungrouped group.
    /// </summary>
    public ICommand? GroupTappedCommand
    {
        get => (ICommand?)GetValue(GroupTappedCommandProperty);
        set => SetValue(GroupTappedCommandProperty, value);
    }

    public DataGridUngroupedMode UngroupedItemsMode
    {
        get => (DataGridUngroupedMode)GetValue(UngroupedItemsModeProperty);
        set => SetValue(UngroupedItemsModeProperty, value);
    }

    /// <summary>Title of the ungrouped group; defaults to <c>DataGrid.Ungrouped</c>.</summary>
    public string? UngroupedGroupText
    {
        get => (string?)GetValue(UngroupedGroupTextProperty);
        set => SetValue(UngroupedGroupTextProperty, value);
    }

    private DataGridStyleOptions ResolveOptions() => DataGridStyleOptions.Resolve(StyleOptions);

    private void OnStyleOptionsChanged()
    {
        QueueRebuild();
        UpdateStatusRow();
        UpdateEmptyView();
    }

    // ---------------------------------------------------------------- binding context flow

    private void OnConfigCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is DataGridSwipeAction action && ReferenceEquals(action.Parent, this))
                    action.Parent = null;
            }
        }
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is DataGridSwipeAction action)
                    action.Parent = this;
                if (item is BindableObject bindable)
                    SetInheritedBindingContext(bindable, BindingContext);
            }
        }
        QueueRebuild();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        // Columns and actions are not in the visual tree; hand them the page's binding context so a
        // {Binding SomeCommand} on them reaches the view model.
        foreach (var column in Columns)
            SetInheritedBindingContext(column, BindingContext);
        foreach (var action in LeftSwipeActions)
            SetInheritedBindingContext(action, BindingContext);
        foreach (var action in RightSwipeActions)
            SetInheritedBindingContext(action, BindingContext);
    }

    // ---------------------------------------------------------------- items source

    // True while the grid is off the visual tree and its CollectionChanged subscription is parked,
    // so a source that outlives the page does not root the grid through the event.
    private bool _sourceEventsDetached;

    private void OnItemsSourceChanged()
    {
        if (_observedSource is not null)
        {
            _observedSource.CollectionChanged -= OnSourceCollectionChanged;
            _observedSource = null;
        }

        _sourceList = ItemsSource as IList ?? ItemsSource?.Cast<object>().ToList();

        if (ItemsSource is INotifyCollectionChanged observable)
        {
            _observedSource = observable;
            if (!_sourceEventsDetached)
                observable.CollectionChanged += OnSourceCollectionChanged;
        }

        var handledInPlace = UpdateDisplaySource();
        UpdateStatusRow();
        AfterDisplaySourceSynced(handledInPlace);
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is null && !_sourceEventsDetached)
        {
            _sourceEventsDetached = true;
            if (_observedSource is not null)
                _observedSource.CollectionChanged -= OnSourceCollectionChanged;
        }
    }

    /// <summary>
    /// Re-arms the parked subscription on attach and re-reads the source, whose changes while the
    /// grid was detached were never observed.
    /// </summary>
    private void ReattachSourceEvents()
    {
        if (!_sourceEventsDetached)
            return;

        _sourceEventsDetached = false;
        if (_observedSource is not null)
            _observedSource.CollectionChanged += OnSourceCollectionChanged;

        UpdateDisplaySource();
        UpdateStatusRow();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Appends and removals are applied in place, keeping the scroll position and the frozen Auto
        // widths; anything else re-renders.
        var handledInPlace = UpdateDisplaySource();
        UpdateStatusRow();
        AfterDisplaySourceSynced(handledInPlace);
    }

    private bool IsLocalSortActive =>
        SortChangedCommand is null
        && SortColumnKey is not null
        && SortDirection != DataGridSortDirection.None;

    /// <returns>True when the change was applied in place, keeping the scroll position and the realized rows.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimmingMessage)]
    private bool UpdateDisplaySource()
    {
        List<object> target = _sourceList?.Cast<object>().ToList() ?? [];

        if (IsLocalSortActive && FindColumn(SortColumnKey!) is { } column)
        {
            var descending = SortDirection == DataGridSortDirection.Descending;
            // List.Sort is not stable; the index keeps equal values in source order.
            var keyed = target.Select((item, index) => (Item: item, Value: column.GetSortValue(item), Index: index)).ToList();
            keyed.Sort((a, b) =>
            {
                var result = CompareValues(a.Value, b.Value) * (descending ? -1 : 1);
                return result != 0 ? result : a.Index.CompareTo(b.Index);
            });
            target = keyed.ConvertAll(k => k.Item);
        }

        // Grouping partitions the sorted rows, so the sort applies within each group and the
        // groups keep their own order.
        if (IsGroupingActive)
            target = BuildGroupedDisplayList(target);

        return SyncDisplayItems(target);
    }

    // ---------------------------------------------------------------- grouping

    private static readonly object UngroupedKey = new();

    private readonly Dictionary<object, DataGridGroup> _groupsByKey = [];
    private readonly Dictionary<object, int> _stripeIndexByItem = new(ReferenceEqualityComparer.Instance);
    private PropertyPathGetter? _groupKeyGetter;
    private PropertyPathGetter? _groupDisplayGetter;

    internal bool IsGroupingActive => !string.IsNullOrEmpty(GroupByPath);

    private void OnGroupingConfigChanged()
    {
        _groupKeyGetter = null;
        _groupDisplayGetter = null;
        if (!IsGroupingActive)
        {
            foreach (var group in _groupsByKey.Values)
                group.PropertyChanged -= OnGroupPropertyChanged;
            _groupsByKey.Clear();
            _stripeIndexByItem.Clear();
        }

        // The template selector has to be in place before group headers reach the list: the row
        // template would bind a group as a row, and a swipe action's typed command throws on it.
        if (IsGroupingActive != _templateIsGrouped && Handler is not null && _collectionView.ItemTemplate is not null)
        {
            // The list is emptied first, so the template swap does not rebind the rows it shows.
            _displayItems = [];
            _collectionView.ItemsSource = _displayItems;
            Rebuild();
            UpdateDisplaySource();
            return;
        }

        UpdateDisplaySource();
        QueueRebuild(); // the template selector and the sizing strategy depend on grouping
    }

    /// <summary>
    /// Flattens the sorted rows into the display list: ungrouped root rows first, then each group as
    /// [header, rows…], rows only while expanded. Groups are reused per key so their expanded state
    /// survives; groups whose rows disappeared are dropped. Groups are ordered by their text, the
    /// ungrouped group first.
    /// </summary>
    [RequiresUnreferencedCode(TrimmingMessage)]
    private List<object> BuildGroupedDisplayList(List<object> rows)
    {
        if (_groupKeyGetter is null || _groupKeyGetter.Path != GroupByPath)
            _groupKeyGetter = new PropertyPathGetter(GroupByPath!);
        if (GroupDisplayPath is { } displayPath)
        {
            if (_groupDisplayGetter is null || _groupDisplayGetter.Path != displayPath)
                _groupDisplayGetter = new PropertyPathGetter(displayPath);
        }
        else
        {
            _groupDisplayGetter = null;
        }

        var rootItems = new List<object>();
        var itemsByKey = new Dictionary<object, List<object>>();

        foreach (var row in rows)
        {
            var key = _groupKeyGetter.GetValue(row);
            if (key is null || key is string { Length: 0 })
            {
                if (UngroupedItemsMode == DataGridUngroupedMode.Root)
                {
                    rootItems.Add(row);
                    continue;
                }
                key = UngroupedKey;
            }
            if (!itemsByKey.TryGetValue(key, out var list))
                itemsByKey[key] = list = [];
            list.Add(row);
        }

        var groups = new List<DataGridGroup>(itemsByKey.Count);
        foreach (var (key, items) in itemsByKey)
        {
            if (!_groupsByKey.TryGetValue(key, out var group))
            {
                group = new DataGridGroup
                {
                    Key = ReferenceEquals(key, UngroupedKey) ? null : key,
                    IsUngrouped = ReferenceEquals(key, UngroupedKey),
                };
                group.PropertyChanged += OnGroupPropertyChanged;
                _groupsByKey[key] = group;
            }
            var displayText = group.IsUngrouped
                ? UngroupedGroupText ?? SpineStrings.Current["DataGrid.Ungrouped"]
                : _groupDisplayGetter?.GetValue(items[0])?.ToString() ?? key.ToString() ?? string.Empty;
            group.Update(displayText, items.AsReadOnly());
            groups.Add(group);
        }

        foreach (var staleKey in _groupsByKey.Keys.Where(k => !itemsByKey.ContainsKey(k)).ToList())
        {
            _groupsByKey[staleKey].PropertyChanged -= OnGroupPropertyChanged;
            _groupsByKey.Remove(staleKey);
        }

        groups = [.. groups
            .OrderBy(g => g.IsUngrouped ? 0 : 1)
            .ThenBy(g => g.DisplayText, StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true))];

        // Striping restarts per group; root rows stripe among themselves.
        _stripeIndexByItem.Clear();
        for (var i = 0; i < rootItems.Count; i++)
            _stripeIndexByItem[rootItems[i]] = i;
        foreach (var group in groups)
        {
            for (var i = 0; i < group.Items.Count; i++)
                _stripeIndexByItem[group.Items[i]] = i;
        }

        var result = new List<object>(rows.Count + groups.Count);
        result.AddRange(rootItems);
        foreach (var group in groups)
        {
            result.Add(group);
            if (group.IsExpanded)
                result.AddRange(group.Items);
        }
        return result;
    }

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataGridGroup.IsExpanded) && sender is DataGridGroup group)
            OnGroupExpandChanged(group);
    }

    /// <summary>Inserts or removes the group's rows after its header in place, keeping the scroll position.</summary>
    private void OnGroupExpandChanged(DataGridGroup group)
    {
        var headerIndex = _displayItems.IndexOf(group);
        if (headerIndex < 0)
            return;

        if (group.IsExpanded)
        {
            for (var i = 0; i < group.Items.Count; i++)
                _displayItems.Insert(headerIndex + 1 + i, group.Items[i]);
        }
        else
        {
            for (var i = 0; i < group.Items.Count && headerIndex + 1 < _displayItems.Count; i++)
                _displayItems.RemoveAt(headerIndex + 1);
        }
    }

    /// <summary>Stripe index of a row: within its group while grouping, the display index otherwise.</summary>
    internal int GetStripeIndex(object item)
    {
        if (!IsGroupingActive)
            return _displayItems.IndexOf(item);
        return _stripeIndexByItem.TryGetValue(item, out var index) ? index : 0;
    }

    /// <summary>
    /// Load more is often implemented by replacing the whole list with a superset. Rebinding the
    /// CollectionView would scroll back to the top on every page, so the grid renders a mirror and
    /// edits it in place when the new list only adds rows at the end or only removes rows. Anything
    /// else replaces the mirror (a fresh render: refresh, sort, filter).
    /// </summary>
    /// <returns>True when the change was applied in place; the Auto widths are then not measured again.</returns>
    private bool SyncDisplayItems(List<object> target)
    {
        // Grouping skips append detection: a new row lands inside a group.
        _appendedFrom = -1;
        var isAppend = !IsGroupingActive && target.Count >= _displayItems.Count && _displayItems.Count > 0;
        if (isAppend)
        {
            for (var i = 0; i < _displayItems.Count; i++)
            {
                if (!ReferenceEquals(_displayItems[i], target[i]))
                {
                    isAppend = false;
                    break;
                }
            }
        }

        if (isAppend)
        {
            _appendedFrom = _displayItems.Count;
            for (var i = _displayItems.Count; i < target.Count; i++)
                _displayItems.Add(target[i]);
            return true;
        }

        // Removals apply under grouping too: a group's last row takes its header with it, and both
        // are removals in the same order-preserving sense. Far cheaper than a new ItemsSource, which
        // reloads every row.
        if (target.Count < _displayItems.Count && TryCollectRemovals(target, out var removedIndexes))
        {
            // Highest index first so the earlier ones stay valid, and by index: Remove(item) would
            // find the first entry that compares Equal.
            for (var i = removedIndexes.Count - 1; i >= 0; i--)
                _displayItems.RemoveAt(removedIndexes[i]);

            // The surviving rows below moved up without being rebound, so their stripes are off by one.
            RefreshRowStriping();
            return true;
        }

        _displayItems = new ObservableCollection<object>(target);
        // Without a row template the platform would realise every item with the default one (a
        // bare label) and scroll and fire the load-more threshold against those; Rebuild assigns
        // the source together with the first template.
        if (_collectionView.ItemTemplate is not null)
            _collectionView.ItemsSource = _displayItems;
        return false;
    }

    /// <summary>
    /// True when <paramref name="target"/> is the display list with entries taken out and nothing
    /// added or reordered; reports the positions to drop in ascending order.
    /// </summary>
    private bool TryCollectRemovals(List<object> target, out List<int> removedIndexes)
    {
        removedIndexes = [];
        var matched = 0;
        for (var i = 0; i < _displayItems.Count; i++)
        {
            if (matched < target.Count && ReferenceEquals(_displayItems[i], target[matched]))
                matched++;
            else
                removedIndexes.Add(i);
        }
        return matched == target.Count;
    }

    private static int CompareValues(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1; // nulls last
        if (b is null) return -1;
        if (a is string sa && b is string sb)
            return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
        if (a is IComparable ca && a.GetType() == b.GetType())
            return ca.CompareTo(b);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase);
    }

    private DataGridColumn? FindColumn(string key) => Columns.FirstOrDefault(c => c.Key == key);

    // ---------------------------------------------------------------- sorting

    private bool _suppressSortReaction;

    private void OnHeaderSortTapped(DataGridColumn column)
    {
        // The tap arrives on release: a long press that opened the tooltip must not also sort.
        if (_headerLongPressFired)
        {
            _headerLongPressFired = false;
            return;
        }

        _suppressSortReaction = true;
        if (SortColumnKey != column.Key)
        {
            SortColumnKey = column.Key;
            SortDirection = DataGridSortDirection.Ascending;
        }
        else
        {
            (SortColumnKey, SortDirection) = SortDirection switch
            {
                DataGridSortDirection.Ascending => (column.Key, DataGridSortDirection.Descending),
                _ => ((string?)null, DataGridSortDirection.None),
            };
        }
        _suppressSortReaction = false;
        OnSortStateChanged();
    }

    private void OnSortStateChanged()
    {
        if (_suppressSortReaction)
            return;

        var descriptor = new DataGridSortDescriptor(SortColumnKey, SortDirection);
        if (SortChangedCommand is { } command)
        {
            if (command.CanExecute(descriptor))
                command.Execute(descriptor);
        }
        else
        {
            UpdateDisplaySource();
        }
        RebuildHeader();
        QueueRebuild(autoFitOnly: true); // the sorted column's Auto width includes the caret
    }

    /// <summary>Clears the sort order without raising <see cref="SortChangedCommand"/>.</summary>
    public void ClearSorting()
    {
        _suppressSortReaction = true;
        SortColumnKey = null;
        SortDirection = DataGridSortDirection.None;
        _suppressSortReaction = false;
        UpdateDisplaySource();
        RebuildHeader();
        QueueRebuild(autoFitOnly: true);
    }

    // ---------------------------------------------------------------- refresh / load more

    private bool _refreshHosted;

    private void UpdateRefreshEnabled()
    {
        // MAUI cascades IsEnabled down the tree, so a disabled RefreshView would make every row
        // untouchable. The list lives inside the RefreshView only while refresh is available.
        var wantRefresh = IsRefreshEnabled && RefreshCommand is not null;
        if (wantRefresh == _refreshHosted)
            return;

        _refreshHosted = wantRefresh;
        if (wantRefresh)
        {
            _root.Remove(_collectionView);
            _refreshView.Content = _collectionView;
            _root.Add(_refreshView, 0, 1);
        }
        else
        {
            _root.Remove(_refreshView);
            _refreshView.Content = new Grid(); // the RefreshView's content must stay non-null
            _root.Add(_collectionView, 0, 1);
        }
    }

    private void UpdateLoadMoreState()
    {
        var enabled = LoadMoreCommand is not null && HasMoreItems;
        _collectionView.RemainingItemsThreshold = enabled ? LoadMoreThreshold : -1;
        UpdateStatusRow();
    }

    private void OnRemainingItemsThresholdReached(object? sender, EventArgs e) => RequestLoadMore();

    private void RequestLoadMore()
    {
        if (!HasMoreItems || IsLoadingMore)
            return;
        if (LoadMoreCommand is { } command && command.CanExecute(null))
            command.Execute(null);
    }

    private void UpdateStatusRow()
    {
        var options = ResolveOptions();
        var strings = SpineStrings.Current;
        var count = _sourceList?.Count ?? 0;
        var showSpinner = IsLoadingMore;
        var moreAvailable = LoadMoreCommand is not null && HasMoreItems;

        // While more pages exist the status says so and offers a load-more link (the threshold never
        // fires on a list shorter than the screen, and a screen reader has no scroll to trigger it);
        // once everything is in, ShowLoadedStatus opts in to a count. An empty list shows neither.
        string? text = null;
        if (count > 0)
        {
            if (moreAvailable)
            {
                var format = StatusTextFormatMore ?? strings["DataGrid.Status.More"];
                text = format.Length > 0 ? string.Format(strings.Culture, format, count) : null;
            }
            else if (ShowLoadedStatus)
            {
                text = TotalItemCount >= 0
                    ? string.Format(strings.Culture, StatusTextFormatWithTotal ?? strings["DataGrid.Status.LoadedOfTotal"], count, TotalItemCount)
                    : StatusTextFormat is { } format
                        ? string.Format(strings.Culture, format, count)
                        : strings.Get("DataGrid.Status.Loaded", count);
            }
        }
        var showLoadMore = moreAvailable && count > 0 && !IsLoadingMore;

        // iOS does not reliably lay out the root grid again when the status row appears after the
        // first pass: the list keeps its height and the status lands outside the control. So on iOS
        // the band is reserved from the start whenever load more is configured, and collapses once
        // everything is loaded.
        var reserve = DeviceInfo.Platform == DevicePlatform.iOS
            && (ShowLoadedStatus || (LoadMoreCommand is not null && (HasMoreItems || count == 0)));
        var show = text is not null || showSpinner || showLoadMore;

        _statusRow.BackgroundColor = options.RowBackgroundColor;
        _statusRow.IsVisible = reserve || show;
        _root.RowDefinitions[2].Height = reserve || show ? GridLength.Auto : new GridLength(0);

        _loadMoreIndicator.IsVisible = showSpinner;
        _loadMoreIndicator.IsRunning = showSpinner;
        _loadMoreIndicator.Color = options.AccentColor;

        // The label keeps taking part in layout (empty text) so the reserved band has a stable height.
        _statusLabel.IsVisible = reserve || text is not null;
        _statusLabel.Text = text ?? string.Empty;
        _statusLabel.FontFamily = options.FontFamily;
        _statusLabel.FontSize = options.StatusFontSize;
        _statusLabel.TextColor = options.StatusTextColor;

        _loadMoreLabel.IsVisible = showLoadMore;
        _loadMoreLabel.Text = strings["DataGrid.LoadMore"];
        _loadMoreLabel.FontFamily = options.FontFamily;
        _loadMoreLabel.FontSize = options.StatusFontSize;
        _loadMoreLabel.TextColor = options.LinkColor;
    }

    // ---------------------------------------------------------------- empty / loading views

    private void UpdateEmptyView()
    {
        var options = ResolveOptions();
        _collectionView.EmptyView = IsLoading
            ? LoadingView ?? BuildDefaultLoadingView(options)
            : EmptyView ?? BuildDefaultEmptyView(options);
        _refreshView.RefreshColor = options.AccentColor;
    }

    private View BuildDefaultLoadingView(DataGridStyleOptions options)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 32),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        stack.Add(new ActivityIndicator
        {
            IsRunning = true,
            IsVisible = true, // iOS: a stopped indicator stays visible, so IsRunning always pairs with IsVisible
            HeightRequest = 40,
            WidthRequest = 40,
            Color = options.AccentColor,
            HorizontalOptions = LayoutOptions.Center,
        });
        var text = LoadingText ?? SpineStrings.Current["DataGrid.Loading"];
        if (text.Length > 0)
        {
            stack.Add(new Label
            {
                Text = text,
                FontFamily = options.FontFamily,
                FontSize = options.FontSize,
                TextColor = options.MutedTextColor,
                HorizontalOptions = LayoutOptions.Center,
            });
        }
        return stack;
    }

    private View BuildDefaultEmptyView(DataGridStyleOptions options) => new Label
    {
        Text = EmptyText ?? SpineStrings.Current["DataGrid.Empty"],
        FontFamily = options.FontFamily,
        FontSize = options.FontSize,
        TextColor = options.MutedTextColor,
        Padding = new Thickness(16, 32),
        HorizontalOptions = LayoutOptions.Center,
        HorizontalTextAlignment = TextAlignment.Center,
    };

    // ---------------------------------------------------------------- rebuild scheduling

    /// <summary>Kept from the Scrolled event so a theme rebuild can restore the position.</summary>
    private int _firstVisibleIndex;

    /// <summary>
    /// A theme or culture change: colours and texts are assigned when the views are built, so the
    /// rows, header, status row and empty view are built again.
    /// </summary>
    private void OnThemeChanged()
    {
        // A new ItemTemplate resets the list to the top; put the user back on the row they were
        // looking at, two dispatches on (the queued rebuild runs in the first). Grouped grids are
        // left alone: the flat index does not map onto the grouped list after a rebuild.
        var restoreTo = _firstVisibleIndex;
        if (IsGroupingActive)
            UpdateDisplaySource(); // the ungrouped group's title is a string
        QueueRebuild();
        UpdateStatusRow();
        UpdateEmptyView();

        if (restoreTo > 0 && !IsGroupingActive)
        {
            Dispatcher.Dispatch(() => Dispatcher.Dispatch(() =>
            {
                // The source may have been swapped meanwhile, and a ScrollTo into a list the platform
                // is still rebuilding throws inside the dispatcher, which ends the process.
                try
                {
                    if (restoreTo < _displayItems.Count)
                        _collectionView.ScrollTo(restoreTo, position: ScrollToPosition.Start, animate: false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DataGrid] scroll restore after a theme change failed: {ex}");
                }
            }));
        }
    }

    /// <param name="autoFitOnly">
    /// True for passes that only need to know whether an Auto width moved (a data change, a sort
    /// change, the grid being attached). <see cref="Rebuild"/> measures and returns without touching
    /// the ItemTemplate when nothing moved.
    /// </param>
    private void QueueRebuild(bool autoFitOnly = false)
    {
        // Coalesce the burst of changes during XAML inflation into one template build.
        if (_rebuildQueued)
        {
            // A full rebuild already queued outranks an auto-fit check.
            if (!autoFitOnly)
                _queuedRebuildIsAutoFitOnly = false;
            return;
        }
        _rebuildQueued = true;
        _queuedRebuildIsAutoFitOnly = autoFitOnly;
        Dispatcher.Dispatch(() =>
        {
            _rebuildQueued = false;

            // By now the page may have replaced the source or been torn down, and assigning an
            // ItemTemplate to a list the platform is updating throws straight out of the dispatcher.
            // A missed repaint is the whole cost of catching it.
            try
            {
                Rebuild(_queuedRebuildIsAutoFitOnly);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DataGrid] rebuild failed: {ex}");
            }
        });
    }
}
