using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// The trees a Live Activity renders in each of its regions. Regions left <see langword="null"/>
/// render empty. On iOS the regions map to the Lock Screen banner and the Dynamic Island; other
/// platforms pick the regions their surface can show.
/// </summary>
public sealed record LiveActivityLayout
{
    /// <summary>The Lock Screen banner, and the notification-style presentation on devices without a Dynamic Island.</summary>
    public WidgetNode? LockScreen { get; init; }

    /// <summary>Leading region of the expanded Dynamic Island.</summary>
    public WidgetNode? ExpandedLeading { get; init; }

    /// <summary>Trailing region of the expanded Dynamic Island.</summary>
    public WidgetNode? ExpandedTrailing { get; init; }

    /// <summary>Center region of the expanded Dynamic Island, below the camera cutout.</summary>
    public WidgetNode? ExpandedCenter { get; init; }

    /// <summary>Bottom region of the expanded Dynamic Island, full width.</summary>
    public WidgetNode? ExpandedBottom { get; init; }

    /// <summary>Leading side of the compact Dynamic Island.</summary>
    public WidgetNode? CompactLeading { get; init; }

    /// <summary>Trailing side of the compact Dynamic Island.</summary>
    public WidgetNode? CompactTrailing { get; init; }

    /// <summary>The minimal Dynamic Island, shown when another activity is also active.</summary>
    public WidgetNode? Minimal { get; init; }

    /// <summary>
    /// The color behind the Lock Screen presentation; a translucent black when <see langword="null"/>.
    /// iOS only: the Dynamic Island is always black, and Android does not promote a Live Update that
    /// asks for a color. A fixed color stays fixed in dark mode, so give the text fixed colors too.
    /// </summary>
    public WidgetColor? Background { get; init; }

    /// <summary>
    /// Draws the Lock Screen presentation on the system's own material, which follows light and dark mode,
    /// instead of the translucent black Spine draws by default. Give the tree semantic colors
    /// (<see cref="WidgetColor.Primary"/> and the like) with it: the material is light in light mode, and
    /// white text disappears on it. <see cref="Background"/> wins when both are set. iOS only.
    /// </summary>
    public bool? SystemBackground { get; init; }

    /// <summary>
    /// The color of the buttons iOS itself puts on the activity; the system's own when <see langword="null"/>.
    /// iOS only, like <see cref="Background"/>.
    /// </summary>
    public WidgetColor? ActionColor { get; init; }

    /// <summary>The URL the app is opened with when the activity is tapped.</summary>
    [JsonIgnore]
    public Uri? Link { get; init; }

    /// <summary>The serialized form of <see cref="Link"/>.</summary>
    [JsonPropertyName("link")]
    public string? LinkValue => Link?.ToString();

    /// <summary>
    /// The layout as the renderer's JSON — what a server puts in the <c>content-state.json</c> of a
    /// Live Activity push, so a backend can build layouts with <see cref="W"/> instead of by hand.
    /// </summary>
    public string ToJson() => Serialization.WidgetJson.Serialize(this);
}
