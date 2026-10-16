using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Duplex bars: the answer's 24 bands grow up from the middle and the microphone's down, so a user talking over the answer
/// shows as both at once. Under Reduce Motion, two level bars instead of the spectrum. <c>bars</c>.
/// </summary>
public sealed class BarsVisual : VoiceVisual
{
    private static readonly TimeSpan Smoothing = TimeSpan.FromMilliseconds(70);

    private readonly float[] _up = new float[VoiceFrame.BandCount];
    private readonly float[] _down = new float[VoiceFrame.BandCount];
    private float _outputLevel;
    private float _inputLevel;

    /// <inheritdoc />
    public override void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)
    {
        var live = IsLive(frame.State);
        var middle = bounds.MidY;
        var half = bounds.Height / 2 - 2 * context.Scale;
        var muted = frame.State == VoiceState.Muted;

        using var line = new SKPaint { Color = context.Input.WithAlpha(50), StrokeWidth = context.Scale, IsAntialias = true };
        canvas.DrawLine(bounds.Left, middle, bounds.Right, middle, line);

        using var up = new SKPaint { Color = context.Output, IsAntialias = true };
        using var down = new SKPaint { Color = muted ? context.Input.WithAlpha(70) : context.Input, IsAntialias = true };

        if (context.ReducedMotion)
        {
            _outputLevel = Ease(_outputLevel, live ? frame.OutputLevel : 0, context.Delta, TimeSpan.FromMilliseconds(400));
            _inputLevel = Ease(_inputLevel, live ? frame.InputLevel : 0, context.Delta, TimeSpan.FromMilliseconds(400));
            var width = bounds.Width * 0.18f;
            var radius = width / 2;
            canvas.DrawRoundRect(SKRect.Create(bounds.MidX - width / 2, middle - half * _outputLevel, width, half * _outputLevel), radius, radius, up);
            canvas.DrawRoundRect(SKRect.Create(bounds.MidX - width / 2, middle, width, half * _inputLevel), radius, radius, down);
            return;
        }

        var gap = Math.Max(2 * context.Scale, bounds.Width * 0.008f);
        var bar = (bounds.Width - gap * (VoiceFrame.BandCount - 1)) / VoiceFrame.BandCount;
        var corner = Math.Min(bar / 2, 3 * context.Scale);
        for (var b = 0; b < VoiceFrame.BandCount; b++)
        {
            _up[b] = Ease(_up[b], live ? frame.OutputBands[b] : 0, context.Delta, Smoothing);
            _down[b] = Ease(_down[b], live ? frame.InputBands[b] : 0, context.Delta, Smoothing);
            var x = bounds.Left + b * (bar + gap);
            // A sliver even at silence, so the bars read as bars.
            var upHeight = Math.Max(half * _up[b], context.Scale);
            var downHeight = Math.Max(half * _down[b], context.Scale);
            canvas.DrawRoundRect(SKRect.Create(x, middle - upHeight, bar, upHeight), corner, corner, up);
            canvas.DrawRoundRect(SKRect.Create(x, middle, bar, downHeight), corner, corner, down);
        }
    }
}
