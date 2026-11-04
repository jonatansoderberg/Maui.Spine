using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>What an attribute's literal is read as.</summary>
internal enum Kind
{
    Number,
    Int,
    Bool,
    Text,
    Color,
    Thickness,
    FontSize,
    Spacing,
    Duration,
    Date,
    Values,
    Icon,
    Enum,
    Condition,
    Name,
    Signature,
    Uri,
    Background,
    Any,
}

internal sealed class Attr(string name, Kind kind, string doc, string? @enum = null)
{
    public string Name { get; } = name;
    public Kind Kind { get; } = kind;
    public string Doc { get; } = doc;
    public string? Enum { get; } = @enum;
}

/// <summary>
/// Every element and attribute a layout file may use: what the generator checks names against (SPL001, SPL002) and
/// what the XSD in the package is written from.
/// </summary>
internal static class Vocabulary
{
    public const string Namespace = "https://schemas.cosmomedia.se/spine/layout";

    public static readonly Dictionary<string, string[]> Enums = new()
    {
        ["WidgetLayoutOptions"] = ["Start", "Center", "End", "Fill"],
        ["WidgetAlignment"] = ["Start", "Center", "End", "Baseline", "TopStart", "Top", "TopEnd", "BottomStart", "Bottom", "BottomEnd"],
        ["WidgetTextAlignment"] = ["Start", "Center", "End"],
        ["WidgetFontAttributes"] = ["None", "Bold", "Italic"],
        ["TextDesign"] = ["Default", "Rounded", "Monospaced", "Serif"],
        ["TextRole"] = ["Body", "Headline", "Title", "Caption", "LargeTitle"],
        ["ElapsedFormat"] = ["Precise", "Clock", "Minutes", "Ago"],
        ["ActivityPart"] = ["Subject", "Value", "Symbol", "Detail", "Time", "Graph", "Progress"],
        ["WidgetPlatform"] = ["iOS", "Android"],
        ["WidgetPlace"] = ["HomeScreen", "LockScreen", "Island", "Notification", "StatusChip", "CarPlay", "Watch"],
        ["WidgetSize"] = ["Small", "Medium", "Large", "ExtraLarge", "LockRectangle", "LockCircle", "LockLine"],
        ["LiveUpdatePresentation"] = ["Automatic", "Template", "Layout"],
        ["WidgetGradientDirection"] = ["Vertical", "Horizontal", "Diagonal"],
        ["LayoutPayload"] = ["Tree", "Template"],
    };

    public static readonly HashSet<string> FlagEnums = ["WidgetFontAttributes", "WidgetPlatform", "WidgetPlace"];

    /// <summary>The semantic colors, as <c>WidgetColor</c> has them.</summary>
    public static readonly string[] Colors = ["Primary", "Secondary", "Accent", "Surface", "OnAccent", "Green", "Red", "Orange", "Yellow", "Blue"];

    /// <summary>The widget sizes by their names in a file, and the <c>WidgetFamily</c> each is.</summary>
    public static readonly Dictionary<string, string> Families = new()
    {
        ["Small"] = "Small",
        ["Medium"] = "Medium",
        ["Large"] = "Large",
        ["ExtraLarge"] = "ExtraLarge",
        ["LockRectangle"] = "AccessoryRectangular",
        ["LockCircle"] = "AccessoryCircular",
        ["LockLine"] = "AccessoryInline",
    };

    /// <summary>The Dynamic Island's regions by their names in a file, and the <c>LiveActivityLayout</c> property each fills.</summary>
    public static readonly Dictionary<string, string> Island = new()
    {
        ["Leading"] = "ExpandedLeading",
        ["Trailing"] = "ExpandedTrailing",
        ["Center"] = "ExpandedCenter",
        ["Bottom"] = "ExpandedBottom",
        ["CompactLeading"] = "CompactLeading",
        ["CompactTrailing"] = "CompactTrailing",
        ["Minimal"] = "Minimal",
    };

