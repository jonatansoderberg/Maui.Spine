using Android.Views;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

public partial class SpineTabbedHostPage
{
    private BottomNavigationView? _bottomNav;

    partial void PlatformAttach()
    {
        if (_bottomNav is null or { IsAttachedToWindow: false })
        {
            var root = Platform.CurrentActivity?.Window?.DecorView as ViewGroup;
            var bottomNav = root is null ? null : FindBottomNavigationView(root);

            if (bottomNav is null)
                return;

            _bottomNav = bottomNav;
            bottomNav.ItemReselected += OnItemReselected;

            // The bar has no height until it has been measured, and it remeasures when the
            // window insets change (gesture bar, three-button navigation, rotation).
            bottomNav.LayoutChange += OnBottomNavLayoutChange;

            ApplyStyle(bottomNav);
            ApplyTabBarInset();
        }

        ApplyAllBadges();
    }

    private void OnBottomNavLayoutChange(object? sender, Android.Views.View.LayoutChangeEventArgs e) =>
        ApplyTabBarInset();

    /// <summary>
    /// Reports the Material bar's height to every tab as its bottom safe-area inset.
    /// </summary>
    /// <remarks>
    /// Tab pages render edge-to-edge — <see cref="SpineTabPage"/> zeroes their native padding
    /// and consumes the window insets — so the page's content area runs the full height of the
    /// window, underneath the bar. The bar is opaque and draws over that area, which makes its
    /// height exactly the padding a region owes at the bottom. The bar applies the system
    /// navigation inset to itself, so its measured height already includes it and must not be
    /// added twice.
    /// </remarks>
    private void ApplyTabBarInset()
    {
        if (_bottomNav is not { } bottomNav)
            return;

        var density = bottomNav.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        double bottom = density > 0 ? bottomNav.Height / density : 0;

        foreach (var slot in _slots)
            slot.Insets.SetBottomOverride(bottom);
    }

    partial void PlatformApplyBadge(int index, string? text)
    {
        if (_bottomNav?.Menu is not { } menu || index >= menu.Size())
            return;

        var itemId = menu.GetItem(index)!.ItemId;

        if (text is null)
        {
            _bottomNav.RemoveBadge(itemId);
            return;
        }

        var badge = _bottomNav.GetOrCreateBadge(itemId);

        // An empty string renders the bare Material dot badge.
        if (text.Length == 0)
            badge.ClearText();
        else
            badge.Text = text;

        if (_options.Tabs.Style?.BadgeBackgroundColor is { } badgeBackground)
            badge.BackgroundColor = badgeBackground.ToPlatform().ToArgb();

        if (_options.Tabs.Style?.BadgeTextColor is { } badgeText)
            badge.BadgeTextColor = badgeText.ToPlatform().ToArgb();
    }

    private void OnItemReselected(object? sender, Google.Android.Material.Navigation.NavigationBarView.ItemReselectedEventArgs e)
    {
        if (_bottomNav?.Menu is not { } menu)
            return;

        for (var i = 0; i < menu.Size(); i++)
        {
            if (menu.GetItem(i)!.ItemId == e.Item.ItemId)
            {
                OnTabReselected(i);
                return;
            }
        }
    }

    private void ApplyStyle(BottomNavigationView bottomNav)
    {
        if (_options.Tabs.Style is not { } style)
            return;

        if (style is { SelectedColor: { } selected, UnselectedColor: { } unselected })
        {
            var states = new[]
            {
                new[] { Android.Resource.Attribute.StateChecked },
                new[] { -Android.Resource.Attribute.StateChecked },
            };
            var colors = new[] { selected.ToPlatform().ToArgb(), unselected.ToPlatform().ToArgb() };
            var stateList = new Android.Content.Res.ColorStateList(states, colors);

            bottomNav.ItemIconTintList = stateList;
            bottomNav.ItemTextColor = stateList;
        }
        else if (style.SelectedColor is { } selectedOnly)
        {
            bottomNav.ItemActiveIndicatorColor = Android.Content.Res.ColorStateList.ValueOf(
                selectedOnly.ToPlatform());
        }

        if (style.BarBackgroundColor is { } background)
            bottomNav.SetBackgroundColor(background.ToPlatform());
    }

    /// <summary>
    /// Wires edge-to-edge inset management for every tab page and replaces the window's bottom
    /// inset with the native tab bar's height — inside the tab host it is the bar, not the
    /// system navigation bar, that content has to clear.
    /// </summary>
    internal void InitializeEdgeToEdgeInsets(SystemInsetsProvider insetsProvider)
    {
        foreach (var slot in _slots)
        {
            // Start at zero rather than the window inset: the system navigation bar sits
            // behind the tab bar, so reporting it would pad content by the wrong amount until
            // the bar has been measured.
            slot.Insets.SetBottomOverride(0);
            slot.Page.InitializeEdgeToEdgeInsets(insetsProvider);
        }

        ApplyTabBarInset();
    }

    private static BottomNavigationView? FindBottomNavigationView(ViewGroup root)
    {
        for (var i = 0; i < root.ChildCount; i++)
        {
            switch (root.GetChildAt(i))
            {
                case BottomNavigationView bottomNav:
                    return bottomNav;
                case ViewGroup group when FindBottomNavigationView(group) is { } nested:
                    return nested;
            }
        }

        return null;
    }
}
