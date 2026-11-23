using System.Globalization;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;
using SkiaSharp;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Turns the tree the app wrote into the Adaptive Card the Windows 11 widget board draws: stacks into containers and
/// column sets, text into text blocks, and everything a card cannot draw — an icon in the app's colour, a ring, a
/// progress bar, a sparkline, the surface — into pictures carried in the card as data URIs. A card names its colours
/// (<c>accent</c>, <c>good</c>, <c>light</c>…), so a text's colour is the nearest name; a colour that differs between
/// light and dark is chosen by the board's theme, <c>$host.hostTheme</c>, when the board expands the card.
/// </summary>
internal sealed class AdaptiveCardRenderer(Func<string, byte[]?> _asset, WindowsWidgetPalette _palette, string _family, BoardArea _area, DateTimeOffset _now)
{
    /// <summary>Pixels per point the pictures are drawn at: the board runs at 150 to 200 % on most screens.</summary>
    private const float Scale = 3;

    private const string Dark = "${$host.hostTheme == 'dark'}";
    private const string Light = "${$host.hostTheme != 'dark'}";

    /// <summary>
    /// The room the board leaves above a card's content (its header) and below it, in points; measured on Windows 11 build
    /// 26300. The card's content is drawn between them, so it would sit lower in the widget than iOS puts it.
    /// </summary>
    private const double CardTop = 41, CardBottom = 5;

    /// <summary>The room at the bottom that centres the card's content in the widget, as iOS centres it between equal margins.</summary>
    private const double HeaderExcess = CardTop - CardBottom;

    /// <summary>The height of a root picture that fills the widget between equal margins.</summary>
    private double Centred => _area.Height - 2 * CardTop;

    /// <summary>The content width in points, for a picture that fills its column (a sparkline); every size is one column wide.</summary>
    private double Width => _area.Width;

    /// <summary>Whether a node counts time, so the card goes stale by the minute while it is on screen.</summary>
    public bool HasClock { get; private set; }

