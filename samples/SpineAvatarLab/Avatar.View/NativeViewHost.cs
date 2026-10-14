using Microsoft.Maui.Handlers;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>Places a platform view the avatar code made itself (a SceneKit view) in the MAUI tree.</summary>
internal sealed class NativeViewHost(object platformView) : View
{
    public object PlatformView { get; } = platformView;
}

#if IOS || MACCATALYST
internal sealed class NativeViewHostHandler() : ViewHandler<NativeViewHost, UIKit.UIView>(ViewMapper)
{
    protected override UIKit.UIView CreatePlatformView() => (UIKit.UIView)VirtualView.PlatformView;
}
#elif ANDROID
internal sealed class NativeViewHostHandler() : ViewHandler<NativeViewHost, Android.Views.View>(ViewMapper)
{
    protected override Android.Views.View CreatePlatformView() => (Android.Views.View)VirtualView.PlatformView;
}
#endif
