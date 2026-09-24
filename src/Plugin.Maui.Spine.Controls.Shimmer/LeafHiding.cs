#if WINDOWS
using WAccessibilityView = Microsoft.UI.Xaml.Automation.Peers.AccessibilityView;
using WAutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using WUIElement = Microsoft.UI.Xaml.UIElement;
#endif

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Hides a view of a skeleton layout on the platform view, so neither it nor a screen reader shows
/// it, without touching its bindable properties: setting <c>Opacity</c> from code would drop a
/// binding the app put on it.
/// </summary>
internal static class LeafHiding
{
    /// <summary>
    /// Hides <paramref name="view"/> and returns what restores it, or <see langword="null"/> while it
    /// has no platform view yet (the next scan catches it).
    /// </summary>
    public static object? Hide(VisualElement view)
    {
        if (view.Handler is not IPlatformViewHandler handler)
            return null;

#if IOS || MACCATALYST
        if ((handler.ContainerView ?? handler.PlatformView) is not UIKit.UIView platform)
            return null;
        var hidden = platform.AccessibilityElementsHidden;
        platform.Alpha = 0;
        platform.AccessibilityElementsHidden = true;
        return hidden;
#elif ANDROID
        if ((handler.ContainerView ?? handler.PlatformView) is not Android.Views.View platform)
            return null;
        var important = platform.ImportantForAccessibility;
        platform.Alpha = 0;
        platform.ImportantForAccessibility = Android.Views.ImportantForAccessibility.NoHideDescendants;
        return important;
#elif WINDOWS
        if ((handler.ContainerView ?? handler.PlatformView) is not WUIElement platform)
            return null;
        var accessibilityView = WAutomationProperties.GetAccessibilityView(platform);
        platform.Opacity = 0;
        WAutomationProperties.SetAccessibilityView(platform, WAccessibilityView.Raw);
        return accessibilityView;
#else
        return null;
#endif
    }

    /// <summary>Undoes <see cref="Hide"/>; the opacity comes back from the view's own <c>Opacity</c>.</summary>
    public static void Restore(VisualElement view, object token)
    {
        if (view.Handler is not IPlatformViewHandler handler)
            return;

        handler.UpdateValue(nameof(IView.Opacity));

#if IOS || MACCATALYST
        if ((handler.ContainerView ?? handler.PlatformView) is UIKit.UIView platform && token is bool hidden)
            platform.AccessibilityElementsHidden = hidden;
#elif ANDROID
        if ((handler.ContainerView ?? handler.PlatformView) is Android.Views.View platform && token is Android.Views.ImportantForAccessibility important)
            platform.ImportantForAccessibility = important;
#elif WINDOWS
        if ((handler.ContainerView ?? handler.PlatformView) is WUIElement platform && token is WAccessibilityView accessibilityView)
            WAutomationProperties.SetAccessibilityView(platform, accessibilityView);
#endif
    }
}
