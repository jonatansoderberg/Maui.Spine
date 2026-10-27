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
    // A pointer press lifts the item only once it has moved this far, so a click on it doesn't make it grow.
    const double LiftDistance = 4;
    const uint SlideMs = 220, SettleMs = 180;

    readonly Layout _layout;
    INotifyCollectionChanged? _observed;
    View? _dragged;
    int _from = -1;
    int _zIndex;
    Point _grab, _pressed;
    bool _lifted;
    // The platform draws the lifted item itself (a drag session's preview on iOS): the view stays in its place, hidden,
    // and only the placeholder and the other items move.
    bool _systemPreview;

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

    /// <summary>
    /// A long-press at <paramref name="point"/> (the layout's coordinates) takes the item there. A finger's lifts it at
    /// once; a pointer's (<paramref name="liftOnMove"/>) lifts it when it starts to move, as a drag on the desktop does.
    /// </summary>
    bool Begin(Point point, bool liftOnMove = false) =>
        Items.FirstOrDefault(v => v.IsVisible && v.Frame.Contains(point)) is { } view && Take(view, point, liftOnMove, systemPreview: false);

    /// <summary>Takes <paramref name="view"/>, pressed at <paramref name="point"/>, for a drag.</summary>
    bool Take(View view, Point point, bool liftOnMove, bool systemPreview)
    {
        if (_dragged is not null || !CanReorder || !_layout.Children.Contains(view))
            return false;

        _dragged = view;
        _systemPreview = systemPreview;
        _from = _layout.Children.IndexOf(view);
        _grab = new Point(point.X - view.Frame.X, point.Y - view.Frame.Y);
        _pressed = point;
        _lifted = false;
        if (!liftOnMove)
            Lift(view);
        return true;
    }

    void Lift(View view)
    {
        _lifted = true;
        Reorder.SetIsDragging(_layout, true);
        if (_systemPreview)
        {
            ShowPlaceholderPlatform(view.Frame, CornerRadius(view));
            HideItemPlatform(view);
            return;
        }
        // Over the other items for the whole drag: a z-index above theirs, and on the platform the view raised too
        // (the children are reordered under the pointer, which puts the view back among them).
        _zIndex = view.ZIndex;
        view.ZIndex = Items.Max(v => v.ZIndex) + 1;
        RaisePlatform(view);
        _ = view.ScaleToAsync(LiftScale, 150, Easing.CubicOut);
        ShowPlaceholderPlatform(view.Frame, CornerRadius(view));
        Haptics.Play(Haptic.Medium);
    }

    /// <summary>
    /// The pointer moved: over another item, the dragged one takes its place among the layout's children and the others
    /// slide to theirs; the dragged one follows the pointer either way.
    /// </summary>
    void Drag(Point point)
    {
        if (_dragged is not { } view)
            return;
        if (!_lifted)
        {
            if (Math.Abs(point.X - _pressed.X) < LiftDistance && Math.Abs(point.Y - _pressed.Y) < LiftDistance)
                return;
            Lift(view);
        }
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
                // The platform slides it with a spring that adds to any slide still running (Apple); elsewhere a
                // translation eased back to zero.
                var slid = false;
                SlidePlatform(item, was.X - item.Frame.X, was.Y - item.Frame.Y, ref slid);
                if (slid)
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

        if (_systemPreview)
            return;
        view.TranslationX = point.X - _grab.X - view.Frame.X;
        view.TranslationY = point.Y - _grab.Y - view.Frame.Y;
    }

    /// <summary>
    /// Puts the item back at the place it was taken from, before a cancelled drag animates there; its frame is where that
    /// is.
    /// </summary>
    Rect? RestoreOrder()
    {
        if (_dragged is not { } view || _from < 0 || !_layout.Children.Contains(view))
            return null;
        if (_layout.Children.IndexOf(view) != _from)
        {
            _layout.Children.Remove(view);
            _layout.Children.Insert(Math.Min(_from, _layout.Children.Count), view);
            Relayout();
            MovePlaceholderPlatform(view.Frame);
        }
        return view.Frame;
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
        // Pressed and let go without moving: it was never lifted.
        if (!_lifted)
            return;
        _lifted = false;
        var to = _layout.Children.IndexOf(view);
        HidePlaceholderPlatform();
        // IsDragging stays true until the source has moved: an app that rebuilds its items when it turns false would
        // otherwise rebuild them under the settling item, and the item would be put back among the new ones.
        try
        {
            await FinishAsync(view, from, to, cancelled);
        }
        finally
        {
            Reorder.SetIsDragging(_layout, false);
        }
    }

    async Task FinishAsync(View view, int from, int to, bool cancelled)
    {
        var systemPreview = _systemPreview;
        _systemPreview = false;
        // The platform's preview has landed where the view is: show the view in its place.
        if (systemPreview)
            ShowItemPlatform(view);

        // The items were rebuilt during the drag: nothing to settle or move.
        if (to < 0)
        {
            if (!systemPreview)
            {
                view.TranslationX = view.TranslationY = 0;
                view.Scale = 1;
                view.ZIndex = _zIndex;
                LowerPlatform(view);
            }
            return;
        }

        if (cancelled && to != from)
        {
            _layout.Children.Remove(view);
            _layout.Children.Insert(from, view);
            Relayout();
            to = from;
        }

        if (!systemPreview)
        {
            Task? settled = null;
            SettlePlatform(view, ref settled);
            await (settled ?? Task.WhenAll(view.TranslateToAsync(0, 0, SettleMs, Easing.CubicOut), view.ScaleToAsync(1, SettleMs, Easing.CubicOut)));
            view.ZIndex = _zIndex;
            LowerPlatform(view);
        }

        // Rebuilt while it settled: the new items are the app's, nothing to move.
        if (to == from || !_layout.Children.Contains(view) || Source is not { } source || from < 0 || from >= source.Count)
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
        AttachItemsPlatform();
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
    partial void HideItemPlatform(View view);
    partial void ShowItemPlatform(View view);
    partial void AttachItemsPlatform();
    partial void RaisePlatform(View view);
    partial void SlidePlatform(View item, double fromX, double fromY, ref bool slid);
    partial void SettlePlatform(View view, ref Task? settled);
    partial void LowerPlatform(View view);
    partial void ApplyActionsPlatform(View view, IReadOnlyList<ReorderAction> actions);
}
