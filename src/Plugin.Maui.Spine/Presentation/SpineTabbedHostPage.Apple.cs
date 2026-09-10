#if IOS || MACCATALYST
using AsyncAwaitBestPractices;
using Foundation;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Svg;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

public partial class SpineTabbedHostPage
{
    private bool _appleAttached;

    private UITabBarController? Controller =>
        (Handler as IPlatformViewHandler)?.ViewController as UITabBarController;

    partial void PlatformAttach()
    {
        var controller = Controller;
        if (controller is null)
            return;

        if (!_appleAttached)
        {
            _appleAttached = true;

            // MAUI's renderer installs an event-backed internal delegate on the controller and
            // relies on it for CurrentPage sync — it must not be replaced. Assigning the
            // binding's ShouldSelectViewController property joins that same internal delegate,
            // which is where re-selection of the active tab is observable.
            controller.ShouldSelectViewController = (tabBarController, viewController) =>
            {
                if (ReferenceEquals(tabBarController.SelectedViewController, viewController)
                    && tabBarController.ViewControllers is { } vcs)
                    OnTabReselected(Array.IndexOf(vcs, viewController));

                return true;
            };

#if IOS
            if (_options.Tabs.MinimizeOnScroll && OperatingSystem.IsIOSVersionAtLeast(26))
                controller.TabBarMinimizeBehavior = UITabBarMinimizeBehavior.OnScrollDown;

            HookTabPageInsets();
#endif

            ApplyTabBarItemImages(controller);
            ApplyStyle(controller);
        }

        ApplyAllBadges();
    }

    partial void PlatformApplyBadge(int index, string? text)
    {
        if (Controller?.ViewControllers is not { } controllers || index >= controllers.Length)
            return;

        // An empty string renders as a dot-style minimal badge.
        controllers[index].TabBarItem.BadgeValue = text switch
        {
            null => null,
            "" => "•",
            _ => text,
        };

        if (text is not null && _options.Tabs.Style?.BadgeBackgroundColor is { } badgeColor)
            controllers[index].TabBarItem.BadgeColor = badgeColor.ToPlatform();
    }

    /// <summary>
    /// Renders each tab's SVG icon and assigns it directly to the <see cref="UITabBarItem"/> as a
    /// template image, so the bar tints it. The loader renders for the screen density and the
    /// image source carries the scale.
    /// </summary>
    private void ApplyTabBarItemImages(UITabBarController controller)
    {
        if (controller.ViewControllers is not { } controllers)
            return;

        var svgResources = _services.GetRequiredService<ResourceNameCache>();

        for (var i = 0; i < _slots.Count && i < controllers.Length; i++)
        {
            if (_slots[i].Definition.Meta.Icon is not { } icon)
                continue;

            var resolved = svgResources.Resolve(icon) ?? icon;
            if (SvgBitmapLoader.LoadFromEmbedded(resolved, 25, 25, Colors.Black) is not SvgBitmapImageSource source)
                continue;

            LoadTabImageAsync(controllers[i], source).SafeFireAndForget();
        }
    }

    private static async Task LoadTabImageAsync(UIViewController controller, SvgBitmapImageSource source)
    {
        using var stream = await ((IStreamImageSource)source).GetStreamAsync(CancellationToken.None);
        if (stream is null)
            return;

        using var data = NSData.FromStream(stream);
        if (data is null)
            return;

        var image = UIImage.LoadFromData(data, source.Scale)?
            .ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);

        if (image is not null)
            MainThread.BeginInvokeOnMainThread(() => controller.TabBarItem.Image = image);
    }

    private void ApplyStyle(UITabBarController controller)
    {
        if (_options.Tabs.Style is not { } style)
            return;

        var tabBar = controller.TabBar;

        if (style.SelectedColor is { } selected)
            tabBar.TintColor = selected.ToPlatform();

        if (style.UnselectedColor is { } unselected)
            tabBar.UnselectedItemTintColor = unselected.ToPlatform();

        if (style.BarBackgroundColor is { } background)
        {
            // A solid background forfeits the iOS 26 Liquid Glass material by design.
            var appearance = new UITabBarAppearance();
            appearance.ConfigureWithOpaqueBackground();
            appearance.BackgroundColor = background.ToPlatform();
            tabBar.StandardAppearance = appearance;
            tabBar.ScrollEdgeAppearance = appearance;
        }
    }

#if IOS
    /// <summary>
    /// Feeds each tab page's measured bottom safe area (which includes the tab bar the content
    /// scrolls under) into that tab's <see cref="TabInsetsProvider"/>, so NavigationRegion's
    /// explicit safe-area contract keeps working inside the tab controller.
    /// </summary>
    private void HookTabPageInsets()
    {
        foreach (var slot in _slots)
        {
            slot.Page.Loaded += (_, _) => MeasureTabPageInsets(slot);
            slot.Page.SizeChanged += (_, _) => MeasureTabPageInsets(slot);
        }
    }

    private static void MeasureTabPageInsets(TabSlot slot)
    {
        if (slot.Page.Handler?.PlatformView is not UIView view)
            return;

        var bottom = (double)view.SafeAreaInsets.Bottom;
        if (bottom > 0)
            slot.Insets.SetBottomOverride(bottom);
    }
#endif

}
#endif
