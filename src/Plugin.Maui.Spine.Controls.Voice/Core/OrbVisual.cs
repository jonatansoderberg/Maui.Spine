using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A soft blob that breathes with the level and wobbles with the bands: cool while the user talks, warm while the model
/// speaks, a slow pulse while it thinks. <c>orb</c>.
/// </summary>
public sealed class OrbVisual : VoiceVisual
{
    private const int Points = 72;
    private static readonly TimeSpan Smoothing = TimeSpan.FromMilliseconds(90);

    private readonly float[] _lobes = new float[6];
    private float _level;
    private float _warmth;
    private double _time;

    /// <inheritdoc />
    public override void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)
    {
        _time += context.Delta.TotalSeconds;
        var live = IsLive(frame.State);
        var speaking = Speaker(frame.State);
        var level = live ? Math.Max(frame.InputLevel * (1 - speaking), frame.OutputLevel * speaking) : 0f;
        _level = Ease(_level, level, context.Delta, Smoothing);
        _warmth = Ease(_warmth, speaking, context.Delta, TimeSpan.FromMilliseconds(250));

        // Six groups of four bands make the lobes; the answer's bands while it speaks, the microphone's otherwise.
        var bands = speaking >= 0.5f ? frame.OutputBands : frame.InputBands;
        for (var i = 0; i < _lobes.Length; i++)
        {
            var sum = 0f;
            for (var b = i * 4; b < i * 4 + 4; b++)
                sum += bands[b];
            _lobes[i] = Ease(_lobes[i], context.ReducedMotion ? 0 : sum / 4, context.Delta, Smoothing);
        }

        var size = Math.Min(bounds.Width, bounds.Height);
        var center = new SKPoint(bounds.MidX, bounds.MidY);
        var pulse = frame.State == VoiceState.Thinking && !context.ReducedMotion ? 0.05f * (float)Math.Sin(_time * Math.Tau / 1.6) : 0f;
        var radius = size * (live ? 0.28f : 0.22f) * (1 + 0.28f * _level + pulse);

        var color = live ? Mix(context.Input, context.Output, _warmth) : Mix(context.Input, SKColors.Gray, 0.6f);
        if (frame.State == VoiceState.Muted)
            color = color.WithAlpha(110);

        using var path = new SKPath();
        for (var p = 0; p <= Points; p++)
        {
            var angle = p * Math.Tau / Points;
            var wobble = 0f;
            for (var i = 0; i < _lobes.Length; i++)
                wobble += _lobes[i] * (float)Math.Sin(angle * (i + 2) + _time * (0.6 + i * 0.25));
            var r = radius * (1 + 0.07f * wobble);
            var point = new SKPoint(center.X + r * (float)Math.Cos(angle), center.Y + r * (float)Math.Sin(angle));
            if (p == 0)
                path.MoveTo(point);
            else
                path.LineTo(point);
        }
        path.Close();

        // A glow behind, stronger with the level, then the body with a lighter centre.
        // A paint does not dispose its filter or shader: they are disposed here, or each frame leaks native memory.
        using (var blur = SKImageFilter.CreateBlur(radius * 0.35f, radius * 0.35f))
        using (var glow = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)(60 + 120 * _level)), ImageFilter = blur })
            canvas.DrawPath(path, glow);

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(center.X - radius * 0.25f, center.Y - radius * 0.3f), radius * 1.4f,
            [Mix(color, SKColors.White, 0.45f), color, Mix(color, SKColors.Black, 0.25f)],
            [0f, 0.55f, 1f], SKShaderTileMode.Clamp);
        using var body = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawPath(path, body);
    }
}
