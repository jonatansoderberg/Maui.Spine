namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Turns a page action's glyph while the action is busy and spins (<see cref="Core.PageAction.SpinsWhenBusy"/>), and
/// lets the turn it is on finish when it stops. On Apple the glyph's layer turns, so the glass circle under it stays still.
/// </summary>
internal static class BusySpin
{
    const double TurnSeconds = 0.9;
    const string Key = "Spine.Busy";

    /// <summary>
    /// Starts the turn, or stops it: finishing the turn it is on, or at once when the glyph now belongs to another
    /// action (<paramref name="finish"/> false). False when the view has no platform view yet to turn.
    /// </summary>
    public static bool Set(View? view, bool on, bool finish = true)
    {
        if (view is null)
            return false;
#if IOS || MACCATALYST
        // A glass button that holds an image draws it in a configuration view, not its ImageView; morphing
        // actions draw the glyph in an Image over the glass instead, which is the view passed here then.
        var layer = view.Handler?.PlatformView switch
        {
            UIKit.UIButton button => button.ImageView?.Layer,
            UIKit.UIView platform => platform.Layer,
            _ => null,
        };
        if (layer is null)
            return false;

        if (on)
        {
            if (layer.AnimationForKey(Key) is not null)
                return true;
            layer.RemoveAnimation(Key + ".Finish");
            layer.AddAnimation(Turn(0, TurnSeconds, float.PositiveInfinity), Key);
            return true;
        }

        if (layer.AnimationForKey(Key) is null)
            return true;
        var angle = (layer.PresentationLayer?.ValueForKeyPath(new Foundation.NSString("transform.rotation.z")) as Foundation.NSNumber)?.DoubleValue ?? 0;
        if (angle < 0)
            angle += Math.PI * 2;
        layer.RemoveAnimation(Key);
        var left = Math.PI * 2 - angle;
        if (finish && left > 0.01)
            layer.AddAnimation(Turn(angle, TurnSeconds * left / (Math.PI * 2), 1), Key + ".Finish");
        return true;
#else
        if (on)
        {
            if (view.AnimationIsRunning(Key))
                return true;
            view.Animate(Key, new Animation(r => view.Rotation = r, view.Rotation % 360, 360),
                length: (uint)(TurnSeconds * 1000), easing: Easing.Linear, finished: (_, _) => view.Rotation = 0, repeat: () => true);
            return true;
        }

        if (!view.AnimationIsRunning(Key))
            return true;
        var from = view.Rotation % 360;
        view.AbortAnimation(Key);
        if (!finish)
        {
            view.Rotation = 0;
            return true;
        }
        view.Animate(Key, new Animation(r => view.Rotation = r, from, 360),
            length: (uint)(TurnSeconds * 1000 * (360 - from) / 360), easing: Easing.Linear, finished: (_, _) => view.Rotation = 0);
        return true;
#endif
    }

#if IOS || MACCATALYST
    static CoreAnimation.CABasicAnimation Turn(double from, double seconds, float repeat) => new()
    {
        KeyPath = "transform.rotation.z",
        From = Foundation.NSNumber.FromDouble(from),
        To = Foundation.NSNumber.FromDouble(Math.PI * 2),
        Duration = seconds,
        RepeatCount = repeat,
    };
#endif
}
