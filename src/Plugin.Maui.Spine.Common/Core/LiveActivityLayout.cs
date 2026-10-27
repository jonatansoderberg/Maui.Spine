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

    /// <summary>
    /// What Android's Live Update should say, when the tree is not a good source for it. Left
    /// <see langword="null"/>, the notification is derived from the tree as before.
    /// </summary>
    public LiveUpdateText? Android { get; init; }

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
    /// The small activity family: CarPlay's Dashboard on iOS 26 and the Smart Stack on Apple Watch, both iOS 18
    /// and later. Rendered only when the project sets <c>SpineWidgetsLiveActivitySmall</c>, which declares the
    /// family in the widget extension; without it those surfaces show <see cref="CompactLeading"/> and
    /// <see cref="CompactTrailing"/> side by side. The space is small, room for a row or two of what matters
    /// most. Android has no such surface and ignores it.
    /// </summary>
    public WidgetNode? Small { get; init; }

    /// <summary>
    /// Nodes several regions show, by name, sent once: a region shows one with <see cref="W.Use(string, double?)"/>,
    /// and sets its own height there. A Live Activity push has 4 KB, and a graph that the Lock Screen, the
    /// Dynamic Island and CarPlay each repeat takes a fifth of it. A shared node cannot use another. The app's
    /// widget extension must be built with a Spine that reads them (0.1.34 or later): an older one draws nothing
    /// where a node is used, so a server keeps sending the full form to apps that are not updated.
    /// </summary>
    public IReadOnlyDictionary<string, WidgetNode>? Shared { get; init; }

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

    /// <summary>Every region's tree, then every shared node.</summary>
    internal IEnumerable<WidgetNode?> Trees =>
        [LockScreen, ExpandedLeading, ExpandedTrailing, ExpandedCenter, ExpandedBottom, CompactLeading, CompactTrailing, Minimal, Small,
            .. Shared?.Values ?? []];

    /// <summary>
    /// The layout as the renderer's JSON — what a server puts in the <c>content-state.layout</c> of a
    /// Live Activity push, so a backend can build layouts with <see cref="W"/> instead of by hand.
    /// </summary>
    public string ToJson() => Serialization.WidgetJson.Serialize(this);
}

/// <summary>
/// The Android Live Update's own words. A tree laid out for the Dynamic Island — three columns, a
/// logo each side, the score between — has no line that reads well as a notification title, and no
/// <c>W.Icon</c> to be its icon, so the app says what it wants instead.
/// </summary>
/// <param name="Title">The notification's title.</param>
/// <param name="Body">Its text, under the title.</param>
/// <param name="Chip">The short text beside the status-bar icon while the Live Update runs.</param>
/// <param name="Icon">A stored asset drawn beside the text, as the notification's large icon.</param>
public sealed record LiveUpdateText(string? Title = null, string? Body = null, string? Chip = null, string? Icon = null);
