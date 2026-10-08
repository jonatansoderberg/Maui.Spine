#if IOS

using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

// iOS only: Mac Catalyst runs motion effects but has no tilt to drive them.
internal sealed partial class MotionState
{
    UIView? _host;
    UIMotionEffectGroup? _effects;

    internal UIMotionEffectGroup? Effects => _effects;

    partial void ConnectPlatform(object platformView) => _host = platformView as UIView;

    partial void DisconnectPlatform()
    {
        RemoveEffects();
        _host = null;
    }

    partial void UpdatePlatform()
    {
        RemoveEffects();

        if (_host is not { } host)
            return;

        // The system drives the effects from the device's attitude, recentres them on a held tilt and
        // turns them off under Reduce Motion. Its viewer offset is positive when the screen turns to
        // the viewer's right or down, so a positive depth moves toward the edge that tilts away.
        var depth = NSNumber.FromDouble(Depth);
        var negative = NSNumber.FromDouble(-Depth);

        _effects = new UIMotionEffectGroup
        {
            MotionEffects =
            [
                new UIInterpolatingMotionEffect("center.x", UIInterpolatingMotionEffectType.TiltAlongHorizontalAxis)
                {
                    MinimumRelativeValue = negative,
                    MaximumRelativeValue = depth,
                },
                new UIInterpolatingMotionEffect("center.y", UIInterpolatingMotionEffectType.TiltAlongVerticalAxis)
                {
                    MinimumRelativeValue = negative,
                    MaximumRelativeValue = depth,
                },
            ],
        };

        host.AddMotionEffect(_effects);
    }

    void RemoveEffects()
    {
        if (_effects is not null)
            _host?.RemoveMotionEffect(_effects);

        _effects = null;
    }
}

#endif
