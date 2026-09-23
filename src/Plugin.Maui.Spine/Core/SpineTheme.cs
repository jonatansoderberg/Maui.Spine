using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The static face of <see cref="IThemeService"/> for controls that subscribe in their constructor,
/// before dependency injection is reachable.
/// </summary>
public static class SpineTheme
{
    /// <inheritdoc cref="IThemeService.Version"/>
    public static int Version => ThemeTracker.Version;

    /// <inheritdoc cref="IThemeService.Track"/>
    public static void Track(VisualElement view, Action onChanged) => ThemeTracker.Track(view, onChanged);
}
