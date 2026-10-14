#if IOS || MACCATALYST
using UIKit;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineTargets
{
    private static partial bool TryGetPlatformFrame(object platformView, out Rect frame)
    {
        frame = default;
        if (platformView is not UIView { Window: { } } view)
            return false;

        var rect = view.ConvertRectToView(view.Bounds, null);
        frame = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        return true;
    }
}
#endif
