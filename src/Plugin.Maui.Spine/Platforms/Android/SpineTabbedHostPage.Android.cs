using Android.Views;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

public partial class SpineTabbedHostPage
{
    private BottomNavigationView? _bottomNav;
    private bool _watchingTheme;

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

        WatchTheme();

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

    /// <summary>
    /// Re-themes the native bar when the system switches between light and dark.
    /// </summary>
    /// <remarks>
    /// The MAUI activity declares <c>UiMode</c> among its <c>ConfigurationChanges</c>, so Android
    /// does not recreate it and nothing re-inflates the bar. Its Material colours were resolved
    /// from the theme when it was created and stay resolved — the page turns light while the bar
    /// keeps the previous theme's surface until the app is restarted (#22).
    ///
    /// Subscribed once and never unsubscribed: the host lives as long as the window does, and a
    /// bar that stops following the theme halfway through a session is the defect again.
    /// </remarks>
    private void WatchTheme()
    {
        if (_watchingTheme || Application.Current is not { } app)
            return;

        _watchingTheme = true;
        app.RequestedThemeChanged += (_, _) => Dispatcher.Dispatch(ReapplyBarAppearance);
    }

    private void ReapplyBarAppearance()
    {
        if (_bottomNav is not { IsAttachedToWindow: true } bottomNav)
            return;

        ApplyThemeColors(bottomNav);

        // Spine's own overrides go on top of the theme, in the same order as at attach.
        ApplyStyle(bottomNav);
        ApplyAllBadges();
    }

    /// <summary>
    /// Reads the bar's surface and item tints out of the activity's theme as it now stands.
    /// </summary>
    /// <remarks>
    /// The theme itself does follow the configuration change; it is only the colours already
    /// resolved into the view that are stale. Resolving them again is therefore enough, and it
    /// keeps the bar on whatever the app's Material theme says rather than on a colour Spine
    /// picked for it.
    /// </remarks>
    private static void ApplyThemeColors(BottomNavigationView bottomNav)
    {
        if (bottomNav.Context is not { Theme: { } theme } context)
            return;

        // Material 3 puts the bar on colorSurfaceContainer; older themes only have colorSurface.
        var surface = Resolve(context, theme, "colorSurfaceContainer")
            ?? Resolve(context, theme, "colorSurface");

        if (surface is { } background)
            bottomNav.SetBackgroundColor(new Android.Graphics.Color(background));

        if (Resolve(context, theme, "colorOnSurface") is { } selected
            && Resolve(context, theme, "colorOnSurfaceVariant") is { } unselected)
        {
            var states = new[]
            {
                new[] { Android.Resource.Attribute.StateChecked },
                new[] { -Android.Resource.Attribute.StateChecked },
            };

            var stateList = new Android.Content.Res.ColorStateList(states, [selected, unselected]);

            bottomNav.ItemIconTintList = stateList;
            bottomNav.ItemTextColor = stateList;
        }
    }

    /// <summary>
    /// Looks a theme attribute up by name. The Material binding does not surface its
    /// <c>Resource.Attribute</c> constants, and a name lookup resolves against whatever theme
    /// the app actually uses rather than against a constant that may not exist in it.
    /// </summary>
    private static int? Resolve(Android.Content.Context context, Android.Content.Res.Resources.Theme theme, string attribute)
    {
        var id = context.Resources?.GetIdentifier(attribute, "attr", context.PackageName) ?? 0;

        if (id == 0)
            return null;

        var value = new Android.Util.TypedValue();

        return theme.ResolveAttribute(id, value, true) ? value.Data : null;
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
