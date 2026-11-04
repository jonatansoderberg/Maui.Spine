using SkiaSharp;
using Svg.Skia;
using Shim = ShimSkiaSharp;

namespace SpineSymbols;

/// <summary>
/// An SVG as one filled outline in its viewBox's coordinates, which is all a custom symbol can be: each stroke
/// becomes its outline with its own width, caps and joins, fills stay, and everything is unioned.
/// </summary>
internal static class SymbolOutline
{
    public sealed record Result(SKPath Outline, SKRect ViewBox, IReadOnlyList<string> Warnings);

    /// <summary>The outline is scaled up later (to the template and then to pixels), so curves are flattened for this.</summary>
    private const float ResolutionScale = 8;

    public static Result Load(string file)
    {
        using var svg = new SKSvg();
        svg.Load(file);
        var model = svg.Model ?? throw new InvalidDataException($"{file}: Svg.Skia could not read it.");
        var skia = svg.SkiaModel;

        var warnings = new List<string>();
        var outline = new SKPath();
        var matrix = SKMatrix.Identity;
        var saved = new Stack<SKMatrix>();

        foreach (var command in model.Commands ?? [])
        {
            switch (command)
            {
                case Shim.SaveCanvasCommand or Shim.SaveLayerCanvasCommand:
                    saved.Push(matrix);
                    break;
                case Shim.RestoreCanvasCommand:
                    if (saved.Count > 0) matrix = saved.Pop();
                    break;
                case Shim.SetMatrixCanvasCommand set:
                    matrix = skia.ToSKMatrix(set.TotalMatrix);
                    break;
                case Shim.DrawPathCanvasCommand { Path: { } path, Paint: { } paint }:
                    if (Shape(skia, path, paint, warnings) is not { } shape) break;
                    using (shape)
                    {
                        shape.Transform(matrix);
                        var union = outline.Op(shape, SKPathOp.Union)
                            ?? throw new InvalidDataException($"{file}: the outlines could not be unioned.");
                        outline.Dispose();
                        outline = union;
                    }
                    break;
                case Shim.DrawTextCanvasCommand or Shim.DrawTextBlobCanvasCommand or Shim.DrawTextOnPathCanvasCommand:
                    warnings.Add("text is left out; convert it to paths");
                    break;
                case Shim.DrawImageCanvasCommand:
                    warnings.Add("an embedded image is left out");
                    break;
            }
        }

        // Path ops leave non-overlapping contours under even-odd; the template is drawn non-zero.
        var winding = outline.ToWinding();
        outline.Dispose();
        var box = model.CullRect;
        return new Result(winding, new SKRect(box.Left, box.Top, box.Right, box.Bottom), warnings);
    }

    private static SKPath? Shape(SkiaModel skia, Shim.SKPath path, Shim.SKPaint paint, List<string> warnings)
    {
        if (paint.Shader is null && paint.Color is not { Alpha: > 0 }) return null;
        if (paint.Color is { Alpha: > 0 and < 255 }) warnings.Add("a part drawn translucent is drawn solid: a monochrome symbol has one ink");

        var source = skia.ToSKPath(path);
        if (paint.Style == Shim.SKPaintStyle.Fill) return source;

        using (source)
        {
            if (paint.StrokeWidth <= 0) return null;
            using var stroke = skia.ToSKPaint(paint);
            if (stroke is null) return null;
            return stroke.GetFillPath(source, ResolutionScale);
        }
    }
}
