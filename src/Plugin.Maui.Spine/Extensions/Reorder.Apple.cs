#if IOS || MACCATALYST

using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls.Handlers.Items2;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

internal sealed partial class ReorderState
{
    const double LiftScale = 1.03;
    const double LiftDuration = 0.2;
    // How close to the top or bottom edge the finger must come for the list to scroll, and how fast
    // it scrolls (points per frame) right at the edge.
    const double AutoScrollZone = 64;
    const double AutoScrollSpeed = 12;

    UICollectionView? _collectionView;
    UILongPressGestureRecognizer? _longPress;
    UICollectionViewCell? _liftedCell;
    CADisplayLink? _autoScroll;
    UIGestureRecognizer? _driver;
    CGPoint _grabOffset;
    CGPoint _grabCenter;
    int _lastIndex = -1;
    bool _moving;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView view
            || (view as UICollectionView ?? SpineExtensions.FindScrollView(view) as UICollectionView) is not { } collectionView)
            return;

        _collectionView = collectionView;
        _longPress = new UILongPressGestureRecognizer(recognizer => Drive(recognizer, null))
        {
            ShouldBegin = _ => LongPressActive,
        };
#if MACCATALYST
        // As MAUI's own: a pointer press need not be held as long as a finger.
        _longPress.MinimumPressDuration = 0.1;
