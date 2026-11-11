using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// <see cref="Reorder"/> on a bindable layout on Windows: the layout's panel passes the pointer to the shared drag. A mouse
/// or pen press takes the item under it and lifts it once it starts to move, as a drag on the desktop does, so a click
/// stays a click; a finger lifts it after a long press. While an item is lifted the panel holds the pointer, so the drag
/// goes on outside the item and the layout.
/// </summary>
internal sealed partial class LayoutReorderState
{
    // A finger's long press, as long as WinUI's own hold.
    static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    FrameworkElement? _panel;
    Pointer? _pointer;
    uint _pointerId;
    bool _captured;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? _hold;

    // One delegate each, so the handlers come off again.
    PointerEventHandler? _onPressed, _onMoved, _onReleased, _onCanceled;

    partial void ConnectPlatform(object platformView)
    {
        if (platformView is not FrameworkElement panel || _panel is not null)
            return;

        _panel = panel;
        _onPressed ??= OnPointerPressed;
        _onMoved ??= OnPointerMoved;
        _onReleased ??= OnPointerReleased;
        _onCanceled ??= OnPointerCanceled;
        // Handled ones too: a card's own tap or button marks the press handled on its way up.
        panel.AddHandler(UIElement.PointerPressedEvent, _onPressed, true);
        panel.AddHandler(UIElement.PointerMovedEvent, _onMoved, true);
        panel.AddHandler(UIElement.PointerReleasedEvent, _onReleased, true);
        panel.AddHandler(UIElement.PointerCanceledEvent, _onCanceled, true);
        panel.AddHandler(UIElement.PointerCaptureLostEvent, _onCanceled, true);
    }

    partial void DisconnectPlatform()
    {
        if (_dragged is not null)
            _ = EndAsync(cancelled: true);
        StopHold();
        if (_panel is not { } panel)
            return;
        panel.RemoveHandler(UIElement.PointerPressedEvent, _onPressed);
        panel.RemoveHandler(UIElement.PointerMovedEvent, _onMoved);
        panel.RemoveHandler(UIElement.PointerReleasedEvent, _onReleased);
        panel.RemoveHandler(UIElement.PointerCanceledEvent, _onCanceled);
        panel.RemoveHandler(UIElement.PointerCaptureLostEvent, _onCanceled);
        _panel = null;
        _pointer = null;
        _captured = false;
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_panel is not { } panel || _pointer is not null || !CanReorder)
            return;
        var point = e.GetCurrentPoint(panel);
        var touch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        if (!touch && !point.Properties.IsLeftButtonPressed)
            return;

        var at = new Point(point.Position.X, point.Position.Y);
        // Lifted when it moves (mouse, pen) or when held (finger, below).
        if (!Begin(at, liftOnMove: true))
            return;
        _pointer = e.Pointer;
        _pointerId = e.Pointer.PointerId;

        // A finger lifts the item once it has been held still; until then a move is a scroll, and lets it go.
        if (touch)
        {
            _hold = panel.DispatcherQueue.CreateTimer();
            _hold.Interval = HoldTime;
            _hold.IsRepeating = false;
            _hold.Tick += (_, _) =>
            {
                StopHold();
                if (_dragged is { } view && !_lifted)
                {
                    Lift(view);
                    Capture();
                }
            };
            _hold.Start();
        }
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panel is not { } panel || _pointer is null || e.Pointer.PointerId != _pointerId || _dragged is null)
            return;
        var point = e.GetCurrentPoint(panel).Position;
        var at = new Point(point.X, point.Y);

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch && !_lifted)
        {
            // Moved before the hold: the finger scrolls; nothing was lifted.
            if (Math.Abs(at.X - _pressed.X) >= LiftDistance || Math.Abs(at.Y - _pressed.Y) >= LiftDistance)
                Release(cancelled: true);
            return;
        }

        Drag(at);
        if (_lifted)
        {
            Capture();
            e.Handled = true;
        }
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer is null || e.Pointer.PointerId != _pointerId)
            return;
        if (_lifted)
            e.Handled = true;
        Release(cancelled: false);
    }

    void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer is null || e.Pointer.PointerId != _pointerId)
            return;
        Release(cancelled: true);
    }

    // The panel holds the pointer while an item is lifted: the drag goes on wherever it moves.
    void Capture()
    {
        if (_captured || _panel is not { } panel || _pointer is not { } pointer)
            return;
        _captured = panel.CapturePointer(pointer);
    }

    void Release(bool cancelled)
    {
        StopHold();
        var pointer = _pointer;
        _pointer = null;
        if (_captured && pointer is not null)
        {
            _captured = false;
            _panel?.ReleasePointerCapture(pointer);
        }
        _ = EndAsync(cancelled);
    }

    void StopHold()
    {
        _hold?.Stop();
        _hold = null;
    }
}
