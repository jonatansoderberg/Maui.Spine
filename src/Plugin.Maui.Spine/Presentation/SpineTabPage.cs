namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Thin <see cref="ContentPage"/> wrapper around one tab's <see cref="NavigationRegion"/>.
/// Exists only because the native tab host requires a <see cref="Page"/> per tab — all
/// navigation, header-bar, and safe-area behavior lives in the region it hosts.
/// </summary>
public sealed partial class SpineTabPage : ContentPage
{
    /// <summary>The navigation region owning this tab's stack.</summary>
    internal NavigationRegion Region { get; }

    internal SpineTabPage(NavigationRegion region, string title)
    {
        // Same contract as SpineHostPage: Spine owns the safe-area geometry explicitly.
        this.SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None;

        Title = title;
        Content = Region = region;
    }
}
