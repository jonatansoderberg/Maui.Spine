using System.Collections;
using System.Collections.Specialized;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// <see cref="Reorder"/> on a bindable layout (<c>BindableLayout.ItemsSource</c> on a FlexLayout, a Grid, a stack or an
/// app's own layout): a long-press lifts an item, it follows the pointer, the other items slide aside and the item's
/// place shows as an empty dashed frame. The item views stay where they are in the tree during the drag (only the
/// layout's children are reordered, so a template's view is never rebuilt under the pointer); the source moves once,
/// on the drop, and the command runs with the move.
/// </summary>
internal sealed partial class LayoutReorderState : IDisposable
{
    const double LiftScale = 1.03;
    const uint SlideMs = 220, SettleMs = 180;

    readonly Layout _layout;
    INotifyCollectionChanged? _observed;
    View? _dragged;
    int _from = -1;
    int _zIndex;
    Point _grab;

    public LayoutReorderState(Layout layout)
    {
        _layout = layout;
        layout.HandlerChanging += OnHandlerChanging;
        layout.HandlerChanged += OnHandlerChanged;
        layout.ChildAdded += OnChildrenChanged;
        layout.PropertyChanged += OnLayoutPropertyChanged;
        Observe();
        if (layout.Handler?.PlatformView is { } platformView)
            ConnectPlatform(platformView);
    }

    IList? Source => BindableLayout.GetItemsSource(_layout) as IList;

    IEnumerable<View> Items => _layout.Children.OfType<View>();

    /// <summary>Whether an item can be lifted now: on, enabled, a list that can change, one view per item.</summary>
    bool CanReorder => Reorder.GetMode(_layout) != ReorderMode.Off && Reorder.GetIsEnabled(_layout)
        && Source is IList { IsReadOnly: false, IsFixedSize: false } source && source.Count == _layout.Children.Count;

    public void Update() => RefreshAccessibility();

    /// <summary>A long-press at <paramref name="point"/> (the layout's coordinates) lifts the item there.</summary>
    bool Begin(Point point)
    {
        if (_dragged is not null || !CanReorder || Items.FirstOrDefault(v => v.IsVisible && v.Frame.Contains(point)) is not { } view)
            return false;

        _dragged = view;
        _from = _layout.Children.IndexOf(view);
        Reorder.SetIsDragging(_layout, true);
        _grab = new Point(point.X - view.Frame.X, point.Y - view.Frame.Y);
        // Over the other items for the whole drag: a z-index above theirs, and on the platform the view raised too
        // (the children are reordered under the pointer, which puts the view back among them).
        _zIndex = view.ZIndex;
        view.ZIndex = Items.Max(v => v.ZIndex) + 1;
        RaisePlatform(view);
        _ = view.ScaleToAsync(LiftScale, 150, Easing.CubicOut);
        ShowPlaceholderPlatform(view.Frame, CornerRadius(view));
        Haptics.Play(Haptic.Medium);
        return true;
    }

    /// <summary>
    /// The pointer moved: over another item, the dragged one takes its place among the layout's children and the others
    /// slide to theirs; the dragged one follows the pointer either way.
    /// </summary>
    void Drag(Point point)
    {
        if (_dragged is not { } view)
            return;
        // The app rebuilt its items under the pointer.
        if (!_layout.Children.Contains(view))
        {
            _ = EndAsync(cancelled: true);
            return;
        }

        if (Items.FirstOrDefault(v => v != view && v.IsVisible && v.Frame.Contains(point)) is { } target)
        {
            var before = Items.ToDictionary(v => v, v => v.Frame);
            var to = _layout.Children.IndexOf(target);
            _layout.Children.Remove(view);
            _layout.Children.Insert(to, view);
            Relayout();

            foreach (var (item, was) in before)
            {
                if (item == view || was.Location == item.Frame.Location)
                    continue;
                item.CancelAnimations();
                item.TranslationX += was.X - item.Frame.X;
                item.TranslationY += was.Y - item.Frame.Y;
                _ = item.TranslateToAsync(0, 0, SlideMs, Easing.CubicOut);
            }

            MovePlaceholderPlatform(view.Frame);
            RaisePlatform(view);
            Haptics.Play(Haptic.Selection);
        }

        view.TranslationX = point.X - _grab.X - view.Frame.X;
        view.TranslationY = point.Y - _grab.Y - view.Frame.Y;
    }

    /// <summary>
    /// The item settles in its place. The children go back to the source's order and the source moves in the same turn,
    /// so the layout rebuilds the item at the place it was dropped on without a frame in between.
    /// </summary>
    async Task EndAsync(bool cancelled)
    {
        if (_dragged is not { } view)
            return;

        _dragged = null;
        var from = _from;
        _from = -1;
        var to = _layout.Children.IndexOf(view);
        HidePlaceholderPlatform();
        Reorder.SetIsDragging(_layout, false);

        // The items were rebuilt during the drag: nothing to settle or move.
        if (to < 0)
        {
            view.TranslationX = view.TranslationY = 0;
            view.Scale = 1;
            view.ZIndex = _zIndex;
            LowerPlatform(view);
            return;
        }

        if (cancelled && to != from)
        {
            _layout.Children.Remove(view);
            _layout.Children.Insert(from, view);
            Relayout();
            to = from;
        }

        await Task.WhenAll(view.TranslateToAsync(0, 0, SettleMs, Easing.CubicOut), view.ScaleToAsync(1, SettleMs, Easing.CubicOut));
        view.ZIndex = _zIndex;
        LowerPlatform(view);

        if (to == from || Source is not { } source || from < 0 || from >= source.Count)
            return;

        _layout.Children.Remove(view);
        _layout.Children.Insert(from, view);
        var item = source[from]!;
        MoveSource(source, from, to);
        Haptics.Play(Haptic.Light);
        Completed(from, to, item);
    }

