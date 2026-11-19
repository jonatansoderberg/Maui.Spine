namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A finger's first moments on a chart in a <see cref="ChartScroller"/>, before they pick a value out: fingers seldom
/// land together, and a second one on its way makes the touch a scroll. A finger still alone after <see cref="Wait"/>
/// picks out where it is then; one that is no longer to pick (<c>still</c> false, as when a scroll began) picks nothing.
/// </summary>
internal sealed class TouchGrace
{
    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(150);

    long _pressed = long.MinValue / 2;
    double _x;
    bool _waiting;

    public void Press() => _pressed = Environment.TickCount64;

    public void Move(BindableObject owner, double x, Action<double> select, Func<bool> still)
    {
        _x = x;
        var waited = Environment.TickCount64 - _pressed;
        if (waited >= Wait.TotalMilliseconds)
        {
            select(x);
            return;
        }
        if (_waiting)
            return;
        _waiting = true;
        owner.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(Wait.TotalMilliseconds - waited), () =>
        {
            _waiting = false;
            if (still())
                select(_x);
        });
    }
}
