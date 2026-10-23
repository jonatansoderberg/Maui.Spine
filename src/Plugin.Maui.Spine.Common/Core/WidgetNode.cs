using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// One node in the platform-neutral view tree that a widget or Live Activity is rendered from.
/// Build trees with <see cref="W"/>; the tree is serialized and interpreted by the native renderer
/// of each platform, so the vocabulary is deliberately small.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(VStackNode), "vstack")]
[JsonDerivedType(typeof(HStackNode), "hstack")]
[JsonDerivedType(typeof(ZStackNode), "zstack")]
[JsonDerivedType(typeof(TextNode), "text")]
[JsonDerivedType(typeof(TimerNode), "timer")]
[JsonDerivedType(typeof(RelativeDateNode), "relative")]
[JsonDerivedType(typeof(IconNode), "image")]
[JsonDerivedType(typeof(ImageNode), "asset")]
[JsonDerivedType(typeof(ProgressNode), "progress")]
[JsonDerivedType(typeof(RingNode), "ring")]
[JsonDerivedType(typeof(SparklineNode), "sparkline")]
[JsonDerivedType(typeof(SpacerNode), "spacer")]
[JsonDerivedType(typeof(DividerNode), "divider")]
[JsonDerivedType(typeof(ButtonNode), "button")]
[JsonDerivedType(typeof(AdaptiveNode), "adaptive")]
public abstract record WidgetNode
{
    /// <summary>
    /// Dimmed by the platform from a tap on any <see cref="W.Button"/> in the widget until the
    /// rebuild that follows the handler, so what the tap is about to change is seen to be changing.
    /// iOS only; Android renders the node as usual. Ignored on a button and inside its child. Set
    /// with <see cref="WidgetNodeStyling.Pending{T}(T)"/>.
    /// </summary>
    public bool? Pending { get; init; }

    /// <summary>
    /// Puts the node in the accent group of a widget drawn in the accented rendering mode — the Tinted and
    /// Clear Home Screen appearances. iOS 26 draws that group white, as it does the rest, so on iOS it is a
    /// grouping rather than a color; it changes nothing in full color. Android ignores it. Set with
    /// <see cref="WidgetNodeStyling.Accented"/>.
    /// </summary>
    public bool? Accented { get; init; }
}

/// <summary>A container that lays its <see cref="Children"/> out along one axis.</summary>
public abstract record StackNode : WidgetNode
{
    /// <summary>Distance between children in points; the renderer's default when <see langword="null"/>.</summary>
    public double? Spacing { get; init; }

    /// <summary>
    /// Space in points between the stack's edges and its children, on every side. Set with
    /// <see cref="WidgetNodeStyling.Padding"/>.
    /// </summary>
    public double? Padding { get; init; }

    /// <summary>
    /// The color behind the stack, padding included. A stack with a background fills the width it is
    /// offered, except inside an <see cref="HStackNode"/>, where it wraps its content. Set with
    /// <see cref="WidgetNodeStyling.Background"/>.
    /// </summary>
    public WidgetColor? Background { get; init; }

    /// <summary>
    /// The radius in points the stack's corners are rounded by, clipping its background and children.
    /// Android 12 and later; square below. Set with <see cref="WidgetNodeStyling.CornerRadius"/>.
    /// </summary>
    public double? CornerRadius { get; init; }

    /// <summary>
    /// Whether the stack takes an equal share of the space along its parent's axis, rather than the
    /// width of its content. Set with <see cref="WidgetNodeStyling.Fill"/>.
    /// </summary>
    [JsonPropertyName("fill")]
    public bool? Fill { get; init; }

    /// <summary>
    /// <c>"center"</c> centres a vertical stack's children on its axis instead of aligning them to the leading edge. Set
    /// with <see cref="WidgetNodeStyling.CenterItems"/>.
    /// </summary>
    [JsonPropertyName("align")]
    public string? Align { get; init; }

    /// <summary>
    /// <c>"evenly"</c> spreads a horizontal stack's children with equal space at its ends and between them, the
    /// children at their own width. Set with <see cref="WidgetNodeStyling.Evenly"/>.
    /// </summary>
    [JsonPropertyName("distribute")]
    public string? Distribute { get; init; }

    /// <summary>The children in layout order.</summary>
    public IReadOnlyList<WidgetNode> Children { get; init; } = [];
}

/// <summary>Children stacked top to bottom, leading-aligned.</summary>
public sealed record VStackNode : StackNode;

/// <summary>Children laid out leading to trailing, vertically centered.</summary>
public sealed record HStackNode : StackNode;

/// <summary>Children drawn on top of each other, centered.</summary>
public sealed record ZStackNode : StackNode;

/// <summary>Typography and color shared by every text-like node.</summary>
/// <param name="Role">Which of the platform's text styles to use.</param>
/// <param name="Bold">Whether to render in bold weight.</param>
/// <param name="Color">The foreground color; the platform's primary text color when <see langword="null"/>.</param>
public readonly record struct TextStyle(TextRole Role = TextRole.Body, bool Bold = false, WidgetColor? Color = null);

