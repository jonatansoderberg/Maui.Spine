using Android.Content;
using Android.Runtime;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>A <see cref="ContentViewHandler"/> whose view takes the back-swipe over from the page under it.</summary>
internal sealed class BackSwipeHostHandler : ContentViewHandler
{
    protected override ContentViewGroup CreatePlatformView() =>
        new BackSwipeViewGroup(Context) { CrossPlatformLayout = VirtualView, Host = VirtualView as BackSwipeHost };
}

/// <summary>
/// Watches the touches on their way to the page (<see cref="OnInterceptTouchEvent"/>) and, once a
/// drag from the leading edge has run rightward past the touch slop, more sideways than up or down,
/// takes the rest of it: the page gets a cancel, as a list does when a pager takes a sideways drag.
/// </summary>
internal sealed class BackSwipeViewGroup : ContentViewGroup
{
    private readonly int _touchSlop;
    private float _startX, _startY;
    private bool _watching, _swiping;

    public BackSwipeViewGroup(Context context) : base(context) =>
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;

    public BackSwipeViewGroup(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) =>
        _touchSlop = 16;

    public BackSwipeHost? Host { get; set; }

    private float Density => Resources?.DisplayMetrics?.Density ?? 1f;

    public override bool OnInterceptTouchEvent(MotionEvent? e)
    {
        if (e is null || Host is not { } host)
            return base.OnInterceptTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                // Screen coordinates throughout: the layer moves with the finger once it swipes.
                _startX = e.RawX;
                _startY = e.RawY;
                _swiping = false;
                _watching = host.Accepts?.Invoke(e.GetX() / Density) == true;
                break;

            case MotionEventActions.Move when _watching:
                var dx = e.RawX - _startX;
                var dy = e.RawY - _startY;

                if (dx > _touchSlop && dx > Math.Abs(dy))
                {
                    // The swipe starts here rather than at the touch, so the page does not jump by the slop.
                    _watching = false;
                    _swiping = true;
                    _startX = e.RawX;
                    _startY = e.RawY;
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    host.Swiped?.Invoke(GestureStatus.Started, 0, 0);
                    return true;
                }

                if (Math.Abs(dy) > _touchSlop || dx < -_touchSlop)
                    _watching = false;
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _watching = false;
                break;
        }

        return base.OnInterceptTouchEvent(e);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (!_swiping || e is null || Host is not { } host)
            return base.OnTouchEvent(e);

        var x = (e.RawX - _startX) / Density;
        var y = (e.RawY - _startY) / Density;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Move:
                host.Swiped?.Invoke(GestureStatus.Running, x, y);
                break;

            case MotionEventActions.Up:
                _swiping = false;
                host.Swiped?.Invoke(GestureStatus.Completed, x, y);
                break;

            case MotionEventActions.Cancel:
                _swiping = false;
                host.Swiped?.Invoke(GestureStatus.Canceled, x, y);
                break;
        }

        return true;
    }
}
