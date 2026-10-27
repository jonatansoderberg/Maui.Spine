#if IOS || MACCATALYST

using CoreAnimation;
using Foundation;
using CoreGraphics;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class LayoutReorderState
{
    UIView? _host;
    CAShapeLayer? _placeholder;
#if MACCATALYST
    UILongPressGestureRecognizer? _press;
#else
    // A finger drags with the system's drag and drop: each item has a drag interaction and the layout a drop interaction.
    // On the item's own view, next to a context menu's interaction, UIKit hands a long-press over from the menu to the
    // drag once the finger moves, and a scroll still wins over both until the press has been held.
    UIDropInteraction? _drop;
    // The delegates are held here: an interaction keeps its delegate weakly.
    readonly List<(WeakReference<View> Item, UIView Host, UIDragInteraction Interaction, DragDelegate Delegate)> _drags = [];
    DropDelegate? _dropDelegate;
    readonly NSObject _token = new();
#endif

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView view || _host is not null)
            return;

        _host = view;
#if MACCATALYST
        _press = new UILongPressGestureRecognizer(Drive)
        {
            ShouldBegin = _ => CanReorder,
            // A pointer press need not be held as long as a finger, and it may move a little while it is held.
            MinimumPressDuration = 0.1,
            AllowableMovement = 20,
        };
        view.AddGestureRecognizer(_press);
#else
        _dropDelegate = new DropDelegate(this);
        _drop = new UIDropInteraction(_dropDelegate);
        view.AddInteraction(_drop);
        AttachItemsPlatform();
#endif
    }

    partial void DisconnectPlatform()
    {
        if (_dragged is not null)
            _ = EndAsync(cancelled: true);
#if MACCATALYST
        if (_press is not null)
        {
            _host?.RemoveGestureRecognizer(_press);
            _press.Dispose();
            _press = null;
        }
#else
        if (_drop is not null)
            _host?.RemoveInteraction(_drop);
        _drop = null;
        _dropDelegate = null;
        foreach (var (_, host, interaction, _) in _drags)
            host.RemoveInteraction(interaction);
        _drags.Clear();
#endif
        HidePlaceholderPlatform();
        _host = null;
    }

#if MACCATALYST
    void Drive(UILongPressGestureRecognizer recognizer)
    {
        if (_host is null)
            return;
        var location = recognizer.LocationInView(_host);
        var point = new Point(location.X, location.Y);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                if (!Begin(point, liftOnMove: true))
                {
                    recognizer.Enabled = false;
                    recognizer.Enabled = true;
                }
                break;
            case UIGestureRecognizerState.Changed:
                Drag(point);
                break;
            case UIGestureRecognizerState.Ended:
                _ = EndAsync(cancelled: false);
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed:
                _ = EndAsync(cancelled: true);
                break;
        }
    }
