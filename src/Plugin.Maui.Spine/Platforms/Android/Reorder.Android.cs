using System.Runtime.CompilerServices;
using Android.Animation;
using Android.Views;
using AndroidX.Core.View;
using AndroidX.Core.View.Accessibility;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls.Handlers.Items;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ReorderState
{
    const float LiftScale = 1.03f;
    const long LiftDuration = 150;

    // MAUI's ItemViewType is internal: header, footer, group header, group footer.
    static readonly int[] StructuralViewTypes = [43, 44, 45, 46];

    static readonly ConditionalWeakTable<AView, List<int>> ActionIds = [];

    RecyclerView? _recyclerView;
    ItemTouchHelper? _helper;
    DragCallback? _callback;
    RecyclerView.ViewHolder? _dragged;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not RecyclerView recyclerView)
            return;

        _recyclerView = recyclerView;
        _callback = new DragCallback(this);
        _helper = new ItemTouchHelper(_callback);
        _helper.AttachToRecyclerView(recyclerView);
    }

    partial void DisconnectPlatform()
    {
        _helper?.AttachToRecyclerView(null);
        _helper?.Dispose();
        _helper = null;
        _callback?.Dispose();
        _callback = null;
        _recyclerView = null;
        _dragged = null;
    }

    partial void UpdatePlatform()
    {
    }

    /// <summary>A handle was touched: lift its item at once.</summary>
    internal void StartDrag(AView handle)
    {
        if (!HandlesActive || _recyclerView?.FindContainingViewHolder(handle) is not { } holder)
            return;

        _helper?.StartDrag(holder);
    }

    // The source's index of an adapter position: a header takes the first position.
    int SourceIndex(int position) => position - HeaderOffset;

    int HeaderOffset => _list.Header is not null || _list.HeaderTemplate is not null ? 1 : 0;

    bool MovePlatform(int from, int to)
    {
        if (_recyclerView?.GetAdapter() is not IItemTouchHelperAdapter adapter)
            return false;

        // MAUI's adapter moves the item in the source without echoing it back and animates the move.
        return adapter.OnItemMove(from + HeaderOffset, to + HeaderOffset);
    }

    partial void ApplyActionsPlatform(Microsoft.Maui.Controls.View view, IReadOnlyList<ReorderAction> actions)
    {
        if (view.Handler?.PlatformView is not AView platformView)
            return;

        var ids = ActionIds.GetOrCreateValue(platformView);
        foreach (var id in ids)
            ViewCompat.RemoveAccessibilityAction(platformView, id);
        ids.Clear();

        foreach (var action in actions)
            ids.Add(ViewCompat.AddAccessibilityAction(platformView, new Java.Lang.String(action.Name), new ActionCommand(action)));
    }

    void OnLift(RecyclerView.ViewHolder holder)
    {
        _dragged = holder;
        // ItemTouchHelper plays the long-press haptic itself when it lifts an item.
        BeginDrag(SourceIndex(holder.BindingAdapterPosition), haptic: false);
        Animate(holder.ItemView, LiftScale);
    }

    void OnDrop(RecyclerView.ViewHolder holder)
    {
        if (_dragged != holder)
            return;

        _dragged = null;
        Animate(holder.ItemView, 1f);
        EndDrag();
    }

    static void Animate(AView view, float scale)
    {
        // Animator duration scale 0 is Android's "Remove animations".
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !ValueAnimator.AreAnimatorsEnabled())
            return;

        view.Animate()?.ScaleX(scale).ScaleY(scale).SetDuration(LiftDuration).Start();
    }

    sealed class DragCallback(ReorderState owner) : ItemTouchHelper.Callback
    {
        readonly WeakReference<ReorderState> _owner = new(owner);

        ReorderState? Owner => _owner.TryGetTarget(out var target) ? target : null;

        public override bool IsLongPressDragEnabled => Owner?.LongPressActive == true;

        public override bool IsItemViewSwipeEnabled => false;

        public override int GetMovementFlags(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            if (Owner is not { IsActive: true } owner || StructuralViewTypes.Contains(viewHolder.ItemViewType))
                return MakeMovementFlags(0, 0);

            var flags = owner._list.ItemsLayout switch
            {
                LinearItemsLayout { Orientation: ItemsLayoutOrientation.Vertical } => ItemTouchHelper.Up | ItemTouchHelper.Down,
                LinearItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal } => ItemTouchHelper.Left | ItemTouchHelper.Right,
                _ => ItemTouchHelper.Up | ItemTouchHelper.Down | ItemTouchHelper.Left | ItemTouchHelper.Right,
            };

            return MakeMovementFlags(flags, 0);
        }

        public override bool CanDropOver(RecyclerView recyclerView, RecyclerView.ViewHolder current, RecyclerView.ViewHolder target) =>
            !StructuralViewTypes.Contains(target.ItemViewType);

        public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, RecyclerView.ViewHolder target)
        {
            if (Owner is not { } owner || recyclerView.GetAdapter() is not IItemTouchHelperAdapter adapter)
                return false;

            // The source follows the finger: ItemTouchHelper expects the adapter to have moved.
            if (!adapter.OnItemMove(viewHolder.BindingAdapterPosition, target.BindingAdapterPosition))
                return false;

            owner.DragStep();
            return true;
        }

        public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
        {
        }

        public override void OnSelectedChanged(RecyclerView.ViewHolder? viewHolder, int actionState)
        {
            base.OnSelectedChanged(viewHolder, actionState);

            if (actionState == ItemTouchHelper.ActionStateDrag && viewHolder is not null)
                Owner?.OnLift(viewHolder);
        }

        public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            base.ClearView(recyclerView, viewHolder);
            Owner?.OnDrop(viewHolder);
        }
    }

    sealed class ActionCommand(ReorderAction action) : Java.Lang.Object, IAccessibilityViewCommand
    {
        public bool Perform(AView? view, AccessibilityViewCommandCommandArguments? arguments) => action.Perform();
    }
}

internal sealed partial class ReorderHandleState
{
    AView? _host;
    string? _previousDescription;
    bool _labelled;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not AView host)
            return;

        _host = host;
        host.Touch += OnTouch;
    }

    partial void DisconnectPlatform()
    {
        if (_host is not null)
            _host.Touch -= OnTouch;

        _host = null;
    }

    partial void UpdatePlatform()
    {
        if (_host is null)
            return;

        // A grip glyph says nothing to TalkBack; name it, unless the app already has.
        if (string.IsNullOrEmpty(SemanticProperties.GetDescription(View)) && CanDrag)
        {
            if (!_labelled)
                _previousDescription = _host.ContentDescription;

            _host.ContentDescription = Common.SpineStrings.Current["Spine.Reorder.Handle"];
            _host.ImportantForAccessibility = ImportantForAccessibility.Yes;
            _labelled = true;
        }
        else if (_labelled && !CanDrag)
        {
            _host.ContentDescription = _previousDescription;
            _host.ImportantForAccessibility = ImportantForAccessibility.Auto;
            _labelled = false;
        }
    }

    void OnTouch(object? sender, AView.TouchEventArgs e)
    {
        // Lifts on touch down and leaves the rest of the gesture to ItemTouchHelper, which takes
        // over the list's touches once the drag has started.
        e.Handled = false;

        if (e.Event?.ActionMasked == MotionEventActions.Down && CanDrag && _host is { } host)
        {
            Owner?.StartDrag(host);
            e.Handled = true;
        }
    }
}
