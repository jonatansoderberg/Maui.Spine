using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// What a control shows: a title, an icon and, for a toggle, whether it is on. Built with
/// <see cref="Toggle"/> or <see cref="Button"/>; the rest is set with <c>with</c>.
/// </summary>
public sealed record ControlState
{
    /// <summary>The control's name: the tile's label on Android, the title under the control on iOS.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// The icon by name, as for <see cref="W.Icon"/>: an SVG (<c>Bell</c>, <c>Plus</c>). Android draws it as
    /// the tile's mask; iOS draws only symbols in a control, so it draws the custom symbol of that name, which
    /// Spine builds into the extension for its own icons and the spine-symbols tool makes for the app's.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>The SF Symbol iOS draws, when it is not named like <see cref="Icon"/> (an SVG <c>unlock</c>, the symbol <c>lock.open</c>).</summary>
    [Obsolete("Icons are SVGs on both platforms: Spine's icons are built into the iOS extension as custom symbols, and the spine-symbols tool makes the app's own. Name the icon in Icon.")]
    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    /// <summary>Whether a toggle is on; <see langword="null"/> makes the control a button.</summary>
    [JsonPropertyName("isOn")]
    public bool? IsOn { get; init; }

    /// <summary>
    /// The second line: the tile's subtitle on Android 10+, the value under the title on iOS. Leave it
    /// <see langword="null"/> on a toggle and both platforms say On or Off themselves.
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The color of a toggle that is on (iOS). Android uses the system's accent.</summary>
    [JsonPropertyName("tint")]
    public WidgetColor? Tint { get; init; }

    /// <summary>A toggle: the platform flips it at once when tapped, and <see cref="IControlProvider.OnActionAsync"/> gets the new value.</summary>
    public static ControlState Toggle(string title, bool isOn, string icon, string? status = null) =>
        new() { Title = title, IsOn = isOn, Icon = icon, Status = status };

    /// <summary>A button: a tap runs <see cref="IControlProvider.OnActionAsync"/> with <see cref="ControlAction.IsOn"/> <see langword="null"/>.</summary>
    public static ControlState Button(string title, string icon, string? status = null) =>
        new() { Title = title, Icon = icon, Status = status };
}
