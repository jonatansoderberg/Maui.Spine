namespace Plugin.Maui.Spine.Widgets;

/// <summary>
/// The builder for widget trees. Every method returns a node, so trees read the way they render:
/// <code>
/// W.VStack(spacing: 6,
///     W.HStack(W.Icon("figure.run", WidgetColor.Green), W.Text("Orientera").Headline().Bold(), W.Spacer()),
///     W.Timer(until: start).Title().Bold())
/// </code>
/// </summary>
public static class W
{
    /// <summary>Children stacked top to bottom.</summary>
    public static VStackNode VStack(params WidgetNode[] children) => new() { Children = children };

    /// <summary>Children stacked top to bottom with <paramref name="spacing"/> points between them.</summary>
    public static VStackNode VStack(double spacing, params WidgetNode[] children) => new() { Spacing = spacing, Children = children };

    /// <summary>Children laid out leading to trailing.</summary>
    public static HStackNode HStack(params WidgetNode[] children) => new() { Children = children };

    /// <summary>Children laid out leading to trailing with <paramref name="spacing"/> points between them.</summary>
    public static HStackNode HStack(double spacing, params WidgetNode[] children) => new() { Spacing = spacing, Children = children };

    /// <summary>Children drawn on top of each other.</summary>
    public static ZStackNode ZStack(params WidgetNode[] children) => new() { Children = children };

    /// <summary>A run of text in the body style.</summary>
    public static TextNode Text(string text) => new(text);

    /// <summary>A countdown that ticks without the app running.</summary>
    public static TimerNode Timer(DateTimeOffset until) => new(until);

    /// <summary>The age of <paramref name="date"/> as relative text that updates without the app running.</summary>
    public static RelativeDateNode Relative(DateTimeOffset date) => new(date);

    /// <summary>A platform symbol by SF Symbols name.</summary>
    public static IconNode Icon(string systemName, WidgetColor? color = null) => new(systemName) { Color = color };

    /// <summary>A bitmap previously stored with <see cref="IWidgetService.StoreAssetAsync"/>.</summary>
    public static ImageNode Image(string assetId, double? height = null) => new(assetId) { Height = height };

    /// <summary>A linear progress bar for <paramref name="value"/> in the range 0 to 1.</summary>
    public static ProgressNode Progress(double value, WidgetColor? color = null) => new(Math.Clamp(value, 0, 1)) { Color = color };

    /// <summary>Flexible space.</summary>
    public static SpacerNode Spacer() => new();

    /// <summary>A separator line.</summary>
    public static DividerNode Divider() => new();
}

/// <summary>Fluent styling for text-like nodes. Each call returns a new node.</summary>
public static class WidgetNodeStyling
{
    /// <summary>Renders in the platform's title style.</summary>
    public static T Title<T>(this T node) where T : TextLikeNode => node.Role(TextRole.Title);

    /// <summary>Renders in the platform's headline style.</summary>
    public static T Headline<T>(this T node) where T : TextLikeNode => node.Role(TextRole.Headline);

    /// <summary>Renders in the platform's body style.</summary>
    public static T Body<T>(this T node) where T : TextLikeNode => node.Role(TextRole.Body);

    /// <summary>Renders in the platform's caption style.</summary>
    public static T Caption<T>(this T node) where T : TextLikeNode => node.Role(TextRole.Caption);

    /// <summary>Renders in bold weight.</summary>
    public static T Bold<T>(this T node) where T : TextLikeNode => (T)node.WithStyle(node.Style with { Bold = true });

    /// <summary>Renders in the platform's secondary text color.</summary>
    public static T Secondary<T>(this T node) where T : TextLikeNode => node.Color(WidgetColor.Secondary);

    /// <summary>Renders in <paramref name="color"/>.</summary>
    public static T Color<T>(this T node, WidgetColor color) where T : TextLikeNode => (T)node.WithStyle(node.Style with { Color = color });

    private static T Role<T>(this T node, TextRole role) where T : TextLikeNode => (T)node.WithStyle(node.Style with { Role = role });
}
