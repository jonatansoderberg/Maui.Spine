#if IOS || MACCATALYST
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
                if (tabBarController.ViewControllers is not { } vcs)
                    return true;

                var index = Array.IndexOf(vcs, viewController);

                if (ReferenceEquals(tabBarController.SelectedViewController, viewController))
                {
                    OnTabReselected(index);
                    return true;
                }

                return AllowTabTap(index);
            };

#if IOS
            if (_options.Tabs.MinimizeOnScroll && OperatingSystem.IsIOSVersionAtLeast(26))
                controller.TabBarMinimizeBehavior = UITabBarMinimizeBehavior.OnScrollDown;

            HookTabPageInsets();
#endif

            ApplyTabBarItemImages(controller);
            ApplyStyle(controller);

            // Styled from resource keys, the bar has to read them again after every switch.
            _theme.Changed += (_, _) =>
            {
                if (Controller is { } current)
                {
                    ApplyStyle(current);
                    ApplyAllBadges();
                }
            };
        }

        ApplyAllBadges();
    }

    /// <remarks>
    /// The bar slides down out of sight and fades, by its transform alone. UIKit's own
    /// <c>setTabBarHidden</c> takes the bar out of the tab's safe area, which moves the region the
    /// lightbox lies in while it zooms; the region already reaches under the bar.
    /// </remarks>
    partial void PlatformSetTabBarHidden(bool hidden)
    {
#if IOS
        if (Controller?.TabBar is not { } bar)
            return;

        var drop = bar.Frame.Height + (bar.Window?.SafeAreaInsets.Bottom ?? 0);
        if (!hidden)
            bar.Hidden = false;

        var animator = new UIViewPropertyAnimator(0.35, new UISpringTimingParameters(1));
        animator.AddAnimations(() =>
        {
            bar.Transform = hidden ? CoreGraphics.CGAffineTransform.MakeTranslation(0, drop) : CoreGraphics.CGAffineTransform.MakeIdentity();
            bar.Alpha = hidden ? 0 : 1;
        });
        animator.AddCompletion(position =>
        {
            if (position == UIViewAnimatingPosition.End && hidden)
                bar.Hidden = true;
        });
        animator.StartAnimation();
#endif
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

        if (text is not null && _options.Tabs.Style?.BadgeBackground is { } badgeColor)
            controllers[index].TabBarItem.BadgeColor = badgeColor.ToPlatform();
    }

    /// <summary>
    /// Renders each tab's SVG icon and assigns it directly to the <see cref="UITabBarItem"/> as a
    /// template image, so the bar tints it. The loader renders for the screen density and the
    /// image source carries the scale.
    /// </summary>
    /// <remarks>
    /// An item that gets its image after the bar built its views keeps the title-only layout: on
    /// iOS 26 the label of every tab not yet selected sat below the others, with a second label
    /// behind the selected one, until the tab was opened. So the images are set at once, not after
    /// an asynchronous load, and the bar is then made to build its items again.
    /// </remarks>
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
            if (SvgBitmapLoader.LoadFromEmbedded(resolved, 25, 25, Colors.Black) is SvgBitmapImageSource source &&
                TabImage(source) is { } image)
            {
                controllers[i].TabBarItem.Image = image;
            }
        }

        // The bar has built its item views by now, each tab still without an image: the tabs not
        // yet selected keep a title-only layout. Handing it the same controllers again rebuilds
        // them with their images, before the bar is on screen.
        var selected = controller.SelectedIndex;
        controller.SetViewControllers(controllers, false);
        controller.SelectedIndex = selected;
    }

    /// <summary>The rendered bitmap as a template image. The PNG is already in memory, so its stream is read without waiting.</summary>
    private static UIImage? TabImage(SvgBitmapImageSource source)
    {
        using var stream = ((IStreamImageSource)source).GetStreamAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (stream is null)
            return null;

        using var data = NSData.FromStream(stream);
        return data is null
            ? null
            : UIImage.LoadFromData(data, source.Scale)?.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
    }

    private void ApplyStyle(UITabBarController controller)
    {
        if (_options.Tabs.Style is not { } style)
            return;

        var tabBar = controller.TabBar;

        if (style.Selected is { } selected)
            tabBar.TintColor = selected.ToPlatform();

        if (style.Unselected is { } unselected)
            tabBar.UnselectedItemTintColor = unselected.ToPlatform();

        if (style.BarBackground is { } background)
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


    /// <remarks>
    /// The bar's item views are private classes. They are the bar's visible controls, one per tab and place,
    /// in order from the left; if the bar is built some other way, the item gets an equal share of the bar.
    /// </remarks>
    private partial bool TryGetTabItemFrame(int index, out Rect frame)
    {
        frame = default;
        if (Controller?.TabBar is not { Window: { } } bar || bar.Hidden)
            return false;

        var items = new List<UIView>();
        CollectTabItems(bar, items);

        // iOS 26's floating bar draws each button twice, once more for the selected tab's lens; one per place.
        var places = items
            .Select(view => view.ConvertRectToView(view.Bounds, null))
            .DistinctBy(rect => Math.Round(rect.X))
            .OrderBy(rect => rect.X)
            .ToList();
        if (places.Count == _slots.Count)
        {
            var rect = places[index];
            frame = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
            return true;
        }

        var whole = bar.ConvertRectToView(bar.Bounds, null);
        var width = whole.Width / _slots.Count;
        frame = new Rect(whole.X + index * width, whole.Y, width, whole.Height);
        return true;
    }

    private static void CollectTabItems(UIView view, List<UIView> items)
    {
        foreach (var child in view.Subviews)
        {
            if (child.Hidden || child.Alpha <= 0 || child.Bounds.Width <= 0)
                continue;

            if (child is UIControl)
                items.Add(child);
            else
                CollectTabItems(child, items);
        }
    }
}
#endif
