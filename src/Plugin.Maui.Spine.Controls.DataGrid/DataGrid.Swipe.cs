namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Keeps a row's swipe from starting while the list scrolls.
/// </summary>
/// <remarks>
/// MAUI's <see cref="SwipeView"/> takes the direction of a drag from its first move. On iOS its pan runs
/// alongside the list's: a scroll with a little sideways drift slid the row sideways and back, and past
/// 15 % of the actions' width switched the list's scrolling off in the middle of the scroll. On Android
/// it took the gesture from the list on the first move that went more sideways than up or down, and
/// the list did not scroll at all. Here the direction is decided once, after the finger has moved as
/// far as a scroll needs, and only a mostly sideways drag becomes a swipe, as in a <c>UITableView</c>
/// or with <c>ItemTouchHelper</c>. A row that is open can be dragged closed either way.
/// </remarks>
public partial class DataGrid
{
    /// <summary>How much more sideways than up or down a drag has to go to swipe a row: within about 34° of the horizontal.</summary>
    internal const double SwipeHorizontalBias = 1.5;

    internal static bool IsSwipeDrag(double dx, double dy) =>
        Math.Abs(dx) > Math.Abs(dy) * SwipeHorizontalBias;

    private SwipeView CreateSwipeView(View row)
    {
        var swipe = new SwipeView { Content = row };

        // A sideways drag is a swipe, not a press held still.
        swipe.SwipeStarted += (_, _) => CancelRowPress();

#if IOS || MACCATALYST
        swipe.HandlerChanged += (_, _) => SwipeGate.Attach(swipe);
#endif
        return swipe;
    }

#if ANDROID
    // Android's decision is made by the swipe view's parent, which sees every touch before the swipe view does.
    private static View GateSwipe(SwipeView swipe) => new SwipeGate { Content = swipe };
#else
    private static View GateSwipe(SwipeView swipe) => swipe;
#endif

#if IOS || MACCATALYST
    private static class SwipeGate
    {
        public static void Attach(SwipeView swipe)
        {
            if (swipe.Handler?.PlatformView is not UIKit.UIView view
                || view.GestureRecognizers?.OfType<UIKit.UIPanGestureRecognizer>().FirstOrDefault() is not { } pan)
                return;

            UIKit.UIScrollView? locked = null;

            // Asked once, when the pan has moved far enough to begin: about 10 points.
            pan.ShouldBegin = _ =>
            {
                if (((ISwipeView)swipe).IsOpen)
                    return true;

                var moved = pan.TranslationInView(view);
                if (moved.X == 0 && moved.Y == 0)
                    moved = pan.VelocityInView(view);

                return IsSwipeDrag(moved.X, moved.Y);
            };

            // The list's pan runs alongside the swipe's; hold the list still for a swipe that began.
            pan.AddTarget(() =>
            {
                switch (pan.State)
                {
                    case UIKit.UIGestureRecognizerState.Began:
                        locked = FindList(view);
                        if (locked is not null)
                            locked.ScrollEnabled = false;
                        break;
                    case UIKit.UIGestureRecognizerState.Ended:
                    case UIKit.UIGestureRecognizerState.Cancelled:
                    case UIKit.UIGestureRecognizerState.Failed:
                        if (locked is not null)
                            locked.ScrollEnabled = true;
                        locked = null;
                        break;
                }
            });
        }

        private static UIKit.UIScrollView? FindList(UIKit.UIView view)
        {
            for (var parent = view.Superview; parent is not null; parent = parent.Superview)
            {
                if (parent is UIKit.UIScrollView list)
                    return list;
            }

            return null;
        }
    }
#endif
}

#if ANDROID
/// <summary>The parent of a row's <see cref="SwipeView"/> on Android; see <see cref="SwipeGateViewGroup"/>.</summary>
internal sealed class SwipeGate : ContentView;

internal sealed class SwipeGateHandler : Microsoft.Maui.Handlers.ContentViewHandler
{
    protected override Microsoft.Maui.Platform.ContentViewGroup CreatePlatformView()
    {
        var view = new SwipeGateViewGroup(Context, () => ((VirtualView as ContentView)?.Content as ISwipeView)?.IsOpen == true)
        {
            CrossPlatformLayout = VirtualView,
        };
        view.SetClipChildren(false);
        return view;
    }
}

/// <summary>
/// Holds a drag back from the swipe view until it has gone a touch slop, then either hands it over
/// (mostly sideways, or the row is open) or keeps it from the swipe view, which leaves the list free
/// to take it. Until a swipe is decided, the swipe view's request that the list keep out is not
/// passed on.
/// </summary>
internal sealed class SwipeGateViewGroup(Android.Content.Context context, Func<bool> isOpen)
    : Microsoft.Maui.Platform.ContentViewGroup(context)
{
    private enum Gesture { None, Undecided, Swipe, Scroll }

    private readonly int _slop = Android.Views.ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 24;
    private float _downX, _downY;
    private Gesture _gesture;

    public override bool DispatchTouchEvent(Android.Views.MotionEvent? e)
    {
        if (e is null)
            return base.DispatchTouchEvent(e);

        switch (e.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                _downX = e.RawX;
                _downY = e.RawY;
                _gesture = Gesture.Undecided;
                break;

            case Android.Views.MotionEventActions.Move when _gesture == Gesture.Undecided:
                var dx = e.RawX - _downX;
                var dy = e.RawY - _downY;
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < _slop)
                    return true;

                _gesture = isOpen() || DataGrid.IsSwipeDrag(dx, dy) ? Gesture.Swipe : Gesture.Scroll;
                if (_gesture == Gesture.Swipe)
                {
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    break;
                }

                // The row saw the finger go down and nothing since; without this, lifting it where the
                // list cannot scroll any further would read as a tap.
                if (Android.Views.MotionEvent.Obtain(e) is { } cancel)
                {
                    cancel.Action = Android.Views.MotionEventActions.Cancel;
                    base.DispatchTouchEvent(cancel);
                    cancel.Recycle();
                }
                return true;

            case Android.Views.MotionEventActions.Move when _gesture == Gesture.Scroll:
                return true;

            case Android.Views.MotionEventActions.Up when _gesture == Gesture.Scroll:
                _gesture = Gesture.None;
                return true;

            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                _gesture = Gesture.None;
                break;
        }

        return base.DispatchTouchEvent(e);
    }

    public override void RequestDisallowInterceptTouchEvent(bool disallowIntercept)
    {
        if (disallowIntercept && _gesture is Gesture.Undecided or Gesture.Scroll)
            return;

        base.RequestDisallowInterceptTouchEvent(disallowIntercept);
    }
}
#endif
