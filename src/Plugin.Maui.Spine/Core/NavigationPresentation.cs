namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Discriminated union that identifies how a navigable page is presented.
/// Use the static singletons <see cref="RegionPresentation"/> and <see cref="SheetPresentation"/>
/// rather than instantiating the nested types directly.
/// </summary>
public abstract record NavigationPresentation
{
    private NavigationPresentation() { }

    /// <summary>Presentation variant used for full-screen region (stack) navigation.</summary>
    public sealed record Region : NavigationPresentation;

    /// <summary>Presentation variant used for bottom-sheet modal navigation.</summary>
    public sealed record Sheet : NavigationPresentation;

    /// <summary>Singleton instance representing region (full-screen stack) presentation.</summary>
    public static readonly NavigationPresentation RegionPresentation = new Region();

    /// <summary>Singleton instance representing sheet (bottom-sheet modal) presentation.</summary>
    public static readonly NavigationPresentation SheetPresentation = new Sheet();
}
