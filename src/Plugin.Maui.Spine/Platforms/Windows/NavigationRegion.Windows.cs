namespace Plugin.Maui.Spine.Presentation;

public sealed partial class NavigationRegion
{
    // A z-index orders the panel's children as they are drawn and hit, without moving any of them.
    partial void OrderLayersOnPlatform(View[] layers)
    {
        for (var i = 0; i < layers.Length; i++)
        {
            if (layers[i].Handler is IPlatformViewHandler handler && (handler.ContainerView ?? handler.PlatformView) is Microsoft.UI.Xaml.UIElement element)
                Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(element, i);
        }
    }
}
