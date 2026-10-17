using Microsoft.Maui.Handlers;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Places a platform view the avatar made itself (a Metal or SceneKit view) in the MAUI tree.</summary>
internal sealed class NativeViewHost(object platformView) : View
{
    public object PlatformView { get; } = platformView;
}

#if IOS || MACCATALYST
internal sealed class NativeViewHostHandler() : ViewHandler<NativeViewHost, UIKit.UIView>(ViewMapper)
{
    protected override UIKit.UIView CreatePlatformView() => (UIKit.UIView)VirtualView.PlatformView;
}
#endif
