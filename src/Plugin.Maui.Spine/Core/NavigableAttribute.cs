namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Controls the visual treatment applied to the page behind a bottom sheet while it is open.
/// </summary>
public enum BackgroundPageOverlay
{
    /// <summary>No overlay is applied; the background page remains fully visible.</summary>
    None,

    /// <summary>A semi-transparent dark scrim is drawn over the background page.</summary>
    Dimmed,

    /// <summary>The background page is blurred. Availability depends on the platform.</summary>
    Blurred,
}

/// <summary>
/// Base attribute for declaring a page as navigable by Spine.
/// Apply the concrete <see cref="NavigableRegionAttribute"/> or <see cref="NavigableSheetAttribute"/>
/// subclass to your page class rather than using this base directly.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public abstract class NavigableAttribute : Attribute
{
    /// <summary>Initializes a new attribute with the specified <paramref name="presentation"/> style.</summary>
    /// <param name="presentation">The navigation presentation variant for this page.</param>
    protected NavigableAttribute(NavigationPresentation presentation)
    {
        Presentation = presentation;
    }

    // Copy-with-defaults constructor: resolves each property from the source attribute
    // unless it was never explicitly set, in which case the SpineOptions default is used.
    /// <summary>
    /// Copy constructor that merges <paramref name="source"/> with global <paramref name="defaults"/>.
    /// Properties not explicitly set on <paramref name="source"/> are replaced with the corresponding default.
    /// </summary>
    protected NavigableAttribute(NavigableAttribute source, NavigableDefaults defaults, NavigationPresentation presentation)
        : this(presentation)
    {
        Lifetime = source.Lifetime;
        Title = source.Title;
        TitlePlacement = source.TitlePlacementSet ? source.TitlePlacement : defaults.TitlePlacement;
        TitleAlignment = source.TitleAlignmentSet ? source.TitleAlignment : defaults.TitleAlignment;
        IsHeaderBarVisible = source.IsHeaderBarVisibleSet ? source.IsHeaderBarVisible : defaults.IsHeaderBarVisible;
        IsBackButtonVisible = source.IsBackButtonVisibleSet ? source.IsBackButtonVisible : defaults.IsBackButtonVisible;
    }

    /// <summary>
    /// DI lifetime used when registering the page and its ViewModel.
    /// Defaults to <see cref="ServiceLifetime.Transient"/> so each navigation creates a fresh instance.
    /// Use <see cref="ServiceLifetime.Singleton"/> to retain state across navigations.
    /// </summary>
    public ServiceLifetime Lifetime { get; init; } = ServiceLifetime.Transient;

    /// <summary>The presentation style resolved from the concrete attribute subclass.</summary>
    public NavigationPresentation Presentation { get; }

    /// <summary>
    /// Text displayed as the page title in the header bar or title bar.
    /// When empty the title area is not populated.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    private bool _titlePlacementSet;
    /// <summary>
    /// Where the <see cref="Title"/> is rendered.
    /// When not set the value is inherited from <see cref="SpineOptions.RegionDefaultsConfig"/> or
    /// <see cref="SpineOptions.SheetDefaultsConfig"/>.
    /// </summary>
    public TitlePlacement TitlePlacement { get => field; set { field = value; _titlePlacementSet = true; } }
    internal bool TitlePlacementSet => _titlePlacementSet;

    private bool _titleAlignmentSet;
    /// <summary>
    /// Horizontal alignment of the title text within the header bar.
    /// When not set the value is inherited from the relevant <c>DefaultsConfig</c>.
    /// </summary>
    public TitleAlignment TitleAlignment { get => field; set { field = value; _titleAlignmentSet = true; } }
    internal bool TitleAlignmentSet => _titleAlignmentSet;

    private bool _isHeaderBarVisibleSet;
    /// <summary>
    /// Whether Spine's in-page header bar is shown for this page.
    /// When not set the value is inherited from the relevant <c>DefaultsConfig</c>.
    /// </summary>
    public bool IsHeaderBarVisible { get => field; set { field = value; _isHeaderBarVisibleSet = true; } }
    internal bool IsHeaderBarVisibleSet => _isHeaderBarVisibleSet;

    private bool _isBackButtonVisibleSet;
    /// <summary>
    /// Whether the back button is shown in the header bar for this page.
    /// When not set the value is inherited from the relevant <c>DefaultsConfig</c>.
    /// </summary>
    public bool IsBackButtonVisible { get => field; set { field = value; _isBackButtonVisibleSet = true; } }
    internal bool IsBackButtonVisibleSet => _isBackButtonVisibleSet;
}

/// <summary>
/// Marks a page as a full-screen region page that participates in Spine's stack navigation.
/// Apply this attribute to a class that derives from <see cref="SpinePage{TViewModel}"/>.
/// </summary>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Home")]
/// public partial class HomePage { public HomePage() => InitializeComponent(); }
/// </code>
/// </example>
public sealed class NavigableRegionAttribute : NavigableAttribute
{
    /// <summary>Initializes a new <see cref="NavigableRegionAttribute"/> with default settings.</summary>
    public NavigableRegionAttribute()
        : base(NavigationPresentation.RegionPresentation) { }

