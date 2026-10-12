using System.Numerics;
using Microsoft.Maui.Platform;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Plugin.Maui.Spine.Core;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WColor = Windows.UI.Color;
using WControl = Microsoft.UI.Xaml.Controls.Control;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Draws a highlight in Composition visuals above the element's content, in the element's shared visual
/// container (<see cref="ElementVisuals"/>) just below Tap's press fill. The animations run on the compositor.
/// </summary>
/// <remarks>
/// Composition has no conic gradient, so Edge's rim is a turning linear gradient, and its glow and
/// Breathe's halo are a one-colour <see cref="DropShadow"/>: close to the other platforms, not the same.
/// </remarks>
internal sealed class HighlightRenderer(object platformView) : IDisposable
{
    const int Order = ElementVisuals.TapOrder - 1_000;
    static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    readonly FrameworkElement? _host = platformView as FrameworkElement;
    ContainerVisual? _root;
    HighlightLook? _look;

    public void Show(HighlightLook look)
    {
        if (_host is null || (_root is not null && look.SameAs(_look)))
            return;

        // A new size or colour while it shows: no fade, so the change does not blink.
        var replacing = _root is not null;
        Remove();
        _look = look;

        var compositor = ElementCompositionPreview.GetElementVisual(_host).Compositor;
        var root = compositor.CreateContainerVisual();
        root.Size = new Vector2((float)look.Size.Width, (float)look.Size.Height);
        root.Opacity = replacing ? 1 : 0;

        var radius = (float)look.Radius(_host is WControl control ? control.CornerRadius.TopLeft : 0);
        switch (look.Kind)
        {
            case HighlightKind.Edge: BuildEdge(compositor, root, look, radius); break;
            case HighlightKind.Pulse: BuildPulse(compositor, root, look, radius); break;
            case HighlightKind.Sheen: BuildSheen(compositor, root, look, radius); break;
            case HighlightKind.Trace: BuildTrace(compositor, root, look, radius); break;
            case HighlightKind.Breathe: BuildBreathe(compositor, root, look, radius); break;
        }

        ElementVisuals.Add(_host, root, Order);
        if (!replacing)
            root.StartAnimation("Opacity", Fade(compositor, 0, 1));
        _root = root;
    }

    public void Hide(bool animated)
    {
        if (_root is not { } root || _host is not { } host)
            return;

        _root = null;
        _look = null;

        if (!animated)
        {
            Remove(host, root);
            return;
        }

        var batch = root.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        root.StartAnimation("Opacity", Fade(root.Compositor, root.Opacity, 0));
        batch.End();
        batch.Completed += (_, _) => Remove(host, root);
    }

    public void Dispose() => Remove();

    /// <summary>Whether any of the element is inside its window, not scrolled out of it.</summary>
    public bool IsInWindow()
    {
        if (_host is not { XamlRoot: { } root, IsLoaded: true } host || root.Content is not { } content)
            return false;

        var bounds = host.TransformToVisual(content).TransformBounds(new Windows.Foundation.Rect(0, 0, host.ActualWidth, host.ActualHeight));
        return bounds.Right > 0 && bounds.Bottom > 0 && bounds.Left < root.Size.Width && bounds.Top < root.Size.Height;
    }

    void Remove()
    {
        if (_root is { } root && _host is { } host)
            Remove(host, root);

        _root = null;
        _look = null;
    }

    static void Remove(FrameworkElement host, ContainerVisual root)
    {
        ElementVisuals.Remove(host, root);
        root.Dispose();
    }

    // ── Edge ──

    static void BuildEdge(Compositor compositor, ContainerVisual root, HighlightLook look, float radius)
    {
        var size = root.Size;
        var rim = (float)Math.Max(1, 2.5 * look.Intensity);
        var brush = TurningGradient(compositor, look, size);

        var shape = compositor.CreateShapeVisual();
        shape.Size = size;
        shape.Shapes.Add(Stroke(compositor, RoundedRect(compositor, new Vector2(rim / 2), size - new Vector2(rim), radius - rim / 2), brush, rim));

        // The glow: a one-colour shadow of the rim, since Composition cannot blur a gradient without Win2D.
        var layer = compositor.CreateLayerVisual();
        layer.Size = size;
        layer.Children.InsertAtTop(shape);
        if (look.Outset > 0 && look.Intensity > 0)
        {
            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = (float)(look.Outset * 0.9);
            shadow.Color = ToWindows(look.Colors[Math.Min(1, look.Colors.Count - 1)]);
            shadow.Opacity = (float)(0.85 * look.Intensity);
            shadow.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
            layer.Shadow = shadow;
        }

        root.Children.InsertAtTop(layer);

        if (look.Still)
            layer.StartAnimation("Opacity", Breath(compositor, look.Cycle, 1, 0.45f));
    }