    static readonly Attr[] Node =
    [
        new("If", Kind.Condition, "Shown only when the bound bool is true, or the bound value is present."),
        new("IfNot", Kind.Condition, "Shown only when the bound bool is false, or the bound value is absent."),
        new("Margin", Kind.Thickness, "Space around the node: one number, horizontal,vertical, or left,top,right,bottom."),
        new("HorizontalOptions", Kind.Enum, "Where the node sits across its VStack.", "WidgetLayoutOptions"),
        new("Hidden", Kind.Bool, "Leaves the node out (usually inside On)."),
        new("Part", Kind.Enum, "What the node is in a Live Activity.", "ActivityPart"),
        new("Description", Kind.Text, "What a screen reader says for the node."),
        new("Decorative", Kind.Bool, "Leaves the node out of what a screen reader says."),
        new("Pending", Kind.Bool, "Dimmed from a tap until the rebuild after it (iOS)."),
        new("Accented", Kind.Bool, "In the accent group in iOS's tinted widgets."),
        new("Shared", Kind.Name, "In LiveActivity.Shared: the name a region uses the node by."),
    ];

    static readonly Attr[] Stack =
    [
        new("Spacing", Kind.Spacing, "Points between the children, or Evenly (HStack)."),
        new("Padding", Kind.Thickness, "Space inside the stack: one number, horizontal,vertical, or left,top,right,bottom."),
        new("Alignment", Kind.Enum, "How the children line up across the stack.", "WidgetAlignment"),
        new("Background", Kind.Color, "The color behind the stack."),
        new("CornerRadius", Kind.Number, "Rounds the stack's corners."),
        new("Expand", Kind.Bool, "Takes an equal share of the parent's axis."),
        new("Fill", Kind.Bool, "The same as Expand."),
    ];

    static readonly Attr[] TextLike =
    [
        new("FontSize", Kind.FontSize, "Points, or the type scale: Body, Headline, Title, Caption, LargeTitle."),
        new("FontAttributes", Kind.Enum, "Bold, Italic or both.", "WidgetFontAttributes"),
        new("FontDesign", Kind.Enum, "The system font's design.", "TextDesign"),
        new("TextColor", Kind.Color, "The text's color."),
        new("HorizontalTextAlignment", Kind.Enum, "How the lines are aligned in the text's width.", "WidgetTextAlignment"),
        new("MaxLines", Kind.Int, "At most this many lines."),
        new("ShrinkToFit", Kind.Number, "Shrinks to this share of the size before it is cut off (iOS)."),
    ];

