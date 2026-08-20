#if IOS || MACCATALYST

using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        SwitchHandler.Mapper.AppendToMapping("SpineInstantSwitch", static (handler, _) =>
        {
            if (handler.PlatformView is not UISwitch uiSwitch)
                return;

            // Handlers are recycled (collection view cells re-bind onto the same
            // UISwitch), so the mapper runs again on a view that already has it.
            if (uiSwitch.GestureRecognizers?.Any(g => g is SpineInstantSwitchRecognizer) == true)
                return;

            uiSwitch.AddGestureRecognizer(new SpineInstantSwitchRecognizer());
        });
    }
}

/// <summary>
/// Settles a tap on a <see cref="UISwitch"/> the moment the touch lifts, however
/// briefly it lasted, and claims the touch so an ancestor pan (sheet drag, sheet
/// dismiss, interactive back) cannot take it away first.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UISwitch"/>'s own tracking ignores a touch that begins and ends
/// within the same run-loop pass, which reads as "the switch needs a long press"
/// — most visibly inside a sheet, where the surrounding drag gestures make it look
/// like the sheet swallowed the tap.
/// </para>
/// <para>
/// Movement past <see cref="MoveSlop"/> fails the recognizer, so dragging the
/// thumb, scrolling the list and dragging the sheet all keep working untouched.
/// </para>
/// </remarks>
internal sealed class SpineInstantSwitchRecognizer : UIGestureRecognizer
{
    private const float MoveSlop = 10f;

    private CGPoint _origin;
    private bool _valueAtTouchDown;

    public SpineInstantSwitchRecognizer()
    {
        // Recognizing cancels the touch inside the UISwitch, which is what keeps
        // its own tracking from toggling a second time on a normal-length tap.
        CancelsTouchesInView = true;

        // Another recognizer winning the touch must not pre-empt this one.
        ShouldRecognizeSimultaneously = static (_, _) => true;
    }

    private UISwitch? Switch => View as UISwitch;

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        base.TouchesBegan(touches, evt);

        if (Switch is not { Enabled: true } uiSwitch || touches.Count != 1)
        {
            State = UIGestureRecognizerState.Failed;
            return;
        }

        _origin = LocationInView(uiSwitch);
        _valueAtTouchDown = uiSwitch.On;
    }

    public override void TouchesMoved(NSSet touches, UIEvent evt)
    {
        base.TouchesMoved(touches, evt);

        if (State != UIGestureRecognizerState.Possible || Switch is not { } uiSwitch)
            return;

        var point = LocationInView(uiSwitch);
        if (Math.Abs(point.X - _origin.X) > MoveSlop || Math.Abs(point.Y - _origin.Y) > MoveSlop)
            State = UIGestureRecognizerState.Failed;
    }

    public override void TouchesEnded(NSSet touches, UIEvent evt)
    {
        base.TouchesEnded(touches, evt);

        if (State != UIGestureRecognizerState.Possible || Switch is not { } uiSwitch)
        {
            State = UIGestureRecognizerState.Failed;
            return;
        }

        State = UIGestureRecognizerState.Recognized;

        var valueAtTouchDown = _valueAtTouchDown;

        // One turn later, so that a toggle UIKit performed for this same touch
        // counts as the tap instead of being doubled by this one.
        uiSwitch.BeginInvokeOnMainThread(() =>
        {
            if (uiSwitch.On != valueAtTouchDown)
                return;

            uiSwitch.SetState(!valueAtTouchDown, animated: true);
            uiSwitch.SendActionForControlEvents(UIControlEvent.ValueChanged);
        });
    }

    public override void TouchesCancelled(NSSet touches, UIEvent evt)
    {
        base.TouchesCancelled(touches, evt);
        State = UIGestureRecognizerState.Cancelled;
    }
}

#endif