    static CompositionLinearGradientBrush TurningGradient(Compositor compositor, HighlightLook look, Vector2 size)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        brush.StartPoint = Vector2.Zero;
        brush.EndPoint = size;
        brush.CenterPoint = size / 2;

        var colors = look.Colors.Append(look.Colors[0]).ToArray();
        for (var i = 0; i < colors.Length; i++)
            brush.ColorStops.Add(compositor.CreateColorGradientStop(i / (float)(colors.Length - 1), ToWindows(colors[i])));

        if (!look.Still)
        {
            var turn = compositor.CreateScalarKeyFrameAnimation();
            turn.InsertKeyFrame(0, 0, compositor.CreateLinearEasingFunction());
            turn.InsertKeyFrame(1, 360, compositor.CreateLinearEasingFunction());
            turn.Duration = look.Cycle;
            turn.IterationBehavior = AnimationIterationBehavior.Forever;
            brush.StartAnimation(nameof(brush.RotationAngleInDegrees), turn);
        }

        return brush;
    }

    // ── Pulse ──

    static void BuildPulse(Compositor compositor, ContainerVisual root, HighlightLook look, float radius)
    {
        var size = root.Size;
        var grow = (float)Math.Max(4, look.Outset);
        var width = (float)Math.Max(1, 2 * look.Intensity);
        var color = ToWindows(look.Primary);

        var shape = compositor.CreateShapeVisual();
        shape.Size = size + new Vector2(4 * grow);
        shape.Offset = new Vector3(-2 * grow, -2 * grow, 0);
        var origin = new Vector2(2 * grow);

        if (look.Still)
        {
            var reach = grow * 0.4f;
            var brush = compositor.CreateColorBrush(WithAlpha(color, 0.6 * look.Intensity));
            shape.Shapes.Add(Stroke(compositor, RoundedRect(compositor, origin - new Vector2(reach), size + new Vector2(2 * reach), radius + reach), brush, width));
            root.Children.InsertAtTop(shape);
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            var geometry = RoundedRect(compositor, origin, size, radius);
            var brush = compositor.CreateColorBrush(WithAlpha(color, 0));
            shape.Shapes.Add(Stroke(compositor, geometry, brush, width));

            var delay = look.Cycle * (0.32 * i);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.6f), new Vector2(0.4f, 1));

            var offset = compositor.CreateVector2KeyFrameAnimation();
            offset.InsertKeyFrame(0, origin);
            offset.InsertKeyFrame(1, origin - new Vector2(grow), easing);
            var extent = compositor.CreateVector2KeyFrameAnimation();
            extent.InsertKeyFrame(0, size);
            extent.InsertKeyFrame(1, size + new Vector2(2 * grow), easing);
            var corner = compositor.CreateVector2KeyFrameAnimation();
            corner.InsertKeyFrame(0, new Vector2(radius));
            corner.InsertKeyFrame(1, new Vector2(radius + grow), easing);
            var fade = compositor.CreateColorKeyFrameAnimation();
            fade.InsertKeyFrame(0, WithAlpha(color, 0.9 * look.Intensity));
            fade.InsertKeyFrame(1, WithAlpha(color, 0), easing);

            foreach (var animation in new KeyFrameAnimation[] { offset, extent, corner, fade })
            {
                animation.Duration = look.Cycle;
                animation.DelayTime = delay;
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
            }

            geometry.StartAnimation(nameof(geometry.Offset), offset);
            geometry.StartAnimation(nameof(geometry.Size), extent);
            geometry.StartAnimation(nameof(geometry.CornerRadius), corner);
            brush.StartAnimation(nameof(brush.Color), fade);
        }

        root.Children.InsertAtTop(shape);
    }

    // ── Sheen ──

    static void BuildSheen(Compositor compositor, ContainerVisual root, HighlightLook look, float radius)
    {
        var size = root.Size;
        var holder = compositor.CreateContainerVisual();
        holder.Size = size;
        holder.Clip = compositor.CreateGeometricClip(RoundedRect(compositor, Vector2.Zero, size, radius));

        var color = ToWindows(look.Primary);
        if (look.Still)
        {
            var tint = compositor.CreateSpriteVisual();
            tint.Size = size;
            tint.Brush = compositor.CreateColorBrush(WithAlpha(color, 0.25 * look.Intensity));
            tint.StartAnimation("Opacity", Breath(compositor, look.Cycle, 0, 1));
            holder.Children.InsertAtTop(tint);
            root.Children.InsertAtTop(holder);
            return;
        }

        var gradient = compositor.CreateLinearGradientBrush();
        gradient.StartPoint = new Vector2(0, 0.35f);
        gradient.EndPoint = new Vector2(1, 0.65f);
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.3f, WithAlpha(color, 0)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, WithAlpha(color, 0.75 * look.Intensity)));
        gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.7f, WithAlpha(color, 0)));

        var band = compositor.CreateSpriteVisual();
        band.Size = size;
        band.Brush = gradient;
        band.Offset = new Vector3(-1.2f * size.X, 0, 0);

        // A 1.3 s sweep after a 0.2 s pause in each cycle.
        var pause = (float)(0.2 / look.Cycle.TotalSeconds);
        var sweep = compositor.CreateScalarKeyFrameAnimation();
        sweep.InsertKeyFrame(0, -1.2f * size.X);
        sweep.InsertKeyFrame(pause, -1.2f * size.X);
        sweep.InsertKeyFrame(Math.Min(1, pause + (float)(1.3 / look.Cycle.TotalSeconds)), 1.2f * size.X,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1)));
        sweep.Duration = look.Cycle;
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        band.StartAnimation("Offset.X", sweep);

        holder.Children.InsertAtTop(band);
        root.Children.InsertAtTop(holder);
    }

    // ── Trace ──

    static void BuildTrace(Compositor compositor, ContainerVisual root, HighlightLook look, float radius)
    {
        var size = root.Size;
        var width = (float)Math.Max(1, 2.5 * look.Intensity);
        var color = ToWindows(look.Primary);
        var shape = compositor.CreateShapeVisual();
        shape.Size = size;

        var track = RoundedRect(compositor, new Vector2(width / 2), size - new Vector2(width), radius - width / 2);
        shape.Shapes.Add(Stroke(compositor, track, compositor.CreateColorBrush(WithAlpha(color, (look.Still ? 0.5 : 0.22) * look.Intensity)), Math.Max(1, width * 0.6f)));

        if (!look.Still)
        {
            // A faint tail and a solid head; a white head reads as a gap on a light card.
            AddComet(compositor, shape, look, size, radius, width, WithAlpha(color, 0.45 * look.Intensity), 0.12f);
            AddComet(compositor, shape, look, size, radius, width, WithAlpha(color, look.Intensity), 0.04f);
        }

        root.Children.InsertAtTop(shape);
    }

    static void AddComet(Compositor compositor, ShapeVisual shape, HighlightLook look, Vector2 size, float radius, float width, WColor color, float length)
    {
        var geometry = RoundedRect(compositor, new Vector2(width / 2), size - new Vector2(width), radius - width / 2);
        geometry.TrimStart = 0.12f - length;
        geometry.TrimEnd = 0.12f;

        var stroke = Stroke(compositor, geometry, compositor.CreateColorBrush(color), width);
        stroke.StrokeStartCap = CompositionStrokeCap.Round;
        stroke.StrokeEndCap = CompositionStrokeCap.Round;
        shape.Shapes.Add(stroke);

        var run = compositor.CreateScalarKeyFrameAnimation();
        run.InsertKeyFrame(0, 0, compositor.CreateLinearEasingFunction());
        run.InsertKeyFrame(1, 1, compositor.CreateLinearEasingFunction());
        run.Duration = look.Cycle;
        run.IterationBehavior = AnimationIterationBehavior.Forever;
        geometry.StartAnimation(nameof(geometry.TrimOffset), run);
    }

    // ── Breathe ──

    static void BuildBreathe(Compositor compositor, ContainerVisual root, HighlightLook look, float radius)
    {
        var size = root.Size;
        var outset = (float)Math.Max(2, look.Outset);
        var color = ToWindows(look.Primary);

        // A thin ring just outside the view, whose shadow is the halo.
        var shape = compositor.CreateShapeVisual();
        shape.Size = size + new Vector2(4);
        shape.Offset = new Vector3(-2, -2, 0);
        shape.Shapes.Add(Stroke(compositor, RoundedRect(compositor, new Vector2(1), size + new Vector2(2), radius + 1), compositor.CreateColorBrush(WithAlpha(color, 0.35 * look.Intensity)), 2));

        var layer = compositor.CreateLayerVisual();
        layer.Size = shape.Size;
        layer.Offset = shape.Offset;
        shape.Offset = Vector3.Zero;
        layer.Children.InsertAtTop(shape);

        var shadow = compositor.CreateDropShadow();
        shadow.Color = color;
        shadow.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        shadow.BlurRadius = look.Still ? outset * 0.5f : outset * 0.2f;
        shadow.Opacity = (float)((look.Still ? 0.5 : 0.25) * look.Intensity);
        layer.Shadow = shadow;

        if (!look.Still)
        {
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
            var blur = compositor.CreateScalarKeyFrameAnimation();
            blur.InsertKeyFrame(0, outset * 0.2f);
            blur.InsertKeyFrame(0.5f, outset * 0.75f, easing);
            blur.InsertKeyFrame(1, outset * 0.2f, easing);
            var glow = compositor.CreateScalarKeyFrameAnimation();
            glow.InsertKeyFrame(0, (float)(0.25 * look.Intensity));
            glow.InsertKeyFrame(0.5f, (float)(0.9 * look.Intensity), easing);
            glow.InsertKeyFrame(1, (float)(0.25 * look.Intensity), easing);
            foreach (var animation in new[] { blur, glow })
            {
                animation.Duration = look.Cycle;
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
            }

            shadow.StartAnimation(nameof(shadow.BlurRadius), blur);
            shadow.StartAnimation(nameof(shadow.Opacity), glow);
        }

        root.Children.InsertAtTop(layer);
    }

    // ── Helpers ──

    static CompositionRoundedRectangleGeometry RoundedRect(Compositor compositor, Vector2 offset, Vector2 size, float radius)
    {
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Offset = offset;
        geometry.Size = Vector2.Max(size, Vector2.Zero);
        geometry.CornerRadius = new Vector2(Math.Clamp(radius, 0, Math.Min(size.X, size.Y) / 2));
        return geometry;
    }

    static CompositionSpriteShape Stroke(Compositor compositor, CompositionGeometry geometry, CompositionBrush brush, float thickness)
    {
        var stroke = compositor.CreateSpriteShape(geometry);
        stroke.StrokeBrush = brush;
        stroke.StrokeThickness = thickness;
        return stroke;
    }

    static ScalarKeyFrameAnimation Fade(Compositor compositor, float from, float to)
    {
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, from);
        fade.InsertKeyFrame(1, to);
        fade.Duration = FadeDuration;
        return fade;
    }

    /// <summary>A value that swings between two ends and back once per cycle, for the still forms.</summary>
    static ScalarKeyFrameAnimation Breath(Compositor compositor, TimeSpan cycle, float from, float to)
    {
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
        var breath = compositor.CreateScalarKeyFrameAnimation();
        breath.InsertKeyFrame(0, from);
        breath.InsertKeyFrame(0.5f, to, easing);
        breath.InsertKeyFrame(1, from, easing);
        breath.Duration = cycle;
        breath.IterationBehavior = AnimationIterationBehavior.Forever;
        return breath;
    }

    static WColor ToWindows(Color color) =>
        WColor.FromArgb((byte)(color.Alpha * 255), (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255));

    static WColor WithAlpha(WColor color, double alpha) =>
        WColor.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
}
