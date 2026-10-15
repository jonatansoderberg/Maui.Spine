using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A simple face: it blinks, looks at the user while listening and up while thinking, and its mouth follows the viseme when
/// a source gives one (<see cref="Visemes"/>), otherwise the answer's level. <c>avatar</c>. Characters of their own (3D,
/// Rive) belong in a <c>VoiceVisualView</c>.
/// </summary>
public sealed class AvatarVisual : VoiceVisual
{
    private static readonly TimeSpan BlinkLength = TimeSpan.FromMilliseconds(130);
    private static readonly int[] Sides = [-1, 1];

    private readonly Random _random = new(5);
    private float _open;
    private float _wide = 0.5f;
    private float _lookX;
    private float _lookY;
    private double _untilBlink = 2.5;
    private double _blinking;

    /// <summary>Whether the eyes are closed on this frame; for tests.</summary>
    internal bool IsBlinking => _blinking > 0;

    /// <summary>How open the mouth is, 0–1, after smoothing; for tests.</summary>
    internal float MouthOpen => _open;

    /// <inheritdoc />
    public override void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)
    {
        var delta = context.Delta.TotalSeconds;
        Blink(delta, context.ReducedMotion);

        var speaking = frame.State == VoiceState.Speaking;
        var (open, wide) = frame.Viseme >= 0
            ? Visemes.Shape(frame.Viseme)
            : (speaking ? Math.Clamp(frame.OutputLevel * 1.3f - 0.1f, 0, 1) : 0f, 0.55f);
        var mouthTime = context.ReducedMotion ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(45);
        _open = Ease(_open, open, context.Delta, mouthTime);
        _wide = Ease(_wide, wide, context.Delta, mouthTime);

        // Down toward the user while listening, up and aside while thinking, ahead otherwise.
        var (lookX, lookY) = frame.State switch
        {
            VoiceState.Listening => (0f, 0.35f),
            VoiceState.Thinking => (0.35f, -0.45f),
            _ => (0f, 0f),
        };
        _lookX = Ease(_lookX, lookX, context.Delta, TimeSpan.FromMilliseconds(200));
        _lookY = Ease(_lookY, lookY, context.Delta, TimeSpan.FromMilliseconds(200));

        var size = Math.Min(bounds.Width, bounds.Height);
        var center = new SKPoint(bounds.MidX, bounds.MidY);
        var head = size * 0.4f;
        var skin = Mix(context.Output, SKColors.White, 0.7f);
        var ink = Mix(context.Output, SKColors.Black, 0.65f);

        using var face = new SKPaint { IsAntialias = true, Color = skin };
        canvas.DrawCircle(center, head, face);
        using var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 * context.Scale, Color = Mix(skin, ink, 0.25f) };
        canvas.DrawCircle(center, head, rim);

        using var eye = new SKPaint { IsAntialias = true, Color = ink };
        var eyeY = center.Y - head * 0.15f;
        var eyeDx = head * 0.36f;
        var eyeR = head * 0.12f;
        foreach (var side in Sides)
        {
            var ex = center.X + side * eyeDx + _lookX * eyeR * 0.6f;
            var ey = eyeY + _lookY * eyeR * 0.6f;
            if (IsBlinking)
            {
                eye.Style = SKPaintStyle.Stroke;
                eye.StrokeWidth = 2.5f * context.Scale;
                eye.StrokeCap = SKStrokeCap.Round;
                canvas.DrawLine(ex - eyeR, ey, ex + eyeR, ey, eye);
                eye.Style = SKPaintStyle.Fill;
            }
            else
            {
                canvas.DrawOval(ex, ey, eyeR * 0.8f, eyeR, eye);
            }
        }

        var mouthY = center.Y + head * 0.4f;
        var mouthWidth = head * (0.3f + 0.35f * _wide);
        var mouthHeight = head * 0.4f * _open;
        if (mouthHeight < 2 * context.Scale)
        {
            // Closed: a small smile.
            using var smile = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f * context.Scale, StrokeCap = SKStrokeCap.Round, Color = ink };
            using var arc = new SKPath();
            arc.MoveTo(center.X - mouthWidth / 2, mouthY);
            arc.QuadTo(center.X, mouthY + head * 0.12f, center.X + mouthWidth / 2, mouthY);
            canvas.DrawPath(arc, smile);
        }
        else
        {
            using var mouth = new SKPaint { IsAntialias = true, Color = Mix(ink, context.Output, 0.25f) };
            canvas.DrawOval(center.X, mouthY + mouthHeight / 4, mouthWidth / 2, mouthHeight / 2, mouth);
        }
    }

    private void Blink(double delta, bool reducedMotion)
    {
        if (_blinking > 0)
        {
            _blinking -= delta;
            return;
        }

        _untilBlink -= delta;
        if (_untilBlink > 0)
            return;

        // Under Reduce Motion the eyes stay open; otherwise a blink every two to six seconds.
        _untilBlink = 2 + _random.NextDouble() * 4;
        if (!reducedMotion)
            _blinking = BlinkLength.TotalSeconds;
    }
}
