namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Host-level tab bar settings, exposed as <see cref="SpineOptions.Tabs"/>.
/// Tabs themselves are declared by decorating pages with <see cref="NavigableTabAttribute"/>;
/// these options only control behaviour that is not per-page.
/// </summary>
public sealed class SpineTabsOptions
{
    /// <summary>
    /// What the Android system back button does when the active tab's stack is at its root.
    /// Defaults to <see cref="TabRootBackBehavior.SwitchToFirstTab"/> per Android guidelines.
    /// </summary>
    public TabRootBackBehavior RootBackBehavior { get; set; } = TabRootBackBehavior.SwitchToFirstTab;

    /// <summary>
    /// When <see langword="true"/>, the iOS 26 tab bar minimizes while scrolling down
    /// (<c>UITabBarController.tabBarMinimizeBehavior</c>). No effect on other platforms
    /// or earlier iOS versions. Defaults to <see langword="false"/>.
    /// </summary>
    public bool MinimizeOnScroll { get; set; }

    /// <summary>
    /// Optional appearance overrides for the tab bar. When <see langword="null"/> (default)
    /// the untouched native look is used — Liquid Glass on iOS 26, Material on Android —
    /// which is the recommended configuration.
    /// </summary>
    public SpineTabBarStyle? Style { get; set; }
}

/// <summary>
/// Controls what the Android system back button does when the active tab's stack is at its root.
/// </summary>
public enum TabRootBackBehavior
{
    /// <summary>Switch to the first (lowest <see cref="NavigableTabAttribute.Order"/>) tab; leave the app only from there. Android guideline behaviour.</summary>
    SwitchToFirstTab,

    /// <summary>Let the system back press leave the app regardless of the active tab.</summary>
    LeaveApp,
}

/// <summary>
/// Opt-in appearance overrides for the native tab bar. Only non-<see langword="null"/> properties
/// are applied, on top of the platform's default appearance.
/// </summary>
/// <remarks>
/// Setting <see cref="BarBackgroundColor"/> on iOS 26 replaces the Liquid Glass material with a
/// flat color — leave it <see langword="null"/> to keep the native look.
/// </remarks>
public sealed class SpineTabBarStyle
{
    /// <summary>Tint applied to the selected tab's icon and label.</summary>
    public Color? SelectedColor { get; set; }

    /// <summary>Tint applied to unselected tabs' icons and labels.</summary>
    public Color? UnselectedColor { get; set; }

    /// <summary>Badge background color. Platform default (red) when <see langword="null"/>.</summary>
    public Color? BadgeBackgroundColor { get; set; }

    /// <summary>Badge text color. Platform default when <see langword="null"/>.</summary>
    public Color? BadgeTextColor { get; set; }

    /// <summary>
    /// Solid bar background. Discouraged on iOS 26 — forfeits the Liquid Glass material.
    /// </summary>
    public Color? BarBackgroundColor { get; set; }
}