/// <summary>Base for nodes that render as text and accept <see cref="TextStyle"/>.</summary>
public abstract record TextLikeNode : WidgetNode
{
    /// <summary>The style to render with.</summary>
    [JsonIgnore]
    public TextStyle Style { get; init; }

    // The three below are flattened views of Style, and each has an init accessor so a tree can be
    // read back from its own JSON. Spine.PushNotifications needs that on Android, where a Live Update arrives as
    // serialized layout in a data message and is rendered in the app's process. Without the setters
    // the structure came back but every style fell to its default, silently.

    /// <summary>The text role, exposed flat for serialization.</summary>
    [JsonPropertyName("font")]
    public TextRole Role { get => Style.Role; init => Style = Style with { Role = value }; }

    /// <summary>Bold weight, exposed flat for serialization.</summary>
    [JsonPropertyName("bold")]
    public bool Bold { get => Style.Bold; init => Style = Style with { Bold = value }; }

    /// <summary>Foreground color, exposed flat for serialization.</summary>
    [JsonPropertyName("color")]
    public WidgetColor? Color { get => Style.Color; init => Style = Style with { Color = value }; }

    /// <summary>
    /// Centres the lines of the text on each other, and a timer's or relative date's text in the width
    /// it takes; see <see cref="WidgetNodeStyling.Centered{T}"/>.
    /// </summary>
    [JsonPropertyName("centered")]
    public bool? IsCentered { get; init; }

    /// <summary>Returns a copy of this node with <paramref name="style"/> applied.</summary>
    public abstract TextLikeNode WithStyle(TextStyle style);
}

/// <summary>A static run of text.</summary>
/// <param name="Text">The text to show.</param>
public sealed record TextNode(string Text) : TextLikeNode
{
    /// <inheritdoc />
    public override TextLikeNode WithStyle(TextStyle style) => this with { Style = style };
}

/// <summary>
/// A countdown to <see cref="Until"/> that the platform re-renders every second without the
/// app running. Use it for anything that must tick; a <see cref="TextNode"/> computed by the app
/// stands still until the next refresh.
/// </summary>
/// <param name="Until">The moment the countdown reaches zero.</param>
public sealed record TimerNode(DateTimeOffset Until) : TextLikeNode
{
    /// <summary>Text drawn in front of the time as part of the same text: <c>"Nedsläpp om "</c>.</summary>
    public string? Prefix { get; init; }

    /// <inheritdoc />
    public override TextLikeNode WithStyle(TextStyle style) => this with { Style = style };
}

/// <summary>
/// The age of <see cref="Date"/> as relative text ("3 min ago"), re-rendered by the platform without
/// the app running. Pair it with data that may go stale so the widget never claims to be current.
/// </summary>
/// <param name="Date">The moment the relative text counts from.</param>
public sealed record RelativeDateNode(DateTimeOffset Date) : TextLikeNode
{
    /// <summary>
    /// Elapsed time as a clock — <c>18:35</c> rather than <c>18 min, 35 secs</c>. The long form is
    /// too wide for the Dynamic Island's compact presentation, where the whole island grows to fit
    /// its widest region and the other one is left with a gap.
    /// </summary>
    public bool? Compact { get; init; }

    /// <summary>Text drawn in front of the time as part of the same text: <c>"Uppdaterad "</c>.</summary>
    public string? Prefix { get; init; }

    /// <inheritdoc />
    public override TextLikeNode WithStyle(TextStyle style) => this with { Style = style };
}

/// <summary>A platform symbol, addressed by its SF Symbols name on Apple platforms.</summary>
/// <param name="SystemName">The symbol name, e.g. <c>figure.run</c>.</param>
public sealed record IconNode([property: JsonPropertyName("systemImage")] string SystemName) : WidgetNode
{
    /// <summary>Tint color; the platform's primary color when <see langword="null"/>.</summary>
    public WidgetColor? Color { get; init; }
}

/// <summary>A bitmap the app has placed in the shared container under <see cref="AssetId"/>.</summary>
/// <param name="AssetId">File name inside the shared container's <c>spine-widgets/assets</c> directory.</param>
public sealed record ImageNode([property: JsonPropertyName("asset")] string AssetId) : WidgetNode
{
    /// <summary>
    /// Keeps the image's own colors in the Tinted and Clear appearances, where iOS otherwise draws it solid
    /// white. iOS 18 and later. Set with <see cref="WidgetNodeStyling.FullColor"/>.
    /// </summary>
    public bool? FullColor { get; init; }

    /// <summary>Requested height in points; intrinsic size when <see langword="null"/>.</summary>
    public double? Height { get; init; }
}

/// <summary>A linear progress bar.</summary>
/// <param name="Value">Progress in the range 0 to 1.</param>
public sealed record ProgressNode(double Value) : WidgetNode
{
    /// <summary>Tint color; the platform accent when <see langword="null"/>.</summary>
    public WidgetColor? Color { get; init; }
}

