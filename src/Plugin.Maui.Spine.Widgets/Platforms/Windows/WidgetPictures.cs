using SkiaSharp;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// What an Adaptive Card cannot draw itself, drawn with Skia into the PNGs or JPEGs the card shows: a surface of the
/// app's colour, gradient or picture, a tinted icon, a ring, a progress bar, a sparkline. A card takes a colour only by
/// name, so every colour of the app's own reaches the widget board as a picture.
/// </summary>
internal static class WidgetPictures
{
    /// <summary>The long side of a surface picture; the widget board's large size is about 300 by 300 at 200 %.</summary>
    private const int SurfacePixels = 720;

    /// <summary>
    /// The widget's surface: the colour or gradient, the picture over it scaled to fill and cropped. JPEG when nothing
    /// is transparent, which keeps a photo small enough to travel in the card.
    /// </summary>
    public static byte[]? Surface(SKColor? color, IReadOnlyList<SKColor>? gradient, string? direction, byte[]? picture, double aspect)
    {
        using var bitmap = picture is null ? null : SKBitmap.Decode(picture);
        if (color is null && gradient is not { Count: > 0 } && bitmap is null) return null;

        // The picture's own proportions when there is one; the widget's otherwise.
        var (width, height) = bitmap is { Width: > 0, Height: > 0 }
            ? Fit(bitmap.Width, bitmap.Height, SurfacePixels)
            : Fit((int)(SurfacePixels * aspect), SurfacePixels, color is not null && gradient is null ? 16 : 256);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var rect = new SKRect(0, 0, width, height);

        if (gradient is { Count: > 1 })
        {
            var end = direction?.ToLowerInvariant() switch
            {
                "horizontal" => new SKPoint(width, 0),
                "diagonal" => new SKPoint(width, height),
                _ => new SKPoint(0, height),
            };
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), end, [.. gradient], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true };
            canvas.DrawRect(rect, paint);
        }
        else if ((gradient is { Count: 1 } ? gradient[0] : color) is { } solid)
            canvas.Clear(solid);

        var opaque = color is { Alpha: 255 } || gradient is { Count: > 0 } && gradient.All(c => c.Alpha == 255);
        if (bitmap is not null)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(image, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
            opaque |= bitmap.AlphaType == SKAlphaType.Opaque;
        }

        using var snapshot = surface.Snapshot();
        using var data = opaque ? snapshot.Encode(SKEncodedImageFormat.Jpeg, 88) : snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A stored picture made small enough for the card: at most <paramref name="pixels"/> on its long side.</summary>
    public static byte[] Shrink(byte[] picture, int pixels)
    {
        using var bitmap = SKBitmap.Decode(picture);
        if (bitmap is null || Math.Max(bitmap.Width, bitmap.Height) <= pixels) return picture;
        var (width, height) = Fit(bitmap.Width, bitmap.Height, pixels);
        using var scaled = bitmap.Resize(new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(scaled);
        using var data = bitmap.AlphaType == SKAlphaType.Opaque ? image.Encode(SKEncodedImageFormat.Jpeg, 88) : image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The white icon <see cref="WidgetIconAssets"/> stored, in <paramref name="color"/>.</summary>
    public static byte[]? Tint(byte[] whiteIcon, SKColor color)
    {
        using var bitmap = SKBitmap.Decode(whiteIcon);
        if (bitmap is null) return null;
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn) };
        surface.Canvas.DrawBitmap(bitmap, 0, 0, paint);
        return Png(surface);
    }

    /// <summary>A ring as Android draws it: the track faint in the colour, the value clockwise from 12 o'clock.</summary>
    public static byte[] Ring(int size, double? value, double? thickness, SKColor color, float scale)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var line = (float)((thickness ?? Math.Max(3, size / scale / 10)) * scale);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = line, StrokeCap = SKStrokeCap.Round };
        var oval = new SKRect(line / 2, line / 2, size - line / 2, size - line / 2);
        paint.Color = color.WithAlpha((byte)(255 * 0.22));
        canvas.DrawOval(oval, paint);
        if (value is { } v && v > 0)
        {
            paint.Color = color;
            using var arc = new SKPath();
            arc.AddArc(oval, -90, (float)(Math.Min(v, 1) * 360));
            canvas.DrawPath(arc, paint);
        }
        return Png(surface);
    }

    /// <summary>A linear progress bar, wide and thin so the card stretches it across its column.</summary>
    public static byte[] Progress(double value, SKColor color)
    {
        const int Width = 600, Height = 12;
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)(255 * 0.22)) };
        canvas.DrawRoundRect(new SKRect(0, 0, Width, Height), Height / 2f, Height / 2f, paint);
        paint.Color = color;
        var filled = (float)(Math.Clamp(value, 0, 1) * Width);
        if (filled > 0) canvas.DrawRoundRect(new SKRect(0, 0, Math.Max(filled, Height), Height), Height / 2f, Height / 2f, paint);
        return Png(surface);
    }

    /// <summary>
    /// A sparkline: a line through the values, coloured above and below the band, with the area under it fading
    /// downwards and a dot on the last value. Straight segments rather than the monotone curve of the other platforms.
    /// </summary>
    public static byte[] Sparkline(IReadOnlyList<double?> values, double? min, double? max, double? low, double? high,
        SKColor color, SKColor above, SKColor below, bool area, bool dot, int width, int height, float scale)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var known = values.OfType<double>().ToArray();
        if (known.Length == 0) return Png(surface);

        var line = Math.Max(1.5f, height / scale / 20) * scale;
        var edge = dot ? line * 3 : line / 2;
        var plot = new SKRect(line / 2, edge, width - edge, height - edge);
        var (lowest, highest) = (known.Min(), known.Max());
        var pad = highest > lowest ? (highest - lowest) * 0.15 : Math.Max(Math.Abs(highest) * 0.1, 1);
        var bottom = min is { } m ? Math.Min(m, lowest) : lowest - pad;
        var top = max is { } t ? Math.Max(t, highest) : highest + pad;
        float X(int i) => values.Count > 1 ? plot.Left + (float)i / (values.Count - 1) * plot.Width : plot.Right;
        float Y(double v) => plot.Bottom - (float)((v - bottom) / (top - bottom)) * plot.Height;
        SKColor Tint(double v) => high is { } h && v > h ? above : low is { } l && v < l ? below : color;

        var bridged = Math.Max(2, values.Count / 10);
        var segments = new List<List<(SKPoint Point, double Value)>> { new() };
        var missing = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } value) { missing++; continue; }
            if (missing > bridged && segments[^1].Count > 0) segments.Add([]);
            missing = 0;
            segments[^1].Add((new SKPoint(X(i), Y(value)), value));
        }

        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = line, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        foreach (var segment in segments.Where(s => s.Count > 0))
        {
            if (area && segment.Count > 1)
            {
                using var fill = new SKPath();
                fill.MoveTo(segment[0].Point.X, plot.Bottom + edge);
                foreach (var (point, _) in segment) fill.LineTo(point);
                fill.LineTo(segment[^1].Point.X, plot.Bottom + edge);
                fill.Close();
                var tint = Tint(segment[^1].Value);
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, segment.Min(p => p.Point.Y)), new SKPoint(0, height),
                    [tint.WithAlpha(90), tint.WithAlpha(0)], SKShaderTileMode.Clamp);
                using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                canvas.DrawPath(fill, paint);
            }
            for (var i = 1; i < segment.Count; i++)
            {
                stroke.Color = Tint((segment[i - 1].Value + segment[i].Value) / 2);
                canvas.DrawLine(segment[i - 1].Point, segment[i].Point, stroke);
            }
            if (segment.Count == 1)
            {
                stroke.Color = Tint(segment[0].Value);
                canvas.DrawPoint(segment[0].Point, stroke);
            }
        }

        if (dot && segments.LastOrDefault(s => s.Count > 0) is { } last)
        {
            var (point, value) = last[^1];
            using var paint = new SKPaint { IsAntialias = true, Color = Tint(value) };
            canvas.DrawCircle(point, line * 2, paint);
        }
        return Png(surface);
    }

    private static (int Width, int Height) Fit(int width, int height, int longSide)
    {
        var scale = (double)longSide / Math.Max(width, height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static byte[] Png(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
