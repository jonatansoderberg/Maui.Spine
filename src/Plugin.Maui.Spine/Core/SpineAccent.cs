namespace Plugin.Maui.Spine.Core;

/// <summary>
/// An accent colour for light and dark mode, set app-wide through <see cref="IThemeService.Accent"/>.
/// </summary>
/// <param name="Light">The accent in light mode.</param>
/// <param name="Dark">The accent in dark mode, usually a little brighter (Apple's system blue is
/// <c>#007AFF</c> light and <c>#0A84FF</c> dark).</param>
public sealed record SpineAccent(Color Light, Color Dark)
{
    /// <summary>The same colour in light and dark mode.</summary>
    public SpineAccent(Color color) : this(color, color)
    {
    }

    /// <summary>The accent for <paramref name="theme"/>; anything but dark is light.</summary>
    public Color For(AppTheme theme) => theme == AppTheme.Dark ? Dark : Light;

    /// <summary>
    /// Black or white, whichever reads on <paramref name="accent"/>. Leans to white, as the
    /// platforms do on their blue buttons; yellow, orange, green and mint get black.
    /// </summary>
    public static Color TextOn(Color accent) =>
        0.2126 * accent.Red + 0.7152 * accent.Green + 0.0722 * accent.Blue > 0.5 ? Colors.Black : Colors.White;

    internal string Serialize() => $"{Light.ToArgbHex(includeAlpha: true)};{Dark.ToArgbHex(includeAlpha: true)}";

    internal static SpineAccent? Deserialize(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Split(';') is not [var light, var dark])
            return null;

        return Color.TryParse(light, out var lightColor) && Color.TryParse(dark, out var darkColor)
            ? new SpineAccent(lightColor, darkColor)
            : null;
    }
}