#endif
        collectionView.AddGestureRecognizer(_longPress);
    }

    partial void DisconnectPlatform()
    {
        if (_moving)
            Finish(cancelled: true);

        if (_longPress is not null)
        {
            _collectionView?.RemoveGestureRecognizer(_longPress);
            _longPress.Dispose();
            _longPress = null;
        }

        if (!_list.IsSet(Reorder.ModeProperty) || Mode == ReorderMode.Off)
            _list.CanReorderItems = false;

        _collectionView = null;
    }

    partial void UpdatePlatform()
    {
        // UICollectionView asks its data source whether an item can move, and MAUI's controller
        // answers with CanReorderItems; its MoveItem is what moves the item in the source. Turning
        // it on also adds MAUI's own long-press, which would lift items in every mode: switch it off.
        _list.CanReorderItems = true;

        foreach (var recognizer in _collectionView?.GestureRecognizers ?? [])
        {
            if (recognizer.GetType() == typeof(UILongPressGestureRecognizer) && recognizer != _longPress)
                recognizer.Enabled = false;
        }

        if (_moving && !IsActive)
            Finish(cancelled: true);
    }

    /// <summary>Runs a drag from either the list's long-press or a handle's touch.</summary>
    internal void Drive(UIGestureRecognizer recognizer, UIView? handle)
    {
        if (_collectionView is not { } collectionView)
            return;

        var location = recognizer.LocationInView(collectionView);

        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                Begin(recognizer, handle, location);
                break;

            case UIGestureRecognizerState.Changed when _moving && recognizer == _driver:
                Track(location);
                break;

            case UIGestureRecognizerState.Ended when _moving && recognizer == _driver:
                Finish(cancelled: false);
                break;

            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed when _moving && recognizer == _driver:
                Finish(cancelled: true);
                break;
        }
    }

    void Begin(UIGestureRecognizer recognizer, UIView? handle, CGPoint location)
    {
        if (_moving || _collectionView is not { } collectionView || !IsActive)
            return;

        var indexPath = handle is not null && FindCell(handle) is { } handleCell
            ? collectionView.IndexPathForCell(handleCell)
            : collectionView.IndexPathForItemAtPoint(location);
        if (indexPath is null || collectionView.CellForItem(indexPath) is not { } cell
            || !collectionView.BeginInteractiveMovementForItem(indexPath))
            return;

        _moving = true;
        _driver = recognizer;
        _grabCenter = cell.Center;
        _grabOffset = new CGPoint(cell.Center.X - location.X, cell.Center.Y - location.Y);
        _lastIndex = (int)indexPath.Item;

        Lift(cell);
        BeginDrag(_lastIndex);

        _autoScroll = CADisplayLink.Create(OnAutoScrollFrame);
        _autoScroll.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    void Track(CGPoint location)
    {
        if (_collectionView is not { } collectionView)
            return;

        collectionView.UpdateInteractiveMovement(Target(location));

        // The place the item would drop at is the one under its centre.
        if (collectionView.IndexPathForItemAtPoint(Target(location)) is { } indexPath
            && (int)indexPath.Item != _lastIndex)
        {
            _lastIndex = (int)indexPath.Item;
            DragStep();
        }
    }

    // A list moves its item along its own axis only, the way a table's reorder does; a grid lets it
    // go anywhere. The item keeps the spot it was grabbed by under the finger.
    CGPoint Target(CGPoint location)
    {
        var x = location.X + _grabOffset.X;
        var y = location.Y + _grabOffset.Y;

        return _list.ItemsLayout switch
        {
            LinearItemsLayout { Orientation: ItemsLayoutOrientation.Vertical } => new CGPoint(_grabCenter.X, y),
            LinearItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal } => new CGPoint(x, _grabCenter.Y),
            _ => new CGPoint(x, y),
        };
    }

    void Finish(bool cancelled)
    {
        _autoScroll?.Invalidate();
        _autoScroll = null;

        if (_collectionView is { } collectionView)
        {
            if (cancelled)
                collectionView.CancelInteractiveMovement();
            else
                collectionView.EndInteractiveMovement();
        }

        _moving = false;
        _driver = null;
        _lastIndex = -1;
        Drop();
        EndDrag();
    }

    void OnAutoScrollFrame()
    {
        if (_collectionView is not { } collectionView || _driver is not { } driver)
            return;

        var location = driver.LocationInView(collectionView);
        var vertical = _list.ItemsLayout is not LinearItemsLayout { Orientation: ItemsLayoutOrientation.Horizontal };
        var inset = collectionView.AdjustedContentInset;
        var offset = collectionView.ContentOffset;
        var bounds = collectionView.Bounds;

        // Distances from the finger to the visible edges, inside the bars that lie over the list.
        var lead = vertical ? location.Y - (offset.Y + inset.Top) : location.X - (offset.X + inset.Left);
        var trail = vertical
            ? offset.Y + bounds.Height - inset.Bottom - location.Y
            : offset.X + bounds.Width - inset.Right - location.X;

        var delta = lead < AutoScrollZone ? -Speed(lead) : trail < AutoScrollZone ? Speed(trail) : 0;
        if (delta == 0)
            return;

        var size = collectionView.ContentSize;
        var min = vertical ? -inset.Top : -inset.Left;
        var max = vertical
            ? Math.Max(min, size.Height - bounds.Height + inset.Bottom)
            : Math.Max(min, size.Width - bounds.Width + inset.Right);
        var current = vertical ? offset.Y : offset.X;
        var next = Math.Clamp(current + delta, min, max);

        if (Math.Abs(next - current) < 0.5)
            return;

        collectionView.ContentOffset = vertical ? new CGPoint(offset.X, next) : new CGPoint(next, offset.Y);

        // The finger has not moved, but the content under it has.
        Track(driver.LocationInView(collectionView));

        static double Speed(double distance) =>
            AutoScrollSpeed * (1 - Math.Clamp(distance, 0, AutoScrollZone) / AutoScrollZone);
    }

    void Lift(UICollectionViewCell cell)
    {
        _liftedCell = cell;

        // The layout sets the cell's own transform on every move, so the scale goes on its content.
        var layer = cell.Layer;
        layer.ShadowColor = UIColor.Black.CGColor;
        layer.ShadowOffset = new CGSize(0, 6);
        layer.ShadowRadius = 14;

        Animate(cell, lifted: true);
    }

    void Drop()
    {
        if (_liftedCell is not { } cell)
            return;

        _liftedCell = null;
        Animate(cell, lifted: false);
    }

    static void Animate(UICollectionViewCell cell, bool lifted)
    {
        var reduceMotion = UIAccessibility.IsReduceMotionEnabled;

        var shadow = CABasicAnimation.FromKeyPath("shadowOpacity");
        shadow.From = NSNumber.FromFloat(cell.Layer.PresentationLayer?.ShadowOpacity ?? cell.Layer.ShadowOpacity);
        shadow.Duration = LiftDuration;
        cell.Layer.ShadowOpacity = lifted ? 0.22f : 0f;
        cell.Layer.AddAnimation(shadow, "spine.reorder.shadow");

        var scale = lifted && !reduceMotion ? LiftScale : 1;
        UIView.Animate(LiftDuration, 0, UIViewAnimationOptions.BeginFromCurrentState | UIViewAnimationOptions.AllowUserInteraction,
            () => cell.ContentView.Transform = CGAffineTransform.MakeScale((nfloat)scale, (nfloat)scale),
            static () => { });
    }

    static UICollectionViewCell? FindCell(UIView view)
    {
        for (var current = view; current is not null; current = current.Superview)
        {
            if (current is UICollectionViewCell cell)
                return cell;
        }

        return null;
    }

    bool MovePlatform(int from, int to)
    {
        if (_collectionView is not { } collectionView
            || collectionView.WeakDataSource is not ReorderableItemsViewController2<ReorderableItemsView> controller)
            return false;

        var fromPath = NSIndexPath.FromItemSection(from, 0);
        var toPath = NSIndexPath.FromItemSection(to, 0);
        var cell = collectionView.CellForItem(fromPath);

        // MAUI's controller moves the item in the source without echoing it back to the list; the
        // list is then told to animate the same move.
        controller.MoveItem(collectionView, fromPath, toPath);
        collectionView.MoveItem(fromPath, toPath);

        // Keep VoiceOver on the item that moved, now somewhere else on the screen.
        if (cell is not null)
            UIAccessibility.PostNotification(UIAccessibilityPostNotification.LayoutChanged, FirstElement(cell));

        return true;
    }

    static NSObject FirstElement(UIView view)
    {
        if (view.IsAccessibilityElement)
            return view;

        foreach (var subview in view.Subviews)
        {
            if (FirstElement(subview) is UIView found && found.IsAccessibilityElement)
                return found;
        }

        return view;
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

internal sealed partial class ReorderHandleState
{
    UIView? _host;
    UILongPressGestureRecognizer? _press;
    bool _labelled;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not UIView host)
            return;

        _host = host;
        host.UserInteractionEnabled = true;

        // Picks the item up the moment the handle is touched, as a table's reorder control does.
        _press = new UILongPressGestureRecognizer(recognizer => Owner?.Drive(recognizer, _host))
        {
            MinimumPressDuration = 0,
            ShouldBegin = _ => CanDrag,
        };
        host.AddGestureRecognizer(_press);
    }

    partial void DisconnectPlatform()
    {
        if (_press is not null)
        {
            _host?.RemoveGestureRecognizer(_press);
            _press.Dispose();
            _press = null;
        }

        _host = null;
    }

    partial void UpdatePlatform()
    {
        if (_host is null)
            return;

        // A grip glyph says nothing to VoiceOver; name it, unless the app already has.
        var label = SemanticProperties.GetDescription(View);
        if (string.IsNullOrEmpty(label) && CanDrag)
        {
            _host.IsAccessibilityElement = true;
            _host.AccessibilityLabel = Common.SpineStrings.Current["Spine.Reorder.Handle"];
            _labelled = true;
        }
        else if (_labelled && !CanDrag)
        {
            _host.IsAccessibilityElement = false;
            _host.AccessibilityLabel = null;
            _labelled = false;
        }
    }
}
#endif
