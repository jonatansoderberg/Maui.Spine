using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>How the items of a list with <see cref="Reorder.ModeProperty"/> are picked up.</summary>
public enum ReorderMode
{
    /// <summary>No reordering. The default.</summary>
    Off,

    /// <summary>A long-press anywhere on an item lifts it; a normal drag still scrolls.</summary>
    LongPress,

    /// <summary>
    /// Only a view marked with <see cref="Reorder.IsHandleProperty"/> starts a drag, at once on touch.
    /// The rest of the item scrolls and taps as usual.
    /// </summary>
    Handle,

    /// <summary>
    /// Like <see cref="Handle"/>, but only while <see cref="Reorder.IsEditingProperty"/> is true; the
    /// handles are hidden otherwise, the way a table in edit mode shows them on demand.
    /// </summary>
    Edit,
}

/// <summary>An item a user moved in a list with <see cref="Reorder.ModeProperty"/>.</summary>
/// <param name="From">The item's index before the move.</param>
/// <param name="To">The item's index after the move.</param>
/// <param name="Item">The item that moved.</param>
public sealed record ReorderMove(int From, int To, object Item);

/// <summary>
/// Lets the user reorder the items of a <see cref="CollectionView"/> (a <c>HeroCollectionView</c>
/// too) with the platform's own drag: the item lifts, the others make room, a haptic marks the lift,
/// each new place and the drop, and a screen reader offers "Move up" and "Move down" on every item.
/// </summary>
/// <remarks>
/// <para>
/// Spine moves the item in <see cref="ItemsView.ItemsSource"/>, which must be an <see cref="IList"/>
/// that can change (an <c>ObservableCollection&lt;T&gt;</c>), and then runs
/// <see cref="CommandProperty"/> once with a <see cref="ReorderMove"/>, for saving the new order.
/// Ungrouped lists only. <see cref="ReorderableItemsView.CanReorderItems"/> is MAUI's own, simpler
/// reordering: leave it unset on a list with a mode; Spine sets it itself where it needs it.
/// </para>
/// <para>
/// iOS and Mac Catalyst use <c>UICollectionView</c>'s interactive movement, Android an
/// <c>ItemTouchHelper</c>, Windows <c>ListViewBase.CanReorderItems</c>, where the mouse drags at once
/// and <see cref="ReorderMode.Handle"/> works on the whole item. Nothing to register.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;CollectionView ItemsSource="{Binding Cards}"
///                 Reorder.Mode="Handle"
///                 Reorder.Command="{Binding SaveOrderCommand}"&gt;
///     &lt;CollectionView.ItemTemplate&gt;
///         &lt;DataTemplate&gt;
///             &lt;Grid ColumnDefinitions="*,44" Semantic.Merge="True"&gt;
///                 &lt;Label Text="{Binding Title}" /&gt;
///                 &lt;Image Grid.Column="1" SvgImageSource.Svg="griphorizontal.svg" Reorder.IsHandle="True" /&gt;
///             &lt;/Grid&gt;
///         &lt;/DataTemplate&gt;
///     &lt;/CollectionView.ItemTemplate&gt;
/// &lt;/CollectionView&gt;
/// </code>
/// </example>
public static class Reorder
{
    /// <summary>Attached property: how the list's items are picked up. <see cref="ReorderMode.Off"/> by default.</summary>
    public static readonly BindableProperty ModeProperty =
        BindableProperty.CreateAttached(
            "Mode",
            typeof(ReorderMode),
            typeof(Reorder),
            ReorderMode.Off,
            propertyChanged: OnModeChanged);

    /// <summary>
    /// Attached property: in <see cref="ReorderMode.Edit"/>, whether the list is being edited, which
    /// shows the handles and lets them drag.
    /// </summary>
    public static readonly BindableProperty IsEditingProperty =
        BindableProperty.CreateAttached(
            "IsEditing",
            typeof(bool),
            typeof(Reorder),
            false,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Update());