/// <summary>
/// A share as a ring: a faint track all round, the value clockwise from 12 o'clock with round ends. Null draws only the
/// track (unknown). For a number in the middle, put the ring and a text in a <see cref="ZStackNode"/>.
/// </summary>
/// <param name="Value">The share, 0 to 1, or <see langword="null"/> for unknown.</param>
public sealed record RingNode(double? Value) : WidgetNode
{
    /// <summary>The value's colour; the track is the same colour, faint. The platform accent when <see langword="null"/>.</summary>
    public WidgetColor? Color { get; init; }

    /// <summary>The ring's diameter in points.</summary>
    [JsonPropertyName("height")]
    public double Size { get; init; } = 56;

    /// <summary>The ring's width in points; a tenth of the diameter when <see langword="null"/>.</summary>
    public double? Thickness { get; init; }
}

/// <summary>
/// A line through <see cref="Values"/>, drawn by the platform. A server can push one inside a Live
/// Activity, which a stored picture cannot travel in. It is drawn smooth, without overshooting a point.
/// The vertical scale fits the values with a little room above and below. The node fills the width it
/// is offered on iOS; on Android it is a picture <see cref="Aspect"/> times as wide as it is high. Build it
/// with <see cref="W.Sparkline(IEnumerable{ValueTuple{DateTimeOffset, double}}, DateTimeOffset, DateTimeOffset, TimeSpan?, WidgetColor?, double)"/>.
/// </summary>
/// <param name="Values">
/// The values in time order, <see langword="null"/> where there is none. A short run of them is bridged; a run of more
/// than a tenth of the values (at least two) breaks the line. With
/// <see cref="From"/> and <see cref="Step"/> value <c>i</c> is at <c>From + i × Step</c>; without them the values
/// are spread evenly across the width.
/// </param>
public sealed record SparklineNode(IReadOnlyList<double?> Values) : WidgetNode
{
    /// <summary>The time at the left edge, and of the first value.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>The time at the right edge. A line that ends before it leaves the rest of the width empty.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>The time between two values, in seconds.</summary>
    public double? Step { get; init; }

    /// <summary>The line's colour, and inside the band its colour there; the platform accent when <see langword="null"/>.</summary>
    public WidgetColor? Color { get; init; }

    /// <summary>The lower edge of the target band, below which the line takes <see cref="BelowColor"/>.</summary>
    public double? Low { get; init; }

    /// <summary>The upper edge of the target band.</summary>
    public double? High { get; init; }

    /// <summary>The line's colour above <see cref="High"/>; <see cref="Color"/> when <see langword="null"/>.</summary>
    public WidgetColor? AboveColor { get; init; }

    /// <summary>The line's colour below <see cref="Low"/>; <see cref="Color"/> when <see langword="null"/>.</summary>
    public WidgetColor? BelowColor { get; init; }

    /// <summary>
    /// The band drawn faintly behind the line. Without it the graph stays relative: the band only colours the line,
    /// and the scale fits the values alone either way.
    /// </summary>
    public bool? Shade { get; init; }

    /// <summary>A soft fill under the line, in the line's colour at each point, fading out downwards.</summary>
    public bool? Area { get; init; }

    /// <summary>A dot on the last value, in its colour.</summary>
    public bool? Dot { get; init; }

    /// <summary>The height in points.</summary>
    public double Height { get; init; } = 48;

    /// <summary>Android: the picture's width as a multiple of <see cref="Height"/>; 3 when <see langword="null"/>.</summary>
    public double? Aspect { get; init; }
}

/// <summary>Flexible space that pushes siblings apart.</summary>
public sealed record SpacerNode : WidgetNode;

/// <summary>A thin separator line.</summary>
public sealed record DividerNode : WidgetNode;

/// <summary>
/// A tappable <see cref="Child"/> that sends <see cref="ActionId"/> to the provider's
/// <see cref="IWidgetActionHandler"/>. The handler runs at once in the app's process on both platforms;
/// an app that is not running is launched in the background for it.
/// </summary>
/// <param name="ActionId">What the tap means to the provider, e.g. <c>next</c>.</param>
/// <param name="Child">What the button looks like.</param>
public sealed record ButtonNode(string ActionId, WidgetNode Child) : WidgetNode;

/// <summary>
/// A node that renders a different subtree per family, for the parts of a tree that vary while the rest
/// is shared. <see cref="Trees"/> is keyed by the family's JSON name; families without an entry, and
/// surfaces without a family such as Live Activity regions, render <see cref="Fallback"/>.
/// </summary>
/// <param name="Fallback">The subtree for every family not in <see cref="Trees"/>.</param>
/// <param name="Trees">The subtree per family name.</param>
public sealed record AdaptiveNode(WidgetNode Fallback, IReadOnlyDictionary<string, WidgetNode> Trees) : WidgetNode;

/// <summary>The text styles a <see cref="TextLikeNode"/> can take, mapped to each platform's type ramp.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TextRole>))]
public enum TextRole
{
    /// <summary>Regular body text.</summary>
    Body,
    /// <summary>Emphasized text for section names and labels.</summary>
    Headline,
    /// <summary>Large text for the primary value.</summary>
    Title,
    /// <summary>Small secondary text.</summary>
    Caption,
}
