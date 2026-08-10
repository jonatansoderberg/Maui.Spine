using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Abstraction over the window-root page of a Spine app. Implemented by
/// <see cref="SpineHostPage"/> (no tabs) and <see cref="SpineTabbedHostPage"/> (one or more
/// <see cref="NavigableTabAttribute"/> pages discovered). <see cref="Services.NavigationService"/>
/// and the platform hooks operate on this interface so both hosts share all navigation mechanics.
/// </summary>
internal interface ISpineHost
{
    /// <summary>The MAUI page acting as the window root.</summary>
    Page HostPage { get; }

    /// <summary>
    /// The primary region that receives region-page navigation: the single root region on
    /// <see cref="SpineHostPage"/>, the <em>active tab's</em> region on <see cref="SpineTabbedHostPage"/>.
    /// </summary>
    NavigationRegion RootNavigationRegion { get; }

    /// <summary>The region hosting pages inside bottom sheets. Active only while a sheet is open.</summary>
    NavigationRegion SheetNavigationRegion { get; }

    /// <summary>
    /// The region view model currently receiving navigation commands: the sheet region's while a
    /// sheet is open, otherwise <see cref="RootNavigationRegion"/>'s.
    /// </summary>
    NavigationRegionViewModel ActiveRegionViewModel { get; }

    /// <summary>Raised when <see cref="RootNavigationRegion"/> changes (tab switch). Never raised by <see cref="SpineHostPage"/>.</summary>
    event Action? ActiveRegionChanged;

    /// <summary>
    /// Whether a system back press at the active region's root can still be handled by the host
    /// (tab host with <see cref="TabRootBackBehavior.SwitchToFirstTab"/> while not on the first tab).
    /// </summary>
    bool CanHandleRootBack { get; }

    /// <summary>Handles a system back press at the active region's root. Returns <see langword="false"/> when the app should be left.</summary>
    bool TryHandleRootBack();
}

/// <summary>
/// Holds the currently-active <see cref="ISpineHost"/>. Exists so the host can be swapped at
/// runtime (<c>SetRootAsync</c> to a non-tab page replaces the tab host with a plain root host,
/// e.g. logout → login) while <see cref="Services.NavigationService"/> keeps a stable dependency.
/// </summary>
internal sealed class SpineHostProvider
{
    /// <summary>The active host. Set during startup resolution and on host swaps.</summary>
    public ISpineHost? Current { get; private set; }

    /// <summary>Makes <paramref name="host"/> the active host.</summary>
    public void SetCurrent(ISpineHost host) => Current = host;
}
