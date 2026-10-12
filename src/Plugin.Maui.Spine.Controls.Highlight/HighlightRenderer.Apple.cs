#if IOS || MACCATALYST

using CoreAnimation;
using CoreGraphics;
using CoreImage;
using Foundation;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Draws a highlight in Core Animation layers on the view's own layer, above its content and just below
/// Tap's press fill. The animations run in the render server, with no C# per frame.
/// </summary>
internal sealed class HighlightRenderer(object platformView) : IDisposable
{
    // Tap's press overlay sits at 10 000, so a press still shows on a pulsing button.
    const float ZPosition = 9_000;
    const double FadeSeconds = 0.25;

    readonly UIView? _host = platformView as UIView;
    CALayer? _root;
    HighlightLook? _look;
    double _epoch;

    public void Show(HighlightLook look)
    {
        if (_host is null || (_root is not null && look.SameAs(_look)))
            return;

        // A new size or colour while it shows: the new layers take over the running ones' phase, with no
        // fade, so the change does not blink.
        var replacing = _root is not null;
        if (!replacing)
            _epoch = CAAnimation.CurrentMediaTime();

        Remove();
        _look = look;

        var root = new CALayer
        {
            Frame = new CGRect(0, 0, look.Size.Width, look.Size.Height),
            ZPosition = ZPosition,
            MasksToBounds = false,
        };

        var radius = look.Radius(_host.Layer.CornerRadius);
        switch (look.Kind)
        {
            case HighlightKind.Edge: BuildEdge(root, look, radius); break;
            case HighlightKind.Pulse: BuildPulse(root, look, radius); break;
            case HighlightKind.Sheen: BuildSheen(root, look, radius); break;
            case HighlightKind.Trace: BuildTrace(root, look, radius); break;
            case HighlightKind.Breathe: BuildBreathe(root, look, radius); break;
        }

        Anchor(root, _epoch);
        if (!replacing)
            root.AddAnimation(Fade(0, 1), "spine.highlight.fade");
        _host.Layer.AddSublayer(root);
        _root = root;
    }

    public void Hide(bool animated)
    {
        if (_root is not { } root)
            return;

        _root = null;
        _look = null;

        if (!animated)
        {
            root.RemoveFromSuperLayer();
            return;
        }

        CATransaction.Begin();
        CATransaction.CompletionBlock = root.RemoveFromSuperLayer;
        root.Opacity = 0;
        root.AddAnimation(Fade(root.PresentationLayer?.Opacity ?? 1, 0), "spine.highlight.fade");
        CATransaction.Commit();
    }

    public void Dispose() => Remove();

    /// <summary>Whether any of the view is inside its window, not scrolled or moved out of it.</summary>
    public bool IsInWindow() =>
        _host?.Window is { } window
        && window.Bounds.IntersectsWith(_host.ConvertRectToView(_host.Bounds, null));

    void Remove()
    {
        _root?.RemoveFromSuperLayer();
        _root = null;
        _look = null;
    }

    // ── Edge: a conic gradient that turns, seen through the rim and a blurred ring round it ──

    static void BuildEdge(CALayer root, HighlightLook look, double radius)
    {
        var size = look.Size;
        var rim = Math.Max(1, 2.5 * look.Intensity);
        var outset = look.Outset;

        // The glow: the same turning colours, seen through a blurred ring made once for this size.
        if (outset > 0 && look.Intensity > 0)
        {
            var glowFrame = new CGRect(-outset, -outset, size.Width + 2 * outset, size.Height + 2 * outset);
            var glow = new CALayer { Frame = glowFrame, Opacity = (float)(0.85 * look.Intensity) };
            if (BlurredRing(glowFrame.Size, outset, radius, 2 + 5 * look.Intensity) is { } ring)
            {
                glow.Mask = new CALayer
                {
                    Frame = new CGRect(CGPoint.Empty, glowFrame.Size),
                    Contents = ring,
                    ContentsScale = UIScreen.MainScreen.Scale,
                };
                glow.AddSublayer(Conic(look, glowFrame.Size));
                root.AddSublayer(glow);
            }
        }

        // The rim: inside the view's edge, as a border would be.
        var rimLayer = new CALayer { Frame = root.Bounds };
        rimLayer.Mask = new CAShapeLayer
        {
            Frame = root.Bounds,
            Path = RoundedRect(new CGRect(CGPoint.Empty, root.Bounds.Size).Inset((nfloat)(rim / 2), (nfloat)(rim / 2)), radius - rim / 2),
            LineWidth = (nfloat)rim,
            StrokeColor = UIColor.Black.CGColor,
            FillColor = UIColor.Clear.CGColor,
        };
        rimLayer.AddSublayer(Conic(look, root.Bounds.Size));
        root.AddSublayer(rimLayer);

        if (look.Still)
            root.AddAnimation(Breath(look.Cycle, "opacity", 1f, 0.45f), "spine.highlight.still");
    }

