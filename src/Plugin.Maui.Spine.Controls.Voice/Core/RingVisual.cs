using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A spectrum around a circle, for around a button or a profile picture: the answer's bands point out, the microphone's
/// in. The middle stays empty for what the ring surrounds. Under Reduce Motion, a ring whose width follows the level. <c>ring</c>.
/// </summary>
public sealed class RingVisual : VoiceVisual
{
    private const int Ticks = VoiceFrame.BandCount * 2;
    private static readonly TimeSpan Smoothing = TimeSpan.FromMilliseconds(80);

    private readonly float[] _out = new float[VoiceFrame.BandCount];
    private readonly float[] _in = new float[VoiceFrame.BandCount];
    private float _level;

    /// <inheritdoc />
    public override void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)
    {
        var live = IsLive(frame.State);
        var size = Math.Min(bounds.Width, bounds.Height);
        var center = new SKPoint(bounds.MidX, bounds.MidY);
        var radius = size * 0.33f;
        var reach = size * 0.14f;

        using var circle = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * context.Scale, IsAntialias = true, Color = context.Input.WithAlpha(70) };
        canvas.DrawCircle(center, radius, circle);

        if (context.ReducedMotion)
        {
            _level = Ease(_level, live ? Math.Max(frame.InputLevel, frame.OutputLevel) : 0, context.Delta, TimeSpan.FromMilliseconds(400));
            circle.Color = Mix(context.Input, context.Output, Speaker(frame.State));
            circle.StrokeWidth = (2 + 10 * _level) * context.Scale;
            canvas.DrawCircle(center, radius, circle);
            return;
        }

        using var outward = new SKPaint { Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 3 * context.Scale, IsAntialias = true, Color = context.Output };
        using var inward = new SKPaint { Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 3 * context.Scale, IsAntialias = true, Color = context.Input };
        for (var b = 0; b < VoiceFrame.BandCount; b++)
        {
            _out[b] = Ease(_out[b], live ? frame.OutputBands[b] : 0, context.Delta, Smoothing);
            _in[b] = Ease(_in[b], live ? frame.InputBands[b] : 0, context.Delta, Smoothing);
        }

        // The bands run up one side and down the other, so the ring is symmetric and low notes meet at the top.
        for (var t = 0; t < Ticks; t++)
        {
            var band = t < VoiceFrame.BandCount ? t : Ticks - 1 - t;
            var angle = -Math.PI / 2 + t * Math.Tau / Ticks;
            var (cos, sin) = ((float)Math.Cos(angle), (float)Math.Sin(angle));
            var gap = 3 * context.Scale;
            var outer = gap + reach * _out[band];
            var inner = gap + reach * 0.7f * _in[band];
            canvas.DrawLine(center.X + cos * (radius + gap), center.Y + sin * (radius + gap), center.X + cos * (radius + outer), center.Y + sin * (radius + outer), outward);
            canvas.DrawLine(center.X + cos * (radius - gap), center.Y + sin * (radius - gap), center.X + cos * (radius - inner), center.Y + sin * (radius - inner), inward);
        }
    }
}
