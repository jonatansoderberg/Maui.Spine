#if IOS || MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class LightboxOverlay
{
    private UIView? _view;

    /// <remarks>
    /// A view on the key window, above the sheet's presentation, rather than a presented controller:
    /// the zoom measures the thumbnail and the lightbox in one window, and nothing slides in.
    /// </remarks>
    partial void Attach(IMauiContext context)
    {
        if (Platform.GetCurrentUIViewController()?.View?.Window is not { } window)
            return;

        var view = _region.ToPlatform(context);
        view.RemoveFromSuperview();
        view.Frame = window.Bounds;
        view.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
        window.AddSubview(view);
        _view = view;
    }

    partial void Detach()
    {
        _view?.RemoveFromSuperview();
        _view = null;
    }
}
#endif