    /// <summary>A square conic gradient round the centre of <paramref name="area"/>, turning once per cycle unless still.</summary>
    static CAGradientLayer Conic(HighlightLook look, CGSize area)
    {
        var side = Math.Sqrt(area.Width * area.Width + area.Height * area.Height);
        var colors = look.Colors.Select(color => color.ToCGColor()).Append(look.Colors[0].ToCGColor()).ToArray();
        var gradient = new CAGradientLayer
        {
            LayerType = CAGradientLayerType.Conic,
            Frame = new CGRect((area.Width - side) / 2, (area.Height - side) / 2, side, side),
            StartPoint = new CGPoint(0.5, 0.5),
            EndPoint = new CGPoint(0.5, 0),
            Colors = colors,
        };

        if (!look.Still)
        {
            gradient.AddAnimation(new CABasicAnimation
            {
                KeyPath = "transform.rotation.z",
                From = NSNumber.FromDouble(0),
                To = NSNumber.FromDouble(2 * Math.PI),
                Duration = look.Cycle.TotalSeconds,
                RepeatCount = float.PositiveInfinity,
                RemovedOnCompletion = false,
            }, "spine.highlight.turn");
        }

        return gradient;
    }

    /// <summary>
    /// A ring along the rounded edge, blurred so it fades both ways; drawn once per size because iOS has
    /// no blur filter on layers.
    /// </summary>
    static CGImage? BlurredRing(CGSize size, double outset, double radius, double width)
    {
        var scale = UIScreen.MainScreen.Scale;
        var format = new UIGraphicsImageRendererFormat { Scale = scale, Opaque = false };
        var renderer = new UIGraphicsImageRenderer(size, format);
        var image = renderer.CreateImage(context =>
        {
            var edge = new CGRect(outset, outset, size.Width - 2 * outset, size.Height - 2 * outset);
            var path = UIBezierPath.FromRoundedRect(edge, (nfloat)radius);
            path.LineWidth = (nfloat)width;
            UIColor.Black.SetStroke();
            path.Stroke();
        });

        if (image.CGImage is not { } drawn)
            return null;

        using var input = new CIImage(drawn);
        using var blur = new CIGaussianBlur { InputImage = input, Radius = (float)(outset * 0.45 * scale) };
        using var output = blur.OutputImage;
        if (output is null)
            return null;

        using var context = CIContext.Create();
        return context.CreateCGImage(output, input.Extent);
    }

    // ── Pulse: two rings that grow and fade, the second half a cycle behind ──

