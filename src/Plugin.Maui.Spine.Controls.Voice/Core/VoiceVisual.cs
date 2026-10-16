using Plugin.Maui.Spine.Voice;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>What a visual needs beside the frame: its colours, the pixel scale, the time since the last draw, and Reduce Motion.</summary>
/// <param name="Input">The colour for the microphone (the user).</param>
/// <param name="Output">The colour for the answer (the model).</param>
/// <param name="Scale">Pixels per point, for line widths that look the same on every screen.</param>
/// <param name="Delta">The time since the last draw, for smoothing and timers that run at any frame rate.</param>
/// <param name="ReducedMotion">The user asked for less motion: draw the state and a slow level, not the spectrum.</param>
public readonly record struct VoiceVisualContext(SKColor Input, SKColor Output, float Scale, TimeSpan Delta, bool ReducedMotion);

/// <summary>
/// A visual for <c>VoiceView</c> that draws one frame on a SkiaSharp canvas. One instance is made per view, so it can keep
/// its own smoothing and timers between frames. It knows nothing of the provider, the transport or the microphone.
/// </summary>
/// <example>
/// <code>
/// public sealed class Pulse : VoiceVisual
/// {
///     public override void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context)
///     {
///         using var paint = new SKPaint { Color = context.Output, IsAntialias = true };
///         canvas.DrawCircle(bounds.MidX, bounds.MidY, bounds.Width / 4 * (1 + frame.OutputLevel), paint);
///     }
/// }
///
/// builder.UseSpineVoiceView(o => o.AddVisual&lt;Pulse&gt;("pulse"));
/// </code>
/// </example>
public abstract class VoiceVisual
{
    /// <summary>Draws <paramref name="frame"/> into <paramref name="bounds"/>, in pixels. The canvas is cleared before.</summary>
    /// <remarks>The frame is reused for the next tick: read it here, and copy what you keep (<see cref="VoiceFrame.Snapshot"/>).</remarks>
    public abstract void Draw(SKCanvas canvas, SKRect bounds, VoiceFrame frame, VoiceVisualContext context);

    /// <summary>Eases <paramref name="current"/> toward <paramref name="target"/> with a time constant, at any frame rate.</summary>
    protected static float Ease(float current, float target, TimeSpan delta, TimeSpan constant) =>
        current + (target - current) * (float)(1 - Math.Exp(-delta.TotalSeconds / Math.Max(constant.TotalSeconds, 1e-6)));

    /// <summary>Mixes two colours, 0 for <paramref name="a"/> and 1 for <paramref name="b"/>.</summary>
    protected static SKColor Mix(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    /// <summary>How much the answer, rather than the user, colours the visual for a state: 1 speaking, 0 listening.</summary>
    protected static float Speaker(VoiceState state) => state switch
    {
        VoiceState.Speaking => 1f,
        VoiceState.Thinking => 0.5f,
        VoiceState.Interrupted => 0.25f,
        _ => 0f,
    };

    /// <summary>Whether the conversation is live (not idle, connecting or muted), for visuals that rest otherwise.</summary>
    protected static bool IsLive(VoiceState state) => state is VoiceState.Listening or VoiceState.Thinking or VoiceState.Speaking or VoiceState.Interrupted;
}