    /// <summary>
    /// Attached property: the command that runs with a <see cref="ReorderMove"/> after the user has
    /// moved an item. The item has moved in the list already.
    /// </summary>
    public static readonly BindableProperty CommandProperty =
        BindableProperty.CreateAttached(
            "Command",
            typeof(ICommand),
            typeof(Reorder),
            null);

    /// <summary>
    /// Attached property: marks a view in the item template as the handle that drags the item in
    /// <see cref="ReorderMode.Handle"/> and <see cref="ReorderMode.Edit"/>. Spine shows it while it
    /// can drag and hides it otherwise, so leave its <see cref="VisualElement.IsVisible"/> to Spine.
    /// </summary>
    public static readonly BindableProperty IsHandleProperty =
        BindableProperty.CreateAttached(
            "IsHandle",
            typeof(bool),
            typeof(Reorder),
            false,
            propertyChanged: OnIsHandleChanged);

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(ReorderState), typeof(Reorder), null);

    static readonly BindableProperty HandleStateProperty =
        BindableProperty.CreateAttached("HandleState", typeof(ReorderHandleState), typeof(Reorder), null);

    /// <summary>Gets how the items of <paramref name="view"/> are picked up.</summary>
    public static ReorderMode GetMode(BindableObject view) => (ReorderMode)view.GetValue(ModeProperty);

    /// <summary>Sets how the items of <paramref name="view"/> are picked up.</summary>
    public static void SetMode(BindableObject view, ReorderMode value) => view.SetValue(ModeProperty, value);

    /// <summary>Gets whether <paramref name="view"/> is being edited.</summary>
    public static bool GetIsEditing(BindableObject view) => (bool)view.GetValue(IsEditingProperty);

    /// <summary>Sets whether <paramref name="view"/> is being edited.</summary>
    public static void SetIsEditing(BindableObject view, bool value) => view.SetValue(IsEditingProperty, value);

    /// <summary>Gets the command that runs after an item of <paramref name="view"/> has moved.</summary>
    public static ICommand? GetCommand(BindableObject view) => (ICommand?)view.GetValue(CommandProperty);

    /// <summary>Sets the command that runs after an item of <paramref name="view"/> has moved.</summary>
    public static void SetCommand(BindableObject view, ICommand? value) => view.SetValue(CommandProperty, value);

    /// <summary>Gets whether <paramref name="view"/> is a drag handle.</summary>
    public static bool GetIsHandle(BindableObject view) => (bool)view.GetValue(IsHandleProperty);

    /// <summary>Sets whether <paramref name="view"/> is a drag handle.</summary>
    public static void SetIsHandle(BindableObject view, bool value) => view.SetValue(IsHandleProperty, value);

    internal static ReorderState? GetState(BindableObject view) => (ReorderState?)view.GetValue(StateProperty);

    internal static ReorderHandleState? GetHandleState(BindableObject view) => (ReorderHandleState?)view.GetValue(HandleStateProperty);

    static void OnModeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not CollectionView list)
            return;

        var state = GetState(list);

        if ((ReorderMode)newValue == ReorderMode.Off)
        {
            if (state is not null)
            {
                state.Dispose();
                list.ClearValue(StateProperty);
            }

            return;
        }

        if (state is null)
            list.SetValue(StateProperty, new ReorderState(list));
        else
            state.Update();
    }

    static void OnIsHandleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        if (view.GetValue(HandleStateProperty) is ReorderHandleState state)
        {
            state.Dispose();
            view.ClearValue(HandleStateProperty);
        }

        if ((bool)newValue)
            view.SetValue(HandleStateProperty, new ReorderHandleState(view));
    }
}

/// <summary>
/// The per-list side of <see cref="Reorder"/>: follows the list's handler, owns the platform drag,
/// and turns a finished drag into haptics, the command and fresh screen-reader actions.
/// </summary>
internal sealed partial class ReorderState : IDisposable
{
    readonly CollectionView _list;
    readonly List<WeakReference<ReorderHandleState>> _handles = [];
    INotifyCollectionChanged? _observed;
    bool _connected;
    object? _dragItem;
    int _dragFrom = -1;