    void Completed(int from, int to, object item)
    {
        var move = new ReorderMove(from, to, item);
        if (Reorder.GetCommand(_layout) is { } command && command.CanExecute(move))
            command.Execute(move);
        RefreshAccessibility();
    }

    // An ObservableCollection moves the item as one change; any other list takes it out and puts it back.
    static void MoveSource(IList source, int from, int to)
    {
        if (source.GetType().GetMethod("Move", [typeof(int), typeof(int)]) is { } move)
        {
            move.Invoke(source, [from, to]);
            return;
        }

        var item = source[from];
        source.RemoveAt(from);
        source.Insert(to, item);
    }

    // Lays the children out now, so their new frames are known in this turn. A layout arranges its children in the
    // platform's layout pass, not in Arrange, so this calls the cross-platform pass itself.
    void Relayout()
    {
        var frame = _layout.Frame;
        var layout = (Microsoft.Maui.ILayout)_layout;
        layout.CrossPlatformMeasure(frame.Width, frame.Height);
        layout.CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
    }

    // The placeholder has the item's corners: a Border's rounded rectangle, or a small radius.
    static double CornerRadius(View view) =>
        view is Border { StrokeShape: Microsoft.Maui.Controls.Shapes.RoundRectangle shape } ? shape.CornerRadius.TopLeft : 12;

    /// <summary>The screen-reader actions on every item: no "Move up" on the first, no "Move down" on the last.</summary>
    void RefreshAccessibility()
    {
        var views = Items.ToList();
        for (var i = 0; i < views.Count; i++)
            ApplyActionsPlatform(views[i], ActionsFor(i, views.Count));
    }

    IReadOnlyList<ReorderAction> ActionsFor(int index, int count)
    {
        if (!CanReorder)
            return [];

        var strings = SpineStrings.Current;
        var actions = new List<ReorderAction>(4);
        void Add(string key, int target) => actions.Add(new(strings[key], () => MoveWithoutDrag(index, target)));

        if (index > 0) Add("Spine.Reorder.MoveUp", index - 1);
        if (index > 1) Add("Spine.Reorder.MoveToTop", 0);
        if (index < count - 1) Add("Spine.Reorder.MoveDown", index + 1);
        if (index < count - 2) Add("Spine.Reorder.MoveToBottom", count - 1);
        return actions;
    }

    bool MoveWithoutDrag(int from, int to)
    {
        if (!CanReorder || Source is not { } source || from == to || from < 0 || to < 0 || from >= source.Count || to >= source.Count)
            return false;
        var item = source[from]!;
        MoveSource(source, from, to);
        Completed(from, to, item);
        return true;
    }

    void Observe()
    {
        if (_observed is not null)
            _observed.CollectionChanged -= OnSourceChanged;
        _observed = BindableLayout.GetItemsSource(_layout) as INotifyCollectionChanged;
        if (_observed is not null)
            _observed.CollectionChanged += OnSourceChanged;
    }

    // The layout builds the item views after this event; the actions follow on the next turn.
    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => _layout.Dispatcher.Dispatch(RefreshAccessibility);

    void OnChildrenChanged(object? sender, ElementEventArgs e)
    {
        if (_dragged is null)
            _layout.Dispatcher.Dispatch(RefreshAccessibility);
    }

    void OnLayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == BindableLayout.ItemsSourceProperty.PropertyName)
        {
            Observe();
            RefreshAccessibility();
        }
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            DisconnectPlatform();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_layout.Handler?.PlatformView is { } platformView)
            ConnectPlatform(platformView);
    }

    public void Dispose()
    {
        DisconnectPlatform();
        _layout.HandlerChanging -= OnHandlerChanging;
        _layout.HandlerChanged -= OnHandlerChanged;
        _layout.ChildAdded -= OnChildrenChanged;
        _layout.PropertyChanged -= OnLayoutPropertyChanged;
        if (_observed is not null)
            _observed.CollectionChanged -= OnSourceChanged;
    }

    partial void ConnectPlatform(object platformView);
    partial void DisconnectPlatform();
    partial void ShowPlaceholderPlatform(Rect frame, double cornerRadius);
    partial void MovePlaceholderPlatform(Rect frame);
    partial void HidePlaceholderPlatform();
    partial void RaisePlatform(View view);
    partial void LowerPlatform(View view);
    partial void ApplyActionsPlatform(View view, IReadOnlyList<ReorderAction> actions);
}
