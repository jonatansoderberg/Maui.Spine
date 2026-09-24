using Plugin.Maui.Spine.Services;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The static face of <see cref="IThemeService"/> for controls that subscribe in their constructor,
/// before dependency injection is reachable. Works without <c>UseSpine</c> too: the tracker then
/// follows the application's theme and <see cref="Plugin.Maui.Spine.Common.SpineStrings"/> itself.
/// </summary>
public static class SpineTheme
{
    /// <inheritdoc cref="IThemeService.Version"/>
    public static int Version => ThemeTracker.Version;

    /// <inheritdoc cref="IThemeService.Track"/>
    public static void Track(VisualElement view, Action onChanged) => ThemeTracker.Track(view, onChanged);

    /// <summary>
    /// The app's accent for <paramref name="theme"/>: <see cref="IThemeService.Accent"/> when one is
    /// set, otherwise the app's own colour resources (<c>Primary</c>, and <c>PrimaryDark</c> in dark
    /// mode when there is one, or the keys configured in <see cref="SpineThemeOptions"/>).
    /// <see langword="null"/> when the app declares no accent; a control then uses its own default.
    /// </summary>
    /// <remarks>
    /// A control that paints with it repaints through <see cref="Track"/>, which runs after an
    /// accent change as well as after a theme change.
    /// </remarks>
    public static Color? GetAccent(AppTheme theme)
    {
        var resources = Application.Current?.Resources;
        var options = ThemeService.Instance?.Options;
        var lightKey = options is null ? "Primary" : options.AccentLightKey;
        var darkKey = options is null ? "PrimaryDark" : options.AccentDarkKey;

        return (theme == AppTheme.Dark ? ThemeService.ReadColor(resources, darkKey) : null)
            ?? ThemeService.ReadColor(resources, lightKey);
    }
}