    public ReorderState(CollectionView list)
    {
        _list = list;
        list.HandlerChanging += OnHandlerChanging;
        list.HandlerChanged += OnHandlerChanged;
        list.ChildAdded += OnChildAdded;
        list.PropertyChanged += OnListPropertyChanged;
        Observe();

        foreach (var child in Items)
        {
            child.BindingContextChanged += OnItemBindingContextChanged;
            AttachHandles(child);
        }

        if (list.Handler is not null)
            Connect();
    }

    public CollectionView List => _list;

    public ReorderMode Mode => Reorder.GetMode(_list);

    /// <summary>Whether a long-press on an item lifts it.</summary>
    public bool LongPressActive => Mode == ReorderMode.LongPress && CanReorder;

    /// <summary>Whether the handles show and drag.</summary>
    public bool HandlesActive =>
        (Mode == ReorderMode.Handle || Mode == ReorderMode.Edit && Reorder.GetIsEditing(_list)) && CanReorder;

    /// <summary>Whether items can move at all right now.</summary>
    public bool IsActive => LongPressActive || HandlesActive;

    bool CanReorder => !_list.IsGrouped && _list.ItemsSource is IList { IsReadOnly: false, IsFixedSize: false };

    IList? Source => _list.ItemsSource as IList;

    /// <summary>The views of the items that exist right now (the realized ones on a virtualizing list).</summary>
    IEnumerable<View> Items => ((IVisualTreeElement)_list).GetVisualChildren().OfType<View>();

    public void Update()
    {
        foreach (var handle in Handles())
            handle.Update();

        if (_connected)
            UpdatePlatform();

        RefreshAccessibility();
    }

    public void Register(ReorderHandleState handle)
    {
        _handles.RemoveAll(static h => !h.TryGetTarget(out _));
        if (!_handles.Any(h => h.TryGetTarget(out var target) && target == handle))
            _handles.Add(new(handle));
    }

    public void Unregister(ReorderHandleState handle) =>
        _handles.RemoveAll(h => !h.TryGetTarget(out var target) || target == handle);

    IEnumerable<ReorderHandleState> Handles()
    {
        foreach (var weak in _handles.ToArray())
        {
            if (weak.TryGetTarget(out var handle))
                yield return handle;
        }
    }

    /// <summary>The item at <paramref name="index"/> in the source, or null.</summary>
    public object? ItemAt(int index) =>
        Source is { } source && index >= 0 && index < source.Count ? source[index] : null;

    public int IndexOf(object? item) => item is null || Source is not { } source ? -1 : source.IndexOf(item);

    /// <summary>
    /// A drag lifted the item at <paramref name="index"/>; <paramref name="haptic"/> is false where the
    /// platform plays its own.
    /// </summary>
    public void BeginDrag(int index, bool haptic = true)
    {
        _dragItem = ItemAt(index);
        _dragFrom = index;

        if (haptic)
            Haptics.Play(Haptic.Medium);
    }

    /// <summary>The dragged item has a new place under the finger.</summary>
    public void DragStep() => Haptics.Play(Haptic.Selection);

    /// <summary>The drag has ended and the source has the item at its new place, or back at its old one.</summary>
    public void EndDrag()
    {
        var item = _dragItem;
        var from = _dragFrom;
        _dragItem = null;
        _dragFrom = -1;

        if (item is null)
            return;

        Haptics.Play(Haptic.Light);
        Completed(from, IndexOf(item), item);
    }

    /// <summary>Moves the item at <paramref name="from"/> to <paramref name="to"/> without a drag (a screen reader's action).</summary>
    public bool MoveWithoutDrag(int from, int to)
    {
        if (!IsActive || Source is not { } source || from == to
            || from < 0 || from >= source.Count || to < 0 || to >= source.Count)
            return false;

        if (source[from] is not { } item || !MovePlatform(from, to))
            return false;

        Completed(from, IndexOf(item), item);
        return true;
    }

