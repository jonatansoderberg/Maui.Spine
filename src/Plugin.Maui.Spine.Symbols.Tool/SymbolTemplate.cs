using System.Globalization;
using System.Text;
using SkiaSharp;

namespace SpineSymbols;

/// <summary>
/// Writes an outline as an SF Symbols 3.0 template that Control Center can draw: the template version in Notes, all
/// six baselines and caplines, and the variable set Ultralight-S, Regular-S and Black-S with their margins, each the
/// same outline, so it interpolates and draws alike at every weight. A static template (Regular-M alone) compiles,
/// and the extension finds it, but SpringBoard logs "No image named … found in asset catalog" and the control stays
/// empty (iOS 26.4 simulator, #743). The metrics are SF Pro's at 100 pt, as in Apple's own templates.
/// </summary>
internal static class SymbolTemplate
{
    private const float BaselineS = 696, BaselineM = 1126, BaselineL = 1556, CapHeight = 70.459f;
    private const float GuideLeft = 263, GuideRight = 3036;
    private static readonly (string Weight, float Left)[] Weights = [("Ultralight", 559), ("Regular", 1449), ("Black", 2339)];

    /// <summary>
    /// How tall the icon's whole viewBox is drawn, in the template's units. Spine's icons leave about a fifth of the
    /// box empty, so Bell and Info come out about as large as SF's bell and info.circle beside them.
    /// </summary>
    public const float BoxHeight = 120;

    public static string Write(SKPath outline, SKRect viewBox)
    {
        var scale = BoxHeight / viewBox.Height;
        var width = viewBox.Width * scale;
        // Centred on the middle of the capital letters, as SF Symbols sit beside text.
        var top = -CapHeight / 2 - BoxHeight / 2;

        using var glyph = new SKPath(outline);
        glyph.Transform(SKMatrix.CreateTranslation(-viewBox.Left, -viewBox.Top).PostConcat(SKMatrix.CreateScale(scale, scale)).PostConcat(SKMatrix.CreateTranslation(0, top)));

        var svg = new StringBuilder();
        svg.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        svg.AppendLine("""<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">""");
        svg.AppendLine("""<svg version="1.1" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="3300" height="2200">""");
        svg.AppendLine(""" <g id="Notes">""");
        svg.AppendLine("""  <text id="template-version" style="stroke:none;fill:black;font-family:sans-serif;font-size:13;" transform="matrix(1 0 0 1 3036 1933)">Template v.3.0</text>""");
        svg.AppendLine(""" </g>""");
        svg.AppendLine(""" <style>.monochrome-0 {fill:#000000}""");
        svg.AppendLine(""".multicolor-0:tintColor {fill:#007AFF}""");
        svg.AppendLine(""".hierarchical-0:primary {fill:#000000}""");
        svg.AppendLine("""</style>""");
        svg.AppendLine(""" <g id="Guides">""");
        foreach (var (scaleName, baseline) in new[] { ("S", BaselineS), ("M", BaselineM), ("L", BaselineL) })
        {
            Line(svg, $"Baseline-{scaleName}", GuideLeft, baseline, GuideRight, baseline);
            Line(svg, $"Capline-{scaleName}", GuideLeft, baseline - CapHeight, GuideRight, baseline - CapHeight);
        }
        foreach (var (weight, left) in Weights)
        {
            Line(svg, $"left-margin-{weight}-S", left, BaselineS - 96, left, BaselineS + 24);
            Line(svg, $"right-margin-{weight}-S", left + width, BaselineS - 96, left + width, BaselineS + 24);
        }
        svg.AppendLine(""" </g>""");
        svg.AppendLine(""" <g id="Symbols">""");
        var path = glyph.ToSvgPathData();
        foreach (var (weight, left) in Weights)
        {
            svg.AppendLine($"""  <g id="{weight}-S" transform="matrix(1 0 0 1 {F(left)} {F(BaselineS)})">""");
            svg.AppendLine($"""   <path class="monochrome-0 multicolor-0:tintColor hierarchical-0:primary" d="{path}"/>""");
            svg.AppendLine("""  </g>""");
        }
        svg.AppendLine(""" </g>""");
        svg.AppendLine("""</svg>""");
        return svg.ToString();
    }

    private static void Line(StringBuilder svg, string id, float x1, float y1, float x2, float y2) =>
        svg.AppendLine($"""  <line id="{id}" style="fill:none;stroke:#27AAE1;opacity:1;stroke-width:0.5;" x1="{F(x1)}" x2="{F(x2)}" y1="{F(y1)}" y2="{F(y2)}"/>""");

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
