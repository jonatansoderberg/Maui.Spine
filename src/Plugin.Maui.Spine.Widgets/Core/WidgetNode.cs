using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Widgets;

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
[JsonDerivedType(typeof(SpacerNode), "spacer")]
[JsonDerivedType(typeof(DividerNode), "divider")]
public abstract record WidgetNode;

/// <summary>A container that lays its <see cref="Children"/> out along one axis.</summary>
public abstract record StackNode : WidgetNode
{
    /// <summary>Distance between children in points; the renderer's default when <see langword="null"/>.</summary>
    public double? Spacing { get; init; }

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

    /// <summary>The text role, exposed flat for serialization.</summary>
    [JsonPropertyName("font")]
    public TextRole Role => Style.Role;

    /// <summary>Bold weight, exposed flat for serialization.</summary>
    [JsonPropertyName("bold")]
    public bool Bold => Style.Bold;

    /// <summary>Foreground color, exposed flat for serialization.</summary>
    [JsonPropertyName("color")]
    public WidgetColor? Color => Style.Color;

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

/// <summary>Flexible space that pushes siblings apart.</summary>
public sealed record SpacerNode : WidgetNode;

/// <summary>A thin separator line.</summary>
public sealed record DividerNode : WidgetNode;

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