    void Completed(int from, int to, object item)
    {
        if (to >= 0 && to != from)
        {
            var move = new ReorderMove(from, to, item);
            if (Reorder.GetCommand(_list) is { } command && command.CanExecute(move))
                command.Execute(move);
        }

        RefreshAccessibility();
    }

    /// <summary>
    /// Gives every realized item, and its handle, the screen-reader actions that fit its place:
    /// no "Move up" on the first item, no "Move down" on the last.
    /// </summary>
    public void RefreshAccessibility()
    {
        if (!_connected)
            return;

        foreach (var view in Items)
            ApplyActionsPlatform(view, ActionsFor(view));

        foreach (var handle in Handles())
            RefreshAccessibility(handle);
    }

    public void RefreshAccessibility(ReorderHandleState handle)
    {
        if (_connected && handle.ItemView is { } itemView)
            ApplyActionsPlatform(handle.View, ActionsFor(itemView));
    }

    internal IReadOnlyList<ReorderAction> ActionsFor(View itemView)
    {
        var index = IndexOf(itemView.BindingContext);
        var count = Source?.Count ?? 0;

        if (!IsActive || index < 0)
            return [];

        var strings = SpineStrings.Current;
        var actions = new List<ReorderAction>(4);

        // The item's place is looked up again when the action runs: other items may have moved since.
        void Add(string key, Func<int, int> target) =>
            actions.Add(new(strings[key], () => IndexOf(itemView.BindingContext) is var at and >= 0 && MoveWithoutDrag(at, target(at))));

        if (index > 0)
            Add("Spine.Reorder.MoveUp", static at => at - 1);
        if (index > 1)
            Add("Spine.Reorder.MoveToTop", static _ => 0);
        if (index < count - 1)
            Add("Spine.Reorder.MoveDown", static at => at + 1);
        if (index < count - 2)
            Add("Spine.Reorder.MoveToBottom", _ => (Source?.Count ?? 0) - 1);

        return actions;
    }

    public void Dispose()
    {
        Disconnect();
        _list.HandlerChanging -= OnHandlerChanging;
        _list.HandlerChanged -= OnHandlerChanged;
        _list.ChildAdded -= OnChildAdded;
        _list.PropertyChanged -= OnListPropertyChanged;

        if (_observed is not null)
            _observed.CollectionChanged -= OnSourceChanged;

        foreach (var child in Items)
            child.BindingContextChanged -= OnItemBindingContextChanged;

        foreach (var handle in Handles())
            handle.Detach();

        _handles.Clear();
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            Disconnect();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_list.Handler is not null)
            Connect();
    }

    // The template builds an item's handles before the item joins the list, so the list hands
    // itself to them when the item arrives.
    void AttachHandles(View item)
    {
        foreach (var element in Descendants(item))
        {
            if (Reorder.GetHandleState(element) is { } handle)
                handle.Attach(this);
        }

        static IEnumerable<BindableObject> Descendants(IVisualTreeElement element)
        {
            if (element is BindableObject bindable)
                yield return bindable;

            foreach (var child in element.GetVisualChildren())
            {
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }

    void OnListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == ItemsView.ItemsSourceProperty.PropertyName)
            Observe();

        if (e.PropertyName == ItemsView.ItemsSourceProperty.PropertyName
            || e.PropertyName == GroupableItemsView.IsGroupedProperty.PropertyName)
            Update();
    }

    void Observe()
    {
        if (_observed is not null)
            _observed.CollectionChanged -= OnSourceChanged;

        _observed = _list.ItemsSource as INotifyCollectionChanged;

        if (_observed is not null)
            _observed.CollectionChanged += OnSourceChanged;
    }

    // An item added or removed by the app changes which items are first and last. The list
    // updates its cells after this event, so the actions follow on the next turn of the loop.
    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_dragItem is null)
            _list.Dispatcher.Dispatch(RefreshAccessibility);
    }

    void OnChildAdded(object? sender, ElementEventArgs e)
    {
        if (e.Element is not View view)
            return;

        view.BindingContextChanged -= OnItemBindingContextChanged;
        view.BindingContextChanged += OnItemBindingContextChanged;
        AttachHandles(view);

        if (_connected)
            ApplyActionsPlatform(view, ActionsFor(view));
    }

    // A recycled cell's view shows another item, at another place.
    void OnItemBindingContextChanged(object? sender, EventArgs e)
    {
        if (!_connected || sender is not View view)
            return;

        ApplyActionsPlatform(view, ActionsFor(view));

        foreach (var handle in Handles())
        {
            if (handle.ItemView == view)
                RefreshAccessibility(handle);
        }
    }

    void Connect()
    {
        if (_connected || _list.Handler?.PlatformView is not { } platformView)
            return;

        _connected = true;
        ConnectPlatform(platformView);
        UpdatePlatform();
        RefreshAccessibility();
    }

    void Disconnect()
    {
        if (!_connected)
            return;

        foreach (var view in Items)
            ApplyActionsPlatform(view, []);

        DisconnectPlatform();
        _connected = false;
        _dragItem = null;
        _dragFrom = -1;
    }

    partial void ConnectPlatform(object platformView);

    partial void DisconnectPlatform();

    partial void UpdatePlatform();

    partial void ApplyActionsPlatform(View view, IReadOnlyList<ReorderAction> actions);