#else
    // Every item's own view gets a drag interaction (the view a context menu also sits on); items rebuilt by the app get
    // theirs as they come.
    partial void AttachItemsPlatform()
    {
        if (_host is null)
            return;
        var items = Items.ToList();
        for (var i = _drags.Count - 1; i >= 0; i--)
        {
            var (item, host, interaction, _) = _drags[i];
            if (item.TryGetTarget(out var view) && items.Contains(view) && view.Handler?.PlatformView == host)
                continue;
            host.RemoveInteraction(interaction);
            _drags.RemoveAt(i);
        }
        foreach (var view in items)
        {
            // Not on screen yet: it gets its interaction once it has a view.
            if (view.Handler?.PlatformView is not UIView host)
            {
                view.HandlerChanged -= OnItemHandlerChanged;
                view.HandlerChanged += OnItemHandlerChanged;
                continue;
            }
            if (_drags.Any(d => d.Host == host))
                continue;
            host.UserInteractionEnabled = true;
            var dragDelegate = new DragDelegate(this, view);
            var interaction = new UIDragInteraction(dragDelegate) { Enabled = true };
            host.AddInteraction(interaction);
            _drags.Add((new WeakReference<View>(view), host, interaction, dragDelegate));
        }
    }

    void OnItemHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view)
            view.HandlerChanged -= OnItemHandlerChanged;
        AttachItemsPlatform();
    }

    Point Location(IUIDragDropSession session) => session.LocationInView(_host!) is var p ? new Point(p.X, p.Y) : default;

    // The lifted item as the system shows it: the view with its own corners and background.
    static UITargetedDragPreview? Preview(View view)
    {
        if (view.Handler?.PlatformView is not UIView { Window: not null } platformView)
            return null;
        return new UITargetedDragPreview(platformView, Parameters(view, platformView));
    }

    // The view drawn into an image now (a snapshot view can come back blank for a view the system has not composited).
    static UIImageView? Snapshot(UIView view)
    {
        if (view.Bounds.IsEmpty)
            return null;
        var image = new UIGraphicsImageRenderer(view.Bounds.Size).CreateImage(_ => view.DrawViewHierarchy(view.Bounds, true));
        return new UIImageView(image) { Frame = view.Bounds };
    }

    static UIDragPreviewParameters Parameters(View view, UIView platformView) => new()
    {
        VisiblePath = UIBezierPath.FromRoundedRect(platformView.Bounds, (nfloat)CornerRadius(view)),
        BackgroundColor = view.BackgroundColor?.ToPlatform() ?? UIColor.SystemBackground,
    };

    // While the system draws the lifted item, the view itself stays behind its placeholder, hidden.
    partial void HideItemPlatform(View view)
    {
        if (Outermost(view) is { } platformView)
            platformView.Alpha = 0;
    }

    partial void ShowItemPlatform(View view)
    {
        if (Outermost(view) is { } platformView)
            platformView.Alpha = 1;
    }

    // The preview lands on the view in its place, shown again for it: the system animates into the view as it is.
    UITargetedDragPreview? Landing(View view)
    {
        ShowItemPlatform(view);
        return Preview(view);
    }

    sealed class DragDelegate(LayoutReorderState owner, View item) : UIDragInteractionDelegate
    {
        readonly WeakReference<LayoutReorderState> _owner = new(owner);
        readonly WeakReference<View> _item = new(item);

        LayoutReorderState? Owner => _owner.TryGetTarget(out var o) && o._host is not null ? o : null;
        View? Item => _item.TryGetTarget(out var v) ? v : null;

        public override UIDragItem[] GetItemsForBeginningSession(UIDragInteraction interaction, IUIDragSession session)
        {
            if (Owner is not { CanReorder: true, _dragged: null } owner || Item is null)
                return [];
            session.LocalContext = owner._token;
            var dragItem = new UIDragItem(new NSItemProvider());
            // The card at full size while it is dragged (the system's own preview of a large view is scaled down), from
            // a snapshot taken now: the view itself is hidden behind its placeholder once the drag begins.
            if (Item is { Handler.PlatformView: UIView platformView } view && Snapshot(platformView) is { } snapshot)
            {
                var parameters = Parameters(view, platformView);
                dragItem.PreviewProvider = () => new UIDragPreview(snapshot, parameters);
            }
            return [dragItem];
        }

        public override UITargetedDragPreview? GetPreviewForLiftingItem(UIDragInteraction interaction, UIDragItem item, IUIDragSession session) =>
            Item is { } view ? Preview(view) : null;

        public override void SessionWillBegin(UIDragInteraction interaction, IUIDragSession session)
        {
            if (Owner is { } owner && Item is { } view)
                owner.Take(view, owner.Location(session), liftOnMove: false, systemPreview: true);
        }

        public override bool SessionAllowsMoveOperation(UIDragInteraction interaction, IUIDragSession session) => true;

        public override bool SessionIsRestrictedToDraggingApplication(UIDragInteraction interaction, IUIDragSession session) => true;

        public override UITargetedDragPreview? GetPreviewForCancellingItem(UIDragInteraction interaction, UIDragItem item, UITargetedDragPreview defaultPreview) =>
            Owner is { _dragged: { } view } owner && owner.RestoreOrder() is not null ? owner.Landing(view) : null;

        public override void SessionDidEnd(UIDragInteraction interaction, IUIDragSession session, UIDropOperation operation) =>
            _ = Owner?.EndAsync(cancelled: operation != UIDropOperation.Move);
    }

    sealed class DropDelegate(LayoutReorderState owner) : UIDropInteractionDelegate
    {
        readonly WeakReference<LayoutReorderState> _owner = new(owner);

        LayoutReorderState? Owner => _owner.TryGetTarget(out var o) && o._host is not null ? o : null;

        // Only this layout's own items.
        public override bool CanHandleSession(UIDropInteraction interaction, IUIDropSession session) =>
            Owner is { } owner && session.LocalDragSession?.LocalContext == owner._token;

        public override UIDropProposal SessionDidUpdate(UIDropInteraction interaction, IUIDropSession session)
        {
            if (Owner is not { _dragged: not null } owner)
                return new UIDropProposal(UIDropOperation.Cancel);
            owner.Drag(owner.Location(session));
            return new UIDropProposal(UIDropOperation.Move);
        }

        public override void PerformDrop(UIDropInteraction interaction, IUIDropSession session)
        {
        }

        public override UITargetedDragPreview? GetPreviewForDroppingItem(UIDropInteraction interaction, UIDragItem item, UITargetedDragPreview defaultPreview) =>
            Owner is { _dragged: { } view } owner ? owner.Landing(view) : null;
    }