    private NavigableRegionAttribute(NavigableRegionAttribute source, SpineOptions.RegionDefaultsConfig defaults)
        : base(source, defaults, NavigationPresentation.RegionPresentation)
    {
        IsTitleBarVisible = source.IsTitleBarVisibleSet ? source.IsTitleBarVisible : defaults.IsTitleBarVisible;
        SafeAreaEdges = source.SafeAreaEdgesSet ? source.SafeAreaEdges : defaults.SafeAreaEdges;
    }

    internal NavigableRegionAttribute WithDefaults(SpineOptions.RegionDefaultsConfig defaults) => new(this, defaults);

    private bool _isTitleBarVisibleSet;
    /// <summary>
    /// Whether the native window title bar is shown when this page is active (desktop only).
    /// When not set the value is inherited from <see cref="SpineOptions.RegionDefaultsConfig.IsTitleBarVisible"/>.
    /// </summary>
    public bool IsTitleBarVisible { get => field; set { field = value; _isTitleBarVisibleSet = true; } }
    internal bool IsTitleBarVisibleSet => _isTitleBarVisibleSet;

    private bool _safeAreaEdgesSet;
    /// <summary>
    /// The edges on which Spine applies system-bar padding (safe area) for this page.
    /// Edges included here are padded by the content host — the page content stops at the system bar.
    /// Edges excluded cause the content to render edge-to-edge behind that bar; use
    /// <see cref="Plugin.Maui.Spine.Core.ViewModelBase.SafeAreaInsets"/> to offset your content manually.
    /// Defaults to <see cref="SafeAreaEdges.All"/> (all bars padded).
    /// When not set the value is inherited from <see cref="SpineOptions.RegionDefaultsConfig.SafeAreaEdges"/>.
    /// </summary>
    public SafeAreaEdges SafeAreaEdges { get => field; set { field = value; _safeAreaEdgesSet = true; } }
    internal bool SafeAreaEdgesSet => _safeAreaEdgesSet;
}

/// <summary>
/// Marks a page as a bottom-sheet modal that Spine presents using the platform sheet API.
/// Apply this attribute to a class that derives from <see cref="SpinePage{TViewModel}"/>.
/// </summary>
/// <example>
/// <code>
/// [NavigableSheet(
///     Title = "Options",
///     BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
///     AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
/// public partial class OptionsSheet { public OptionsSheet() => InitializeComponent(); }
/// </code>
/// </example>
public sealed class NavigableSheetAttribute : NavigableAttribute
{
    /// <summary>Initializes a new <see cref="NavigableSheetAttribute"/> with default settings.</summary>
    public NavigableSheetAttribute()
        : base(NavigationPresentation.SheetPresentation) { }

    private NavigableSheetAttribute(NavigableSheetAttribute source, SpineOptions.SheetDefaultsConfig defaults)
        : base(source, defaults, NavigationPresentation.SheetPresentation)
    {
        BackgroundPageOverlay = source.BackgroundPageOverlaySet ? source.BackgroundPageOverlay : defaults.BackgroundPageOverlay;
        SafeAreaEdges = source.SafeAreaEdgesSet ? source.SafeAreaEdges : defaults.SafeAreaEdges;
        InitialDetent = source.InitialDetent;
        AllowedDetents = source.AllowedDetents;
    }

    internal NavigableSheetAttribute WithDefaults(SpineOptions.SheetDefaultsConfig defaults) => new(this, defaults);

    /// <summary>
    /// The detent the sheet snaps to when it first opens.
    /// Use one of the <see cref="SheetDetent"/> string constants, a percentage string such as <c>"50%"</c>,
    /// or an absolute pixel string such as <c>"300px"</c>.
    /// When empty the first entry in <see cref="AllowedDetents"/> is used.
    /// </summary>
    public string InitialDetent { get; init; } = string.Empty;

    /// <summary>
    /// The set of detents the user can snap the sheet to.
    /// Each entry follows the same format as <see cref="InitialDetent"/>.
    /// Use the <see cref="SheetDetent"/> string constants for the built-in sizes:
    /// <see cref="SheetDetent.Compact"/>, <see cref="SheetDetent.Medium"/>,
    /// <see cref="SheetDetent.Expanded"/>, <see cref="SheetDetent.FullScreen"/>.
    /// </summary>
    public string[] AllowedDetents { get; init; } = [];

    private bool _backgroundPageOverlaySet;
    /// <summary>
    /// Visual treatment applied to the page behind the sheet while it is open.
    /// When not set the value is inherited from <see cref="SpineOptions.SheetDefaultsConfig.BackgroundPageOverlay"/>.
    /// </summary>
    public BackgroundPageOverlay BackgroundPageOverlay { get => field; set { field = value; _backgroundPageOverlaySet = true; } }
    internal bool BackgroundPageOverlaySet => _backgroundPageOverlaySet;

    private bool _safeAreaEdgesSet;
    /// <summary>
    /// The edges on which Spine applies system-bar padding (safe area) for this sheet page.
    /// Edges included here are padded by the content host; excluded edges cause the content to
    /// render edge-to-edge behind that bar — use
    /// <see cref="Plugin.Maui.Spine.Core.ViewModelBase.SafeAreaInsets"/> to offset your content manually.
    /// When not set the value is inherited from <see cref="SpineOptions.SheetDefaultsConfig.SafeAreaEdges"/>.
    /// </summary>
    public SafeAreaEdges SafeAreaEdges { get => field; set { field = value; _safeAreaEdgesSet = true; } }
    internal bool SafeAreaEdgesSet => _safeAreaEdgesSet;
}