    public JsonObject Card(JsonNode? tree, WidgetSurfaceFields surface, string? link)
    {
        var body = new JsonArray();
        var stretched = false;
        if (tree is JsonObject root)
            foreach (var element in Render(root, null, root: true))
            {
                stretched |= element["height"]?.GetValue<string>() == "stretch";
                body.Add(element);
            }
        // The board's header takes the top of the card, so its content starts lower than it ends; the same room at the
        // bottom centres the content in the widget, as iOS centres it between equal margins.
        if (body.Count > 0)
            body.Add(Gap(HeaderExcess));

        var card = new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["verticalContentAlignment"] = stretched ? "top" : "center",
            // The board sizes a card to its content, which leaves the surface short of the widget's bottom edge.
            ["minHeight"] = Px(_area.Height),
        };
        if (Background(surface) is { } background) card["backgroundImage"] = background;
        if (link is not null) card["selectAction"] = new JsonObject { ["type"] = "Action.Execute", ["verb"] = WidgetBoardProvider.OpenVerb + link };
        return card;
    }

    private JsonObject? Background(WidgetSurfaceFields surface)
    {
        var picture = surface.Image is { } id ? _asset(id) : null;
        var gradient = surface.Gradient?.Select(_palette.Resolve).ToList();
        var color = surface.Color is { } c ? _palette.Resolve(c) : ((SKColor, SKColor)?)null;
        var aspect = _area.Width / _area.Height;

        byte[]? Draw(bool dark) => WidgetPictures.Surface(color is { } pair ? (dark ? pair.Item2 : pair.Item1) : null,
            gradient?.Select(g => dark ? g.Dark : g.Light).ToList(), surface.Direction, picture, aspect);

        var light = Draw(false);
        if (light is null) return null;
        var themed = color is { } p && p.Item1 != p.Item2 || gradient?.Any(g => g.Light != g.Dark) == true;
        var url = themed && Draw(true) is { } dark
            ? $"${{if($host.hostTheme == 'dark', '{DataUri(dark)}', '{DataUri(light)}')}}"
            : DataUri(light);
        return new JsonObject { ["url"] = url, ["fillMode"] = "cover", ["horizontalAlignment"] = "center", ["verticalAlignment"] = "center" };
    }

    /// <summary>The elements of one node: usually one, two where a picture differs between the board's themes.</summary>
    private List<JsonObject> Render(JsonObject node, string? hAlign, bool root = false)
    {
        switch (Str(node, "type"))
        {
            case "vstack": return [VStack(node, root)];
            case "hstack": return [HStack(node)];
            case "zstack": return ZStack(node, hAlign, root);
            case "text":
                return [Text(node, Str(node, "text") ?? "", hAlign)];
            case "timer":
                HasClock = true;
                return [Text(node, (Str(node, "prefix") ?? "") + Duration(Date(node, "until") - _now, up: true), hAlign)];
            case "relative":
                HasClock = true;
                return [Text(node, (Str(node, "prefix") ?? "") + Duration(_now - Date(node, "date"), up: false), hAlign)];
            case "image":
            {
                var size = Num(node, "height") ?? 20;
                var asset = IconNode.AssetName(Str(node, "systemImage") ?? "", Str(node, "variant"));
                if (_asset(WidgetIconAssets.AssetId(asset)) is not { } white) return [];
                return Themed(Str(node, "color") ?? "primary", color => WidgetPictures.Tint(white, color), url => Image(node, url, $"{Px(size)}", $"{Px(size)}", hAlign));
            }
            case "asset":
            {
                if (Str(node, "asset") is not { } id || _asset(id) is not { } bytes) return [];
                var image = Image(node, DataUri(WidgetPictures.Shrink(bytes, 640)), null, null, hAlign ?? (root ? "center" : null));
                if (Num(node, "height") is { } height) image["height"] = Px(height);
                else if (root) image["height"] = Px(Centred);
                return [image];
            }
            case "ring":
            {
                var size = Num(node, "height") ?? 56;
                return Themed(Str(node, "color") ?? "accent",
                    color => WidgetPictures.Ring((int)(size * Scale), Num(node, "value"), Num(node, "thickness"), color, Scale),
                    url => Image(node, url, Px(size), Px(size), hAlign));
            }
            case "progress":
                return Themed(Str(node, "color") ?? "accent", color => WidgetPictures.Progress(Num(node, "value") ?? 0, color),
                    url => Stretch(Image(node, url, null, null, hAlign)));
            case "sparkline":
            {
                var height = Num(node, "height") ?? 48;
                var width = Math.Min(Width, height * (Num(node, "maxAspect") ?? double.MaxValue));
                double?[] values = node["values"] is JsonArray array ? [.. array.Select(v => v is JsonValue n && n.TryGetValue<double>(out var d) ? d : (double?)null)] : [];
                var line = Str(node, "color") ?? "accent";
                var (lineLight, lineDark) = _palette.Resolve(line);
                var (aboveLight, aboveDark) = Str(node, "aboveColor") is { } a ? _palette.Resolve(a) : (lineLight, lineDark);
                var (belowLight, belowDark) = Str(node, "belowColor") is { } b ? _palette.Resolve(b) : (lineLight, lineDark);
                byte[] Draw(bool dark) => WidgetPictures.Sparkline(values, Num(node, "min"), Num(node, "max"), Num(node, "low"), Num(node, "high"),
                    dark ? lineDark : lineLight, dark ? aboveDark : aboveLight, dark ? belowDark : belowLight,
                    Bool(node, "area"), Bool(node, "dot"), (int)(width * Scale), (int)(height * Scale), Scale);
                var light = Image(node, DataUri(Draw(false)), Px(width), Px(height), hAlign);
                if (lineLight == lineDark && aboveLight == aboveDark && belowLight == belowDark) return [light];
                light["$when"] = Light;
                var dark = Image(node, DataUri(Draw(true)), Px(width), Px(height), hAlign);
                dark["$when"] = Dark;
                return [light, dark];
            }
            case "adaptive":
            {
                var chosen = node["trees"] is JsonObject trees && trees[_family] is JsonObject tree ? tree : node["fallback"] as JsonObject;
                return chosen is null ? [] : Render(chosen, hAlign, root);
            }
            case "button":
            {
                if (node["child"] is not JsonObject child) return [];
                var items = new JsonArray([.. Render(child, hAlign)]);
                var button = new JsonObject { ["type"] = "Container", ["items"] = items };
                if (Str(node, "actionId") is { } actionId)
                    button["selectAction"] = new JsonObject { ["type"] = "Action.Execute", ["verb"] = WidgetBoardProvider.ButtonVerb + actionId };
                return [button];
            }
            default: return [];
        }
    }

    private JsonObject VStack(JsonObject node, bool root)
    {
        var align = HAlign(Str(node, "align"));
        var items = new JsonArray();
        var spacing = Spacing(Num(node, "spacing") ?? 4);
        var (first, separator, stretched) = (true, false, false);
        foreach (var child in Children(node))
        {
            switch (Str(child, "type"))
            {
                case "divider":
                    separator = true;
                    continue;
                case "spacer":
                    items.Add(new JsonObject { ["type"] = "Container", ["height"] = "stretch", ["items"] = new JsonArray() });
                    stretched = true;
                    first = false;
                    continue;
            }
            var own = Str(child, "hAlign") switch { "center" => "center", "end" => "right", "start" => "left", _ => align };
            foreach (var element in Render(child, own))
            {
                if (!first) element["spacing"] = spacing;
                if (separator) element["separator"] = true;
                if (Str(child, "hAlign") == "fill" && element["type"]?.GetValue<string>() == "Image") Stretch(element);
                items.Add(element);
            }
            (first, separator) = (false, false);
        }
        var container = new JsonObject { ["type"] = "Container", ["items"] = items };
        if (stretched || root && items.Any(i => i?["height"]?.GetValue<string>() == "stretch")) container["height"] = "stretch";
        return Box(container, node);
    }

    private JsonObject HStack(JsonObject node)
    {
        var valign = Str(node, "align") switch { "start" or "baseline" => "top", "end" => "bottom", _ => "center" };
        var columns = new JsonArray();
        var spacing = Spacing(Num(node, "spacing") ?? 4);
        var evenly = Str(node, "distribute") == "evenly";
        JsonObject Filler() => new() { ["type"] = "Column", ["width"] = "stretch", ["items"] = new JsonArray() };

        var (first, separator) = (true, false);
        foreach (var child in Children(node))
        {
            var type = Str(child, "type");
            if (type == "divider") { separator = true; continue; }
            if (evenly) columns.Add(Filler());
            var column = type == "spacer"
                ? Filler()
                : new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = Bool(child, "fill") ? "stretch" : "auto",
                    ["verticalContentAlignment"] = valign,
                    ["items"] = new JsonArray([.. Render(child, null)]),
                };
            if (!first && !evenly) column["spacing"] = spacing;
            if (separator) column["separator"] = true;
            columns.Add(column);
            (first, separator) = (false, false);
        }
        if (evenly) columns.Add(Filler());

        var set = new JsonObject { ["type"] = "ColumnSet", ["columns"] = columns };
        // A column set takes no picture behind it; a box goes on a container around it.
        return HasBox(node) ? Box(new JsonObject { ["type"] = "Container", ["items"] = new JsonArray(set) }, node) : set;
    }

    /// <summary>
    /// A card has no layers. A ZStack whose bottom layer is a stored picture becomes a container with that picture
    /// behind the rest, which covers a label on a photo; any other is drawn top to bottom.
    /// </summary>
    private List<JsonObject> ZStack(JsonObject node, string? hAlign, bool root)
    {
        var layers = Children(node).ToList();
        if (layers.Count == 0) return [];
        var align = Str(node, "align") ?? "center";
        var valign = align.StartsWith("top", StringComparison.Ordinal) ? "top" : align.StartsWith("bottom", StringComparison.Ordinal) ? "bottom" : "center";
        var halign = align.EndsWith("Start", StringComparison.Ordinal) || align == "start" ? "left"
            : align.EndsWith("End", StringComparison.Ordinal) || align == "end" ? "right" : "center";

        var container = new JsonObject { ["type"] = "Container", ["verticalContentAlignment"] = valign };
        var rest = layers;
        if (layers.Count > 1 && Str(layers[0], "type") == "asset" && Str(layers[0], "asset") is { } id && _asset(id) is { } bytes)
        {
            container["backgroundImage"] = new JsonObject { ["url"] = DataUri(WidgetPictures.Shrink(bytes, 640)), ["fillMode"] = "cover" };
            if (Num(layers[0], "height") is { } height) container["minHeight"] = Px(height);
            else if (root) container["minHeight"] = Px(Centred);
            rest = layers[1..];
        }
        container["items"] = new JsonArray([.. rest.SelectMany(layer => Render(layer, hAlign ?? halign))]);
        return [Box(container, node)];
    }

    /// <summary>
    /// A stack's padding, background and corners. A card has no padding of its own, so the content goes in a column
    /// between two of the padding's width, with rows of its height above and below.
    /// </summary>
    private JsonObject Box(JsonObject container, JsonObject node)
    {
        if ((Thickness(node, "insets") ?? Thickness(node, "padding")) is { } padding && container["items"] is JsonArray content)
        {
            var (left, top, right, bottom) = padding;
            JsonObject Side(double width) => new() { ["type"] = "Column", ["width"] = Px(width), ["spacing"] = "none", ["items"] = new JsonArray() };
            var middle = new JsonObject { ["type"] = "Column", ["width"] = "stretch", ["spacing"] = "none", ["items"] = new JsonArray([.. content.Select(i => i!.DeepClone())]) };
            if (container["verticalContentAlignment"] is { } valign) middle["verticalContentAlignment"] = valign.DeepClone();
            var columns = new JsonArray();
            if (left > 0) columns.Add(Side(left));
            columns.Add(middle);
            if (right > 0) columns.Add(Side(right));
            var padded = new JsonArray();
            if (top > 0) padded.Add(Gap(top));
            padded.Add(new JsonObject { ["type"] = "ColumnSet", ["spacing"] = "none", ["columns"] = columns });
            if (bottom > 0) padded.Add(Gap(bottom));
            container["items"] = padded;
        }

        if (Str(node, "background") is { } background)
        {
            var (light, dark) = _palette.Resolve(background);
            var lightUrl = DataUri(WidgetPictures.Surface(light, null, null, null, 1)!);
            container["backgroundImage"] = new JsonObject
            {
                ["url"] = light == dark ? lightUrl : $"${{if($host.hostTheme == 'dark', '{DataUri(WidgetPictures.Surface(dark, null, null, null, 1)!)}', '{lightUrl}')}}",
                ["fillMode"] = "repeat",
            };
        }
        if (Num(node, "cornerRadius") is > 0) container["roundedCorners"] = true;
        return container;
    }

    /// <summary>
    /// Empty vertical room. The board leaves out a container with no items whatever its <c>minHeight</c>, so the room is a
    /// transparent picture of that height.
    /// </summary>
    private static JsonObject Gap(double height) =>
        new() { ["type"] = "Image", ["url"] = Transparent, ["width"] = "1px", ["height"] = Px(height), ["spacing"] = "none" };

    private static readonly string Transparent = DataUri(WidgetPictures.Clear());

    private static bool HasBox(JsonObject node) =>
        Str(node, "background") is not null || Num(node, "cornerRadius") is > 0 || (Thickness(node, "insets") ?? Thickness(node, "padding")) is not null;

    /// <summary>Padding or a margin: one number for every side, or left, top, right and bottom.</summary>
    private static (double Left, double Top, double Right, double Bottom)? Thickness(JsonObject node, string name) => node[name] switch
    {
        JsonValue value when value.TryGetValue<double>(out var all) && all > 0 => (all, all, all, all),
        JsonArray { Count: 4 } sides when sides.Select(s => s is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0).ToArray() is var s4 && s4.Any(d => d > 0)
            => (s4[0], s4[1], s4[2], s4[3]),
        _ => null,
    };

    private JsonObject Text(JsonObject node, string text, string? hAlign)
    {
        var size = Num(node, "size") switch
        {
            { } points => points <= 12 ? "small" : points <= 15 ? "default" : points <= 19 ? "medium" : points <= 25 ? "large" : "extraLarge",
            null => Str(node, "font")?.ToLowerInvariant() switch
            {
                "largetitle" => "extraLarge",
                "title" => "large",
                "headline" => "medium",
                "caption" => "small",
                _ => "default",
            },
        };
        var bold = Bool(node, "bold") || Str(node, "font")?.ToLowerInvariant() == "headline";
        var style = new JsonObject { ["size"] = size };
        if (bold) style["weight"] = "bolder";
        if (Str(node, "design") == "monospaced") style["fontType"] = "monospace";
        Color(style, Str(node, "color"));

        var align = Bool(node, "centered") ? "center" : Bool(node, "trailing") ? "right" : hAlign;
        JsonObject element;
        if (Bool(node, "italic"))
        {
            var run = new JsonObject { ["type"] = "TextRun", ["text"] = text, ["italic"] = true };
            foreach (var (key, value) in style) run[key] = value?.DeepClone();
            element = new JsonObject { ["type"] = "RichTextBlock", ["inlines"] = new JsonArray(run) };
        }
        else
        {
            element = new JsonObject { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true };
            foreach (var (key, value) in style) element[key] = value?.DeepClone();
            if (Num(node, "singleLine") is not null) { element["wrap"] = false; element["maxLines"] = 1; }
            else if (Num(node, "maxLines") is { } lines and >= 1) element["maxLines"] = (int)lines;
        }
        if (align is not null) element["horizontalAlignment"] = align;
        return element;
    }

    /// <summary>The card's name for a colour: <c>secondary</c> is subtle text, a colour of the app's own the light or dark text it is nearest.</summary>
    private void Color(JsonObject style, string? color)
    {
        switch (color)
        {
            case null or "primary": return;
            case "secondary": style["isSubtle"] = true; return;
            case "accent" or "blue": style["color"] = "accent"; return;
            case "green": style["color"] = "good"; return;
            case "red": style["color"] = "attention"; return;
            case "orange" or "yellow": style["color"] = "warning"; return;
            case "onAccent": style["color"] = "light"; return;
            case "surface": return;
        }
        var (light, dark) = _palette.Resolve(color);
        var (lightName, darkName) = (Name(light), Name(dark));
        style["color"] = lightName == darkName ? lightName : $"${{if($host.hostTheme == 'dark', '{darkName}', '{lightName}')}}";
        if (light.Alpha < 192) style["isSubtle"] = true;

        static string Name(SKColor c) => 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue > 140 ? "light" : "dark";
    }

    /// <summary>A picture in a colour that may differ between the themes: one image, or one per theme with <c>$when</c>.</summary>
    private List<JsonObject> Themed(string color, Func<SKColor, byte[]?> draw, Func<string, JsonObject> element)
    {
        var (light, dark) = _palette.Resolve(color);
        if (draw(light) is not { } lightBytes) return [];
        if (light == dark || draw(dark) is not { } darkBytes) return [element(DataUri(lightBytes))];
        var forLight = element(DataUri(lightBytes));
        forLight["$when"] = Light;
        var forDark = element(DataUri(darkBytes));
        forDark["$when"] = Dark;
        return [forLight, forDark];
    }

    private static JsonObject Image(JsonObject node, string url, string? width, string? height, string? hAlign)
    {
        var image = new JsonObject { ["type"] = "Image", ["url"] = url };
        if (width is not null) image["width"] = width;
        if (height is not null) image["height"] = height;
        if (hAlign is not null) image["horizontalAlignment"] = hAlign;
        if (!Bool(node, "decorative") && (Str(node, "description") ?? Str(node, "altText")) is { } alt) image["altText"] = alt;
        return image;
    }

    private static JsonObject Stretch(JsonObject image)
    {
        image.Remove("width");
        image.Remove("height");
        image["size"] = "stretch";
        return image;
    }

    /// <summary>
    /// A count the card cannot tick: minutes, or hours and minutes, refreshed every minute while the board shows it.
    /// A countdown rounds up, so it reaches zero at the moment, and a time since rounds down.
    /// </summary>
    private static string Duration(TimeSpan span, bool up)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        var minutes = up ? (int)Math.Ceiling(span.TotalMinutes) : (int)Math.Floor(span.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60}:{minutes % 60:00}" : $"{minutes} min";
    }

    private static string Spacing(double points) => points switch
    {
        <= 0 => "none",
        <= 4 => "small",
        <= 8 => "default",
        <= 16 => "medium",
        <= 24 => "large",
        _ => "extraLarge",
    };

    private static string? HAlign(string? align) => align switch { "center" => "center", "end" => "right", _ => null };

    private static string Px(double points) => $"{Math.Max(1, (int)Math.Round(points)).ToString(CultureInfo.InvariantCulture)}px";

    internal static string DataUri(byte[] bytes) =>
        (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "data:image/jpeg;base64," : "data:image/png;base64,") + Convert.ToBase64String(bytes);

    private static IEnumerable<JsonObject> Children(JsonObject node) =>
        node["children"] is JsonArray children ? children.OfType<JsonObject>() : [];

    private static string? Str(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double? Num(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static bool Bool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static DateTimeOffset Date(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : DateTimeOffset.UtcNow;
}

/// <summary>
/// The content area of one of the board's sizes, in points: every size is one column wide, about 300 points with the
/// board's own margins, and the sizes differ in height. The board's small is a wide
/// strip about 100 points high; its medium is near square; its large is taller than it is wide.
/// </summary>
internal sealed record BoardArea(double Width, double Height, string[] Families);

/// <summary>A surface as the timeline document carries it: colour, gradient (colours and direction) and picture.</summary>
internal sealed record WidgetSurfaceFields(string? Color, IReadOnlyList<string>? Gradient, string? Direction, string? Image);

/// <summary>
/// The colours of the tree in the board's light and dark themes: the semantic ones as Windows draws text and its accent,
/// the iOS system palette for <c>green</c> … <c>blue</c> as on Android, and the app's own as written.
/// </summary>
internal sealed class WindowsWidgetPalette
{
    private readonly SKColor _accentLight;
    private readonly SKColor _accentDark;

    public WindowsWidgetPalette()
    {
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            _accentLight = Sk(settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1));
            _accentDark = Sk(settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            (_accentLight, _accentDark) = (new SKColor(0xFF005FB8), new SKColor(0xFF60CDFF));
        }

        static SKColor Sk(Windows.UI.Color c) => new(c.R, c.G, c.B, c.A);
    }

    public (SKColor Light, SKColor Dark) Resolve(string color)
    {
        if (color.IndexOf('/') is var slash and > 0 && SKColor.TryParse(color[..slash], out var lightPart) && SKColor.TryParse(color[(slash + 1)..], out var darkPart))
            return (lightPart, darkPart);
        if (color.StartsWith('#') && SKColor.TryParse(color, out var fixedColor))
            return (fixedColor, fixedColor);
        return color switch
        {
            "secondary" => (new SKColor(0x9E000000), new SKColor(0xC8FFFFFF)),
            "accent" => (_accentLight, _accentDark),
            "surface" => (new SKColor(0xFFF9F9F9), new SKColor(0xFF2C2C2C)),
            "onAccent" => (SKColors.White, SKColors.Black),
            "green" => (new SKColor(0xFF34C759), new SKColor(0xFF30D158)),
            "red" => (new SKColor(0xFFFF3B30), new SKColor(0xFFFF453A)),
            "orange" => (new SKColor(0xFFFF9500), new SKColor(0xFFFF9F0A)),
            "yellow" => (new SKColor(0xFFFFCC00), new SKColor(0xFFFFD60A)),
            "blue" => (new SKColor(0xFF007AFF), new SKColor(0xFF0A84FF)),
            _ => (new SKColor(0xE4000000), SKColors.White),
        };
    }
}