#endif

    // A dashed frame where the item will land, under the items.
    partial void ShowPlaceholderPlatform(Rect frame, double cornerRadius)
    {
        if (_host is null)
            return;
        HidePlaceholderPlatform();
        var dark = _host.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;
        _placeholder = new CAShapeLayer
        {
            FillColor = UIColor.Clear.CGColor,
            StrokeColor = (dark ? UIColor.FromWhiteAlpha(1, 0.28f) : UIColor.FromWhiteAlpha(0, 0.25f)).CGColor,
            LineWidth = 1.5f,
            LineDashPattern = [5, 4],
        };
        Place(frame, cornerRadius, animated: false);
        _host.Layer.InsertSublayer(_placeholder, 0);
    }

    partial void MovePlaceholderPlatform(Rect frame) => Place(frame, null, animated: true);

    void Place(Rect frame, double? cornerRadius, bool animated)
    {
        if (_placeholder is null)
            return;
        var radius = cornerRadius is { } r ? (nfloat)r : _placeholder.CornerRadius;
        var was = _placeholder.Position;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _placeholder.CornerRadius = radius;
        _placeholder.Frame = new CGRect(frame.X, frame.Y, frame.Width, frame.Height);
        // Inset by half the line so the dash sits inside the item's edge.
        _placeholder.Path = UIBezierPath.FromRoundedRect(new CGRect(0.75, 0.75, frame.Width - 1.5, frame.Height - 1.5), radius).CGPath;
        CATransaction.Commit();
        // It moves with the items: the same spring from where it was.
        if (animated)
        {
            Spring(_placeholder, "position.x", (double)(was.X - _placeholder.Position.X));
            Spring(_placeholder, "position.y", (double)(was.Y - _placeholder.Position.Y));
        }
    }

    // A quick spring, damped just short of critical: the items settle in about a third of a second with a hint of
    // give, and never overshoot by more than a point or two. Additive, so a slide that starts while another is
    // running adds to it instead of cutting it off: the item keeps moving smoothly however fast the pointer goes.
    const double Stiffness = 420, Damping = 38;

    static void Spring(CALayer layer, string keyPath, double from, double to = 0)
    {
        if (Math.Abs(from - to) < 0.5 && keyPath != "transform.scale")
            return;
        var spring = new CASpringAnimation
        {
            KeyPath = keyPath,
            From = NSNumber.FromDouble(from),
            To = NSNumber.FromDouble(to),
            Additive = true,
            Mass = 1,
            Stiffness = (nfloat)Stiffness,
            Damping = (nfloat)Damping,
        };
        spring.Duration = spring.SettlingDuration;
        layer.AddAnimation(spring, null);
    }

    // The item that made room slides from where it was to its new place. Its frame has moved already; the spring
    // runs from the old place (the offset) to none.
    partial void SlidePlatform(View item, double fromX, double fromY, ref bool slid)
    {
        if (Outermost(item) is not { } platformView)
            return;
        slid = true;
        if (UIAccessibility.IsReduceMotionEnabled)
            return;
        Spring(platformView.Layer, "transform.translation.x", fromX);
        Spring(platformView.Layer, "transform.translation.y", fromY);
    }

    // The dropped item: back to its place and size with the same spring. Its translation and scale are reset at
    // once (so nothing animates them a second time) and the spring takes it from where it was shown.
    partial void SettlePlatform(View view, ref Task? settled)
    {
        if (Outermost(view) is not { } platformView)
            return;
        var (x, y, scale) = (view.TranslationX, view.TranslationY, view.Scale);
        view.TranslationX = 0;
        view.TranslationY = 0;
        view.Scale = 1;
        if (UIAccessibility.IsReduceMotionEnabled)
        {
            settled = Task.CompletedTask;
            return;
        }
        Spring(platformView.Layer, "transform.translation.x", x);
        Spring(platformView.Layer, "transform.translation.y", y);
        Spring(platformView.Layer, "transform.scale", scale - 1);
        settled = Task.Delay(TimeSpan.FromSeconds(new CASpringAnimation { Mass = 1, Stiffness = (nfloat)Stiffness, Damping = (nfloat)Damping }.SettlingDuration));
    }

    // In front of its siblings and of the placeholder layer: the subview order alone is reset when the children are
    // reordered, and a z-position keeps it on top between those changes and while it settles.
    partial void RaisePlatform(View view)
    {
        if (Outermost(view) is not { Superview: { } superview } platformView)
            return;
        superview.BringSubviewToFront(platformView);
        platformView.Layer.ZPosition = 1000;
    }

    // A view with a shadow or a clip sits in a wrapper (the handler's ContainerView); that is what lies among the
    // layout's subviews.
    static UIView? Outermost(View view) =>
        (view.Handler as IViewHandler)?.ContainerView as UIView ?? view.Handler?.PlatformView as UIView;

    partial void LowerPlatform(View view)
    {
        if (Outermost(view) is { } platformView)
            platformView.Layer.ZPosition = 0;
    }

    partial void HidePlaceholderPlatform()
    {
        _placeholder?.RemoveFromSuperLayer();
        _placeholder?.Dispose();
        _placeholder = null;
    }

    partial void ApplyActionsPlatform(View view, IReadOnlyList<ReorderAction> actions)
    {
        if (view.Handler?.PlatformView is not UIView platformView)
            return;
        platformView.AccessibilityCustomActions = actions.Count == 0
            ? null
            : [.. actions.Select(static action => new UIAccessibilityCustomAction(action.Name, (UIAccessibilityCustomActionHandler)(_ => action.Perform())))];
    }
}

#endif