    public static readonly Dictionary<string, Attr[]> Elements = new()
    {
        ["VStack"] = [.. Node, .. Stack],
        ["HStack"] = [.. Node, .. Stack],
        ["ZStack"] = [.. Node, .. Stack],
        ["Label"] = [.. Node, .. TextLike, new("Text", Kind.Text, "The text.")],
        ["Elapsed"] = [.. Node, .. TextLike,
            new("Since", Kind.Date, "The moment it counts from."),
            new("Format", Kind.Enum, "How it is written.", "ElapsedFormat"),
            new("Prefix", Kind.Text, "Text in front of the time.")],
        ["Countdown"] = [.. Node, .. TextLike,
            new("Until", Kind.Date, "The moment it reaches zero."),
            new("Prefix", Kind.Text, "Text in front of the time.")],
        ["Icon"] = [.. Node,
            new("Source", Kind.Icon, "The SVG: one of Spine's icons by name (ArrowUpRight) or the app's by file name (drop)."),
            new("Values", Kind.Text, "Every icon a bound Source can name, comma-separated, so each is stored at start."),
            new("Tint", Kind.Color, "The icon's color."),
            new("Size", Kind.Number, "Points square."),
            new("AltText", Kind.Text, "The icon as text where only text fits.")],
        ["Image"] = [.. Node,
            new("Source", Kind.Text, "A stored asset's id."),
            new("Height", Kind.Number, "Points high."),
            new("FullColor", Kind.Bool, "Keeps its colors in iOS's tinted widgets.")],
        ["Progress"] = [.. Node,
            new("Value", Kind.Number, "0 to 1."),
            new("Tint", Kind.Color, "The bar's color.")],
        ["Ring"] = [.. Node,
            new("Value", Kind.Number, "0 to 1, or absent for unknown."),
            new("Tint", Kind.Color, "The ring's color."),
            new("Size", Kind.Number, "Points square.")],
        ["Sparkline"] = [.. Node,
            new("Values", Kind.Values, "The points: times and values with From and To, or values alone."),
            new("From", Kind.Date, "The time at the left edge."),
            new("To", Kind.Date, "The time at the right edge."),
            new("Step", Kind.Duration, "The time between two values: 5m, 30s, 1h."),
            new("Tint", Kind.Color, "The line's color."),
            new("Color", Kind.Color, "The same as Tint."),
            new("Height", Kind.Number, "Points high."),
            new("Area", Kind.Bool, "A soft fill under the line."),
            new("Dot", Kind.Bool, "A dot on the last value."),
            new("BandLow", Kind.Number, "The target band's low edge."),
            new("BandHigh", Kind.Number, "The target band's high edge."),
            new("AboveColor", Kind.Color, "The line over the band."),
            new("BelowColor", Kind.Color, "The line under the band."),
            new("Shaded", Kind.Bool, "Draws the band faintly behind the line."),
            new("Minimum", Kind.Number, "The value at the bottom edge."),
            new("Maximum", Kind.Number, "The value at the top edge."),
            new("MaxAspect", Kind.Number, "No wider than this times its height.")],
        ["Spacer"] = Node,
        ["Divider"] = Node,
        ["Button"] = [.. Node, new("Action", Kind.Name, "The action id the handler gets; End ends a Live Activity.")],
        ["Use"] = [.. Node.Where(a => a.Name is "If" or "IfNot"),
            new("Shared", Kind.Name, "The shared node's name."),
            new("Height", Kind.Number, "Its height here.")],
        ["Choose"] = [.. Node.Where(a => a.Name is "If" or "IfNot" or "Shared")],
        ["When"] = [.. Node.Where(a => a.Name is "If" or "IfNot")],
        ["Otherwise"] = [],
        ["ForEach"] = [
            new("Items", Kind.Any, "The bound list."),
            new("Max", Kind.Int, "At most this many items.")],
        ["ForEach.Empty"] = [],
        ["Content"] = [],
        ["Template"] = [
            new("Name", Kind.Name, "The element name it is used by."),
            new("Parameters", Kind.Signature, "Its parameters as C#: double Size = 40, WidgetColor? Tint = null.")],
        ["On"] = [
            new("Platform", Kind.Enum, "iOS, Android.", "WidgetPlatform"),
            new("Surface", Kind.Enum, "Where it is drawn.", "WidgetPlace"),
            new("Size", Kind.Enum, "The widget's size.", "WidgetSize"),
            new("MinVersion", Kind.Int, "The lowest major OS version.")],
        ["LiveActivity"] = [
            new("Model", Kind.Name, "The model type's full name."),
            new("Class", Kind.Name, "The generated class's full name; the file's name and Layout by default."),
            new("Background", Kind.Color, "The color behind the Lock Screen presentation (iOS)."),
            new("SystemBackground", Kind.Bool, "The system's material behind the Lock Screen presentation (iOS)."),
            new("ActionColor", Kind.Color, "The color of the buttons iOS puts on the activity."),
            new("Link", Kind.Uri, "The URL the app is opened with."),
            new("Payload", Kind.Enum, "Template also writes Template and Data(model), so a push can carry the values alone.", "LayoutPayload")],
        ["LiveActivity.Shared"] = [],
        ["LiveActivity.Island"] = [],
        ["LiveActivity.Small"] = [],
        ["LiveActivity.Notification"] = [
            new("Title", Kind.Text, "The notification's title."),
            new("Text", Kind.Text, "Its text."),
            new("Chip", Kind.Text, "The status-bar chip's text."),
            new("Icon", Kind.Text, "A stored asset as its large icon."),
            new("Presentation", Kind.Enum, "Template or Layout.", "LiveUpdatePresentation")],
        ["Widget"] = [
            new("Model", Kind.Name, "The model type's full name."),
            new("Class", Kind.Name, "The generated class's full name; the file's name and Layout by default."),
            new("Background", Kind.Background, "The widget's surface: a color or {LinearGradient …}."),
            new("BackgroundImage", Kind.Text, "A stored picture over the background, scaled to fill and cropped.")],
        ["Resources"] = [],
    };

