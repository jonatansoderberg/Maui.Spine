namespace Plugin.Maui.Spine.Core;

/// <summary>Applies a page's <see cref="StatusBarStyle"/> to the window's status bar.</summary>
internal static partial class StatusBar
{
    /// <summary>The style last applied; re-applied on a theme change.</summary>
    internal static StatusBarStyle Current { get; private set; }

    internal static void Apply(StatusBarStyle style)
    {
        Current = style;
        ApplyPlatform(style);
    }

    /// <summary>Re-applies <see cref="Current"/>, for a theme change that would otherwise reset the bar.</summary>
    internal static void Reapply() => ApplyPlatform(Current);

    /// <summary>Whether light content is wanted for <paramref name="style"/> under the current theme.</summary>
    internal static bool WantsLightContent(StatusBarStyle style) => style switch
    {
        StatusBarStyle.LightContent => true,
        StatusBarStyle.DarkContent => false,
        _ => (Application.Current?.RequestedTheme ?? AppTheme.Unspecified) == AppTheme.Dark
            || (Application.Current?.RequestedTheme is AppTheme.Unspecified && Application.Current?.PlatformAppTheme == AppTheme.Dark),
    };

    static partial void ApplyPlatform(StatusBarStyle style);
}
