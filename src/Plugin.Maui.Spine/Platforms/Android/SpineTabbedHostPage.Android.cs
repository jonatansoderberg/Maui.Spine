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

            ApplyStyle(bottomNav);
        }

        ApplyAllBadges();
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
    /// Wires edge-to-edge inset management for every tab page and zeroes the per-tab bottom
    /// inset — on Android the opaque Material bar owns the bottom edge, so regions must not
    /// pad for the system navigation bar underneath it.
    /// </summary>
    internal void InitializeEdgeToEdgeInsets(SystemInsetsProvider insetsProvider)
    {
        foreach (var slot in _slots)
        {
            slot.Insets.SetBottomOverride(0);
            slot.Page.InitializeEdgeToEdgeInsets(insetsProvider);
        }
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
