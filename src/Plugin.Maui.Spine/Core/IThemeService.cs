namespace Plugin.Maui.Spine.Core;

/// <summary>
/// The app's light/dark theme: the user's choice, what is actually in effect, and a change signal
/// that fires once the palette is fully in place.
/// </summary>
/// <remarks>
/// Set <see cref="Current"/> from a settings page instead of <see cref="Application.UserAppTheme"/>:
/// Spine stores the choice (see <see cref="SpineThemeOptions.Persist"/>) and applies it again at the
/// next launch before the first page is built. With <see cref="SpineThemeOptions.UseTokens{TLight, TDark}"/>
/// the matching token dictionary is merged into the application resources on every change, so
/// <c>{DynamicResource}</c> consumers re-resolve. Controls that assign colours in code use
/// <see cref="Track"/> (or <see cref="SpineTheme.Track"/> from a constructor) to repaint.
/// </remarks>
public interface IThemeService
{
    /// <summary>
    /// The user's choice. <see cref="AppTheme.Unspecified"/> follows the system. Setting it applies
    /// the theme at once and, when persistence is on, remembers it for the next launch.
    /// </summary>
    AppTheme Current { get; set; }

    /// <summary>The theme in effect: <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>, never unspecified.</summary>
    AppTheme Effective { get; }

    /// <summary>
    /// The app-wide accent the user picked, or <see langword="null"/> for the app's own colour
    /// resources. Setting it writes the accent into the resources named by
    /// <see cref="SpineThemeOptions.AccentLightKey"/>, <see cref="SpineThemeOptions.AccentDarkKey"/>,
    /// <see cref="SpineThemeOptions.AccentKey"/> and <see cref="SpineThemeOptions.OnAccentKey"/>
    /// and announces it like a theme change (<see cref="Version"/>, <see cref="Changed"/>,
    /// <see cref="Track"/>). Stored with <see cref="Current"/> and applied again at the next
    /// launch before the first page.
    /// </summary>
    SpineAccent? Accent { get; set; }

    /// <summary>
    /// Bumped on every change, of the theme or of the accent. A control that repaints itself compares the version it painted at
    /// with this one to decide whether it missed a change while detached.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Raised on the UI thread after the theme or the accent changed and the token dictionary has been swapped,
    /// so a handler reads a consistent palette.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Runs <paramref name="onChanged"/> after every theme or accent change while <paramref name="view"/> is
    /// attached to a window, and once when it is attached again if the theme changed meanwhile.
    /// </summary>
    /// <remarks>
    /// The subscription lives exactly as long as the view: Spine holds it weakly and the view holds
    /// it through its own <see cref="Element.HandlerChanged"/> handler. Nothing is unsubscribed
    /// by hand and an abandoned page is collected together with its subscriptions. The callback is
    /// not run at registration; paint the initial state yourself.
    /// </remarks>
    void Track(VisualElement view, Action onChanged);
}
