using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Two things browsers draw that Svg.Skia does not, rewritten into SVG it does, before it parses the file. Figma exports
/// both, so official assets such as Google's G need them:
/// <list type="bullet">
/// <item>A <c>foreignObject</c> whose content is a CSS <c>conic-gradient</c> (Figma's angular gradient). It becomes an
/// <c>image</c> of the same box, drawn with Skia's sweep gradient.</item>
/// <item><c>mask-type: alpha</c>. Svg.Skia reads every mask by luminance, so the mask's colours become white with their
/// alpha kept, which makes the luminance the alpha.</item>
/// </list>
/// </summary>
internal static class SvgCssFeatures
{
    // The sweep is drawn once at load, before the size it is shown at is known. A conic gradient has no edges inside it,
    // so this many pixels across the box stay smooth when the picture is scaled up.
    private const int ConicPixels = 512;

    /// <summary>
    /// The SVG with both rewritten, or <see langword="null"/> when it uses neither or cannot be read here; Svg.Skia then
    /// gets the file as it was.
    /// </summary>
    public static string? Rewrite(string svg)
    {
        if (!svg.Contains("conic-gradient", StringComparison.OrdinalIgnoreCase) && !svg.Contains("mask-type", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return RewriteXml(svg.TrimStart('\uFEFF'));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string? RewriteXml(string svg)
    {
        var document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        var changed = false;

        foreach (var foreign in document.Descendants().Where(e => e.Name.LocalName == "foreignObject").ToList())
        {
            var background = foreign.Descendants().Select(e => Style(e, "background") ?? Style(e, "background-image"))
                .FirstOrDefault(b => b?.TrimStart().StartsWith("conic-gradient(", StringComparison.OrdinalIgnoreCase) == true);
            if (background is null || ConicGradient.Parse(background) is not { } gradient)
                continue;

            var ns = foreign.Name.Namespace;
            var href = "data:image/png;base64," + Convert.ToBase64String(gradient.RenderPng(ConicPixels, ConicPixels));
            var image = new XElement(ns + "image",
                new XAttribute("x", Number(foreign, "x")),
                new XAttribute("y", Number(foreign, "y")),
                new XAttribute("width", Number(foreign, "width")),
                new XAttribute("height", Number(foreign, "height")),
                new XAttribute("preserveAspectRatio", "none"),
                new XAttribute("href", href),
                new XAttribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href", href));
            foreign.ReplaceWith(image);
            changed = true;
        }

        foreach (var mask in document.Descendants().Where(e => e.Name.LocalName == "mask"
                     && string.Equals(Style(e, "mask-type") ?? (string?)e.Attribute("mask-type"), "alpha", StringComparison.OrdinalIgnoreCase)))
        {
            // A shape without a fill is black, which a luminance mask hides; in an alpha mask it shows.
            if (mask.Attribute("fill") is null && Style(mask, "fill") is null)
                mask.SetAttributeValue("fill", "#FFFFFF");

            foreach (var element in mask.Descendants())
            {
                Whiten(element, "fill");
                Whiten(element, "stroke");
                Whiten(element, "stop-color");
            }
            changed = true;
        }

        return changed ? document.ToString(SaveOptions.DisableFormatting) : null;
    }

    private static void Whiten(XElement element, string property)
    {
        if (element.Attribute(property) is { } attribute && IsPaint(attribute.Value))
            attribute.Value = White(attribute.Value);
        if (Style(element, property) is { } value && IsPaint(value) && element.Attribute("style") is { } style)
            style.Value = string.Join(';', style.Value.Split(';').Select(d =>
                d.Split(':', 2) is [var name, _] && name.Trim().Equals(property, StringComparison.OrdinalIgnoreCase) ? $"{property}:{White(value)}" : d));

        // White with the colour's own alpha (rgba(), #RRGGBBAA), which is what the mask is made of.
        static string White(string colour) => ConicGradient.Colour(colour) is { Alpha: < 255 } c
            ? FormattableString.Invariant($"rgba(255,255,255,{c.Alpha / 255f:0.###})")
            : "#FFFFFF";

        // A gradient or pattern keeps its stops' alpha but not its colour; its stops are whitened where they are.
        static bool IsPaint(string value) => value.Trim() is var v && v.Length > 0
            && !v.Equals("none", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("url(", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Style(XElement element, string property)
    {
        if (element.Attribute("style")?.Value is not { } style)
            return null;
        foreach (var declaration in style.Split(';'))
        {
            if (declaration.Split(':', 2) is [var name, var value] && name.Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                return value.Trim();
        }
        return null;
    }

    private static string Number(XElement element, string name) => element.Attribute(name)?.Value ?? "0";
}

/// <summary>A CSS <c>conic-gradient(…)</c>: its start angle, centre and colour stops.</summary>
internal sealed record ConicGradient(float FromDegrees, SKPoint Centre, IReadOnlyList<(SKColor Color, float Position)> Stops)
{
    /// <summary>The gradient, or <see langword="null"/> when <paramref name="css"/> is not one Spine can read.</summary>
    /// <param name="css">The value, as <c>conic-gradient(from 90deg at 50% 50%, red 0deg, blue 25%, …)</c>.</param>
    public static ConicGradient? Parse(string css)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close <= open)
            return null;

        var parts = SplitTopLevel(css[(open + 1)..close]);
        if (parts.Count == 0)
            return null;

        var from = 0f;
        var centre = new SKPoint(0.5f, 0.5f);
        var first = parts[0].Trim();
        if (first.StartsWith("from", StringComparison.OrdinalIgnoreCase) || first.StartsWith("at", StringComparison.OrdinalIgnoreCase))
        {
            var words = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Equals("from", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Length && Angle(words[i + 1]) is { } angle)
                    from = angle * 360;
                else if (words[i].Equals("at", StringComparison.OrdinalIgnoreCase) && i + 2 < words.Length
                         && Percent(words[i + 1]) is { } x && Percent(words[i + 2]) is { } y)
                    centre = new SKPoint(x, y);
            }
            parts.RemoveAt(0);
        }

        var stops = new List<(SKColor Color, float? Position)>();
        foreach (var part in parts)
        {
            var text = part.Trim();
            var colourEnd = text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || text.StartsWith("hsl", StringComparison.OrdinalIgnoreCase)
                ? text.IndexOf(')') + 1
                : text.IndexOf(' ') is var space and > 0 ? space : text.Length;
            if (colourEnd <= 0 || Colour(text[..colourEnd]) is not { } colour)
                return null;
            var positions = text[colourEnd..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (positions.Length == 0)
                stops.Add((colour, null));
            foreach (var position in positions)
            {
                if (Angle(position) is not { } at)
                    return null;
                stops.Add((colour, at));
            }
        }
        if (stops.Count < 2)
            return null;

        // CSS: a first stop without a position is at 0, a last one at 1, and those in between share the gap evenly.
        if (stops[0].Position is null) stops[0] = (stops[0].Color, 0);
        if (stops[^1].Position is null) stops[^1] = (stops[^1].Color, 1);
        for (var i = 1; i < stops.Count; i++)
        {
            if (stops[i].Position is not null)
                continue;
            var next = stops.FindIndex(i, s => s.Position is not null);
            var start = stops[i - 1].Position!.Value;
            var step = (stops[next].Position!.Value - start) / (next - i + 1);
            for (var j = i; j < next; j++)
                stops[j] = (stops[j].Color, start + step * (j - i + 1));
        }

        // A position before an earlier one is moved up to it, as CSS does.
        var resolved = new List<(SKColor, float)>();
        var last = 0f;
        foreach (var (color, position) in stops)
            resolved.Add((color, last = Math.Max(last, position!.Value)));
        return new ConicGradient(from, centre, resolved);
    }

    /// <summary>Draws the gradient filling a <paramref name="width"/> × <paramref name="height"/> PNG, as CSS fills its box.</summary>
    public byte[] RenderPng(int width, int height)
    {
        var centre = new SKPoint(Centre.X * width, Centre.Y * height);

        // CSS turns clockwise from the top; Skia's sweep starts at the right, so a quarter turn back.
        using var shader = SKShader.CreateSweepGradient(centre,
            Stops.Select(s => s.Color).ToArray(),
            Stops.Select(s => Math.Clamp(s.Position, 0, 1)).ToArray(),
            SKShaderTileMode.Clamp, 0, 360,
            SKMatrix.CreateRotationDegrees(FromDegrees - 90, centre.X, centre.Y));
        using var paint = new SKPaint { Shader = shader };
        using var bitmap = new SKBitmap(new SKImageInfo(width, height));
        using (var canvas = new SKCanvas(bitmap))
            canvas.DrawRect(0, 0, width, height, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>An angle as a fraction of a turn: <c>deg</c>, <c>turn</c>, <c>rad</c>, <c>grad</c> or a percentage.</summary>
    private static float? Angle(string text)
    {
        foreach (var (unit, perTurn) in new[] { ("deg", 360f), ("grad", 400f), ("rad", MathF.Tau), ("turn", 1f), ("%", 100f) })
        {
            if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase)
                && float.TryParse(text[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value / perTurn;
        }
        return null;
    }

    private static float? Percent(string text) => text.EndsWith('%')
        && float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value / 100 : null;

    /// <summary>A CSS colour Spine reads: <c>rgb()</c>/<c>rgba()</c> or hex; <see langword="null"/> for anything else.</summary>
    internal static SKColor? Colour(string text)
    {
        text = text.Trim();
        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open < 0 || close < open)
                return null;
            var parts = text[(open + 1)..close].Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return null;

            // A channel's percentage is of 255, the alpha's of 1.
            var values = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var percent = parts[i].EndsWith('%');
                if (!float.TryParse(percent ? parts[i][..^1] : parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    return null;
                values[i] = !percent ? value : i < 3 ? value * 255 / 100 : value / 100;
            }
            var alpha = values.Length > 3 ? values[3] : 1;
            return new SKColor((byte)Math.Clamp(MathF.Round(values[0]), 0, 255), (byte)Math.Clamp(MathF.Round(values[1]), 0, 255),
                (byte)Math.Clamp(MathF.Round(values[2]), 0, 255), (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
        }
        return SKColor.TryParse(text, out var colour) ? colour : null;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }
}
