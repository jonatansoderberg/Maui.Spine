namespace Plugin.Maui.Spine.Controls;

/// <summary>A placeholder block in overlay coordinates; <paramref name="Fill"/> when it has no colour of its own.</summary>
internal readonly record struct Placeholder(RectF Rect, float Radius, bool Fill);

/// <summary>
/// Paints the placeholders that have no colour of their own, then the wave clipped to every
/// placeholder, so only the blocks light up and the gaps between them stay untouched.
/// </summary>
internal sealed class SkeletonDrawable : IDrawable
{
    public readonly List<Placeholder> Placeholders = [];

    public float Progress;
    public bool ShowWave;

    /// <summary>With no placeholders the wave sweeps the whole canvas (a skeleton of an unexpected shape) instead of nothing.</summary>
    public bool WaveEverywhereWhenEmpty;

    public Color PlaceholderColor = Colors.LightGray;
    public Color WaveColor = Colors.White;
    public float WaveOpacity = 0.5f;
    public float WaveWidth = 0.22f;
    public float WaveAngle = 15f;

    // Draw runs every animation tick; rebuilding dozens of rounded rects and a gradient per frame
    // is what caused visible stutter, so both are kept until the placeholders or the style change.
    private PathF? _clipPath;
    private PathF? _fillPath;
    private LinearGradientPaint? _paint;

    public void InvalidatePlaceholders()
    {
        _clipPath = null;
        _fillPath = null;
    }

    public void InvalidatePaint() => _paint = null;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        if (Placeholders.Count > 0)
        {
            _fillPath ??= BuildPath(fillOnly: true);
            if (_fillPath.OperationCount > 0)
            {
                canvas.FillColor = PlaceholderColor;
                canvas.FillPath(_fillPath);
            }
        }

        if (!ShowWave || (Placeholders.Count == 0 && !WaveEverywhereWhenEmpty))
            return;

        canvas.SaveState();

        if (Placeholders.Count > 0)
            canvas.ClipPath(_clipPath ??= BuildPath(fillOnly: false));

        _paint ??= new LinearGradientPaint
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            [
                new PaintGradientStop(0f, WaveColor.WithAlpha(0f)),
                new PaintGradientStop(0.5f, WaveColor.WithAlpha(WaveOpacity)),
                new PaintGradientStop(1f, WaveColor.WithAlpha(0f)),
            ],
        };

        var waveWidth = Math.Max(1, dirtyRect.Width * WaveWidth);
        var bandX = -waveWidth + Progress * (dirtyRect.Width + 2 * waveWidth);

        // Overshoot vertically so the tilted band still covers the canvas.
        canvas.Rotate(WaveAngle, bandX + waveWidth / 2, dirtyRect.Height / 2);
        var band = new RectF(bandX, -dirtyRect.Height, waveWidth, dirtyRect.Height * 3);
        canvas.SetFillPaint(_paint, band);
        canvas.FillRectangle(band);

        canvas.RestoreState();
    }

    private PathF BuildPath(bool fillOnly)
    {
        var path = new PathF();
        foreach (var placeholder in Placeholders)
        {
            if (fillOnly && !placeholder.Fill)
                continue;

            if (placeholder.Radius > 0)
                path.AppendRoundedRectangle(placeholder.Rect, placeholder.Radius);
            else
                path.AppendRectangle(placeholder.Rect);
        }
        return path;
    }
}