    static void BuildPulse(CALayer root, HighlightLook look, double radius)
    {
        var bounds = root.Bounds;
        var grow = Math.Max(4, look.Outset);
        var width = (nfloat)Math.Max(1, 2 * look.Intensity);
        var color = look.Primary.ToCGColor();

        if (look.Still)
        {
            var reach = grow * 0.4;
            root.AddSublayer(new CAShapeLayer
            {
                Frame = bounds,
                Path = RoundedRect(bounds.Inset((nfloat)(-reach), (nfloat)(-reach)), radius + reach),
                LineWidth = width,
                StrokeColor = color,
                FillColor = UIColor.Clear.CGColor,
                Opacity = (float)(0.6 * look.Intensity),
            });
            return;
        }

        var start = RoundedRect(bounds, radius);
        var end = RoundedRect(bounds.Inset((nfloat)(-grow), (nfloat)(-grow)), radius + grow);
        for (var i = 0; i < 2; i++)
        {
            var ring = new CAShapeLayer
            {
                Frame = bounds,
                Path = start,
                LineWidth = width,
                StrokeColor = color,
                FillColor = UIColor.Clear.CGColor,
                Opacity = 0,
            };

            var path = new CABasicAnimation { KeyPath = "path" };
            path.SetFrom(start);
            path.SetTo(end);
            var fade = new CABasicAnimation { KeyPath = "opacity", From = NSNumber.FromFloat((float)(0.9 * look.Intensity)), To = NSNumber.FromFloat(0) };
            ring.AddAnimation(new CAAnimationGroup
            {
                Animations = [path, fade],
                Duration = look.Cycle.TotalSeconds,
                BeginTime = i * look.Cycle.TotalSeconds * 0.32,
                RepeatCount = float.PositiveInfinity,
                TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseOut),
                RemovedOnCompletion = false,
            }, "spine.highlight.ring");
            root.AddSublayer(ring);
        }
    }

    // ── Sheen: a band of light that crosses once per cycle, clipped to the view's shape ──

    static void BuildSheen(CALayer root, HighlightLook look, double radius)
    {
        var bounds = root.Bounds;
        var holder = new CALayer
        {
            Frame = bounds,
            Mask = new CAShapeLayer { Frame = bounds, Path = RoundedRect(bounds, radius), FillColor = UIColor.Black.CGColor },
        };

        if (look.Still)
        {
            holder.BackgroundColor = look.Primary.WithAlpha((float)(0.25 * look.Intensity)).ToCGColor();
            holder.AddAnimation(Breath(look.Cycle, "opacity", 0f, 1f), "spine.highlight.still");
            root.AddSublayer(holder);
            return;
        }

        var band = new CAGradientLayer
        {
            Frame = bounds,
            StartPoint = new CGPoint(0, 0.35),
            EndPoint = new CGPoint(1, 0.65),
            Colors = [look.Primary.WithAlpha(0).ToCGColor(), look.Primary.WithAlpha((float)(0.75 * look.Intensity)).ToCGColor(), look.Primary.WithAlpha(0).ToCGColor()],
            Locations = [NSNumber.FromDouble(0.3), NSNumber.FromDouble(0.5), NSNumber.FromDouble(0.7)],
        };

        var sweep = new CABasicAnimation
        {
            KeyPath = "transform.translation.x",
            From = NSNumber.FromDouble(-1.2 * bounds.Width),
            To = NSNumber.FromDouble(1.2 * bounds.Width),
            BeginTime = 0.2,
            Duration = 1.3,
            TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut),
            FillMode = CAFillMode.Both,
        };
        band.AddAnimation(new CAAnimationGroup
        {
            Animations = [sweep],
            Duration = look.Cycle.TotalSeconds,
            RepeatCount = float.PositiveInfinity,
            RemovedOnCompletion = false,
        }, "spine.highlight.sweep");
        band.Transform = CATransform3D.MakeTranslation(-1.2f * (float)bounds.Width, 0, 0);

        holder.AddSublayer(band);
        root.AddSublayer(holder);
    }

    // ── Trace: a point of light with a tail running along the border ──

    static void BuildTrace(CALayer root, HighlightLook look, double radius)
    {
        var bounds = root.Bounds;
        var width = Math.Max(1, 2.5 * look.Intensity);
        var edge = bounds.Inset((nfloat)(width / 2), (nfloat)(width / 2));
        var loop = RoundedRect(edge, radius - width / 2);

        root.AddSublayer(new CAShapeLayer
        {
            Frame = bounds,
            Path = loop,
            LineWidth = (nfloat)Math.Max(1, width * 0.6),
            StrokeColor = look.Primary.WithAlpha((float)((look.Still ? 0.5 : 0.22) * look.Intensity)).ToCGColor(),
            FillColor = UIColor.Clear.CGColor,
        });

        if (look.Still)
            return;

        // The loop drawn twice, so a segment can run past the start without a jump.
        var twice = new CGPath();
        twice.AddPath(loop);
        twice.AddPath(loop);

        // A faint tail and a solid head; a white head, as on the concept page, reads as a gap on a light card.
        AddComet(root, twice, look, (nfloat)width, look.Primary.WithAlpha((float)(0.45 * look.Intensity)), 0.12);
        AddComet(root, twice, look, (nfloat)width, look.Primary.WithAlpha((float)look.Intensity), 0.04);
    }

    static void AddComet(CALayer root, CGPath path, HighlightLook look, nfloat width, Color color, double length)
    {
        var comet = new CAShapeLayer
        {
            Frame = root.Bounds,
            Path = path,
            LineWidth = width,
            LineCap = CAShapeLayer.CapRound,
            StrokeColor = color.ToCGColor(),
            FillColor = UIColor.Clear.CGColor,
            StrokeStart = 0,
            StrokeEnd = 0,
        };

        // The head is at the same place for both comets; a shorter one is the bright tip of the longer.
        var head = new CABasicAnimation { KeyPath = "strokeEnd", From = NSNumber.FromDouble(0.12), To = NSNumber.FromDouble(0.62) };
        var tail = new CABasicAnimation { KeyPath = "strokeStart", From = NSNumber.FromDouble(0.12 - length), To = NSNumber.FromDouble(0.62 - length) };
        comet.AddAnimation(new CAAnimationGroup
        {
            Animations = [head, tail],
            Duration = look.Cycle.TotalSeconds,
            RepeatCount = float.PositiveInfinity,
            RemovedOnCompletion = false,
        }, "spine.highlight.trace");
        root.AddSublayer(comet);
    }

    // ── Breathe: a halo outside the view that swells and fades ──

    static void BuildBreathe(CALayer root, HighlightLook look, double radius)
    {
        var bounds = root.Bounds;
        var outset = Math.Max(2, look.Outset);
        var halo = new CALayer
        {
            Frame = bounds,
            ShadowPath = RoundedRect(bounds, radius),
            ShadowColor = look.Primary.ToCGColor(),
            ShadowOffset = CGSize.Empty,
            ShadowRadius = (nfloat)(look.Still ? outset * 0.5 : outset * 0.2),
            ShadowOpacity = (float)((look.Still ? 0.5 : 0.25) * look.Intensity),
        };

        // Only outside the view: the shadow would tint its content too.
        var margin = outset * 2;
        var maskFrame = bounds.Inset((nfloat)(-margin), (nfloat)(-margin));
        var cutout = new CGPath();
        cutout.AddRect(new CGRect(CGPoint.Empty, maskFrame.Size));
        cutout.AddPath(RoundedRect(new CGRect(margin, margin, bounds.Width, bounds.Height), radius));
        halo.Mask = new CAShapeLayer
        {
            Frame = maskFrame,
            Path = cutout,
            FillRule = CAShapeLayer.FillRuleEvenOdd,
            FillColor = UIColor.Black.CGColor,
        };

        if (!look.Still)
        {
            var swell = new CABasicAnimation
            {
                KeyPath = "shadowRadius",
                From = NSNumber.FromDouble(outset * 0.2),
                To = NSNumber.FromDouble(outset * 0.75),
            };
            var glow = new CABasicAnimation
            {
                KeyPath = "shadowOpacity",
                From = NSNumber.FromFloat((float)(0.25 * look.Intensity)),
                To = NSNumber.FromFloat((float)(0.9 * look.Intensity)),
            };
            halo.AddAnimation(new CAAnimationGroup
            {
                Animations = [swell, glow],
                Duration = look.Cycle.TotalSeconds / 2,
                AutoReverses = true,
                RepeatCount = float.PositiveInfinity,
                TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut),
                RemovedOnCompletion = false,
            }, "spine.highlight.breathe");
        }

        root.AddSublayer(halo);
    }

    // ── Helpers ──

    /// <summary>
    /// Starts every repeating animation in <paramref name="layer"/>'s tree at <paramref name="epoch"/>, plus
    /// its own delay, so layers built later continue where the earlier ones were.
    /// </summary>
    static void Anchor(CALayer layer, double epoch)
    {
        foreach (var key in layer.AnimationKeys ?? [])
        {
            if (layer.AnimationForKey(key) is not { } animation || !float.IsPositiveInfinity(animation.RepeatCount))
                continue;

            var anchored = (CAAnimation)animation.Copy();
            anchored.BeginTime = epoch + animation.BeginTime;
            layer.RemoveAnimation(key);
            layer.AddAnimation(anchored, key);
        }

        foreach (var sublayer in layer.Sublayers ?? [])
            Anchor(sublayer, epoch);
    }

    static CGPath RoundedRect(CGRect rect, double radius) =>
        UIBezierPath.FromRoundedRect(rect, (nfloat)Math.Clamp(radius, 0, Math.Min(rect.Width, rect.Height) / 2)).CGPath!;

    static CABasicAnimation Fade(float from, float to) => new()
    {
        KeyPath = "opacity",
        From = NSNumber.FromFloat(from),
        To = NSNumber.FromFloat(to),
        Duration = FadeSeconds,
    };

    /// <summary>A value that swings between two ends and back once per cycle, for the still forms.</summary>
    static CABasicAnimation Breath(TimeSpan cycle, string keyPath, float from, float to) => new()
    {
        KeyPath = keyPath,
        From = NSNumber.FromFloat(from),
        To = NSNumber.FromFloat(to),
        Duration = cycle.TotalSeconds / 2,
        AutoReverses = true,
        RepeatCount = float.PositiveInfinity,
        TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut),
        RemovedOnCompletion = false,
    };
}

#endif