    static Vocabulary()
    {
        foreach (var region in Island.Keys)
            Elements["Island." + region] = [];
        foreach (var size in Families.Keys)
            Elements["Widget." + size] = [];
    }

    public static readonly HashSet<string> Nodes =
        ["VStack", "HStack", "ZStack", "Label", "Elapsed", "Countdown", "Icon", "Image", "Progress", "Ring", "Sparkline", "Spacer", "Divider", "Button", "Use", "Choose"];

    public static readonly HashSet<string> Stacks = ["VStack", "HStack", "ZStack"];

    public static readonly HashSet<string> Texts = ["Label", "Elapsed", "Countdown"];

    public static Attr? Find(string element, string attribute) =>
        Elements.TryGetValue(element, out var attrs) ? attrs.FirstOrDefault(a => a.Name == attribute) : null;

    /// <summary>The XSD shipped in the package for completion in editors, written from the tables above.</summary>
    public static string Xsd()
    {
        var xsd = new StringBuilder();
        xsd.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        xsd.Append("<!-- Spine's layout files (*.layout.xml, *.resources.xml). Generated from the generator's vocabulary; do not edit. -->\n");
        xsd.Append($"<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" targetNamespace=\"{Namespace}\" xmlns=\"{Namespace}\" elementFormDefault=\"qualified\">\n");
        foreach (var element in Elements.OrderBy(e => e.Key, System.StringComparer.Ordinal))
        {
            if (Nodes.Contains(element.Key) || element.Key is "ForEach" or "Content")
                continue;
            if (element.Key.Contains('.'))
                continue;
            Element(xsd, element.Key, element.Value);
        }
        foreach (var name in Nodes.Concat(["ForEach", "Content"]).OrderBy(n => n, System.StringComparer.Ordinal))
            Element(xsd, name, Elements[name]);
        foreach (var e in Enums.OrderBy(e => e.Key, System.StringComparer.Ordinal))
        {
            xsd.Append($"  <xs:simpleType name=\"{e.Key}\">\n    <xs:union memberTypes=\"xs:string\">\n      <xs:simpleType>\n        <xs:restriction base=\"xs:string\">\n");
            foreach (var value in e.Value)
                xsd.Append($"          <xs:enumeration value=\"{value}\" />\n");
            xsd.Append("        </xs:restriction>\n      </xs:simpleType>\n    </xs:union>\n  </xs:simpleType>\n");
        }
        xsd.Append("</xs:schema>\n");
        return xsd.ToString();
    }

    static void Element(StringBuilder xsd, string name, Attr[] attrs)
    {
        xsd.Append($"  <xs:element name=\"{name}\">\n    <xs:complexType mixed=\"true\">\n");
        xsd.Append("      <xs:sequence>\n        <xs:any namespace=\"##targetNamespace\" processContents=\"lax\" minOccurs=\"0\" maxOccurs=\"unbounded\" />\n      </xs:sequence>\n");
        foreach (var attr in attrs)
        {
            var type = attr.Enum ?? "xs:string";
            xsd.Append($"      <xs:attribute name=\"{attr.Name}\" type=\"{type}\">\n        <xs:annotation><xs:documentation>{Escape(attr.Doc)}</xs:documentation></xs:annotation>\n      </xs:attribute>\n");
        }
        xsd.Append("      <xs:anyAttribute processContents=\"skip\" />\n    </xs:complexType>\n  </xs:element>\n");
    }

    static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