#if !(IOS || MACCATALYST || ANDROID)
    bool MovePlatform(int from, int to) => false;
#endif
}

/// <summary>A screen-reader action on a reorderable item: its name and what it does.</summary>
internal sealed record ReorderAction(string Name, Func<bool> Perform);

/// <summary>
/// The per-handle side of <see cref="Reorder"/>: finds the handle's list once the handle is in
/// one, shows it while it can drag and starts the platform drag on touch.
/// </summary>
internal sealed partial class ReorderHandleState : IDisposable
{
    readonly View _view;
    ReorderState? _owner;
    bool _connected;

    public ReorderHandleState(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;

        if (view.Handler is not null)
            Connect();

        if (ItemView?.Parent is CollectionView list && Reorder.GetState(list) is { } owner)
            Attach(owner);
    }

    public View View => _view;

    /// <summary>The list this handle drags in, once it is in one with a mode.</summary>
    public ReorderState? Owner => _owner;

    /// <summary>The item's root view in the list: the handle's ancestor whose parent is the list.</summary>
    public View? ItemView
    {
        get
        {
            Element? element = _view;
            while (element is not null && element.Parent is not ItemsView)
                element = element.Parent;
            return element as View;
        }
    }

    public bool CanDrag => Owner?.HandlesActive == true;

    public void Update()
    {
        if (_owner is null)
            return;

        _view.IsVisible = _owner.HandlesActive;

        if (_connected)
            UpdatePlatform();
    }

    /// <summary>The list went away or turned reordering off: a handle with nothing to drag hides.</summary>
    public void Detach()
    {
        _owner = null;
        _view.IsVisible = false;
        if (_connected)
            UpdatePlatform();
    }

    public void Dispose()
    {
        Disconnect();
        _owner?.Unregister(this);
        _owner = null;
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
    }

    public void Attach(ReorderState owner)
    {
        if (_owner == owner)
            return;

        _owner?.Unregister(this);
        _owner = owner;
        owner.Register(this);
        Update();
        owner.RefreshAccessibility(this);
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            Disconnect();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_view.Handler is not null)
            Connect();
    }

    void Connect()
    {
        if (_connected || _view.Handler?.PlatformView is not { } platformView)
            return;

        _connected = true;
        ConnectPlatform(platformView);
        UpdatePlatform();
        _owner?.RefreshAccessibility(this);
    }

    void Disconnect()
    {
        if (!_connected)
            return;

        DisconnectPlatform();
        _connected = false;
    }

    partial void ConnectPlatform(object platformView);

    partial void DisconnectPlatform();

    partial void UpdatePlatform();
}
