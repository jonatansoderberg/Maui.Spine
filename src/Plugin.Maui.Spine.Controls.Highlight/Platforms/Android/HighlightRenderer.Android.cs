using Android.Animation;
using Android.Graphics;
using Android.Graphics.Drawables;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using Rect = Android.Graphics.Rect;
using AColor = Android.Graphics.Color;
using APath = Android.Graphics.Path;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;
using PointF = Android.Graphics.PointF;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Draws a highlight as one drawable in the overlay of the view's parent, at the view's place: an overlay is
/// drawn after the content without touching layout or the view's <c>Foreground</c> (Tap's ripple), and the
/// parent's lets halos reach outside the view, which a Border clips to its shape. A
/// <see cref="TimeAnimator"/> invalidates only that drawable on each frame.
/// </summary>
internal sealed class HighlightRenderer(object platformView) : IDisposable
{
    const long FadeMilliseconds = 250;

    readonly AView? _host = platformView as AView;
    AView? _holder;
    bool _following;
    HighlightDrawable? _drawable;
    TimeAnimator? _ticker;
    ValueAnimator? _fade;
    HighlightLook? _look;
    long _epoch;

    public void Show(HighlightLook look)
    {
        if (_host is null || (_drawable is not null && look.SameAs(_look)))
            return;

        // A new size or colour while it shows: the new drawable takes over the running one's phase, with no
        // fade, so the change does not blink.
        var replacing = _drawable is not null;
        if (!replacing)
            _epoch = Android.OS.SystemClock.UptimeMillis();

        Remove();
        _look = look;

        var density = _host.Resources?.DisplayMetrics?.Density ?? 1;
        var width = _host.Width > 0 ? _host.Width : (int)(look.Size.Width * density);
        var height = _host.Height > 0 ? _host.Height : (int)(look.Size.Height * density);
        var drawable = new HighlightDrawable(look, width, height, density)
        {
            Alpha = replacing ? 255 : 0,
            Elapsed = TimeSpan.FromMilliseconds(Android.OS.SystemClock.UptimeMillis() - _epoch),
        };

        _holder = _host.Parent as Android.Views.ViewGroup ?? _host;
        Place(drawable);
        _holder.Overlay?.Add(drawable);
        _drawable = drawable;
        if (!_following)
        {
            _host.LayoutChange += OnHostLayoutChange;
            _following = true;
        }

        if (!look.Still)
        {
            _ticker = new TimeAnimator();
            _ticker.SetTimeListener(new Ticker(drawable, _epoch));
            _ticker.Start();
        }

        if (!replacing)
            _fade = Fade(drawable, 255, onEnd: null);
    }

    public void Hide(bool animated)
    {
        if (_drawable is not { } drawable)
            return;

        var ticker = _ticker;
        _drawable = null;
        _ticker = null;
        _look = null;
        _fade?.Cancel();

        if (!animated)
        {
            Remove(drawable, ticker);
            return;
        }

        _fade = Fade(drawable, 0, onEnd: () => Remove(drawable, ticker));
    }

    public void Dispose()
    {
        Remove();
        if (_following && _host is not null)
            _host.LayoutChange -= OnHostLayoutChange;
        _following = false;
    }

    /// <summary>Whether any of the view is on screen: false when a scroll view or list has clipped it away.</summary>
    public bool IsInWindow()
    {
        if (_host is not { IsAttachedToWindow: true } host)
            return false;

        using var visible = new Rect();
        return host.GetGlobalVisibleRect(visible);
    }

    void Remove()
    {
        _fade?.Cancel();
        _fade = null;

        var drawable = _drawable;
        var ticker = _ticker;
        _drawable = null;
        _ticker = null;
        _look = null;

        if (drawable is not null)
            Remove(drawable, ticker);
    }

    void Remove(HighlightDrawable drawable, TimeAnimator? ticker)
    {
        ticker?.Cancel();
        ticker?.Dispose();
        drawable.Holder?.Overlay?.Remove(drawable);
        drawable.Dispose();
    }

    /// <summary>Puts the drawable where the view is in its holder: the parent, or the view itself.</summary>
    void Place(HighlightDrawable drawable)
    {
        if (_host is null || _holder is null)
            return;

        drawable.Holder = _holder;
        drawable.MoveTo(ReferenceEquals(_holder, _host) ? new PointF() : new PointF(_host.Left + _host.TranslationX, _host.Top + _host.TranslationY));
    }

    // The view moved within its parent (a list row recycled, a layout pass); the drawable follows.
    void OnHostLayoutChange(object? sender, AView.LayoutChangeEventArgs e)
    {
        if (_drawable is { } drawable)
            Place(drawable);
    }

    static ValueAnimator Fade(HighlightDrawable drawable, int to, Action? onEnd)
    {
        var fade = ValueAnimator.OfInt(drawable.Alpha, to)!;
        fade.SetDuration(FadeMilliseconds);
        fade.Update += (_, e) =>
        {
            if (e.Animation.AnimatedValue is Java.Lang.Integer value)
                drawable.Alpha = value.IntValue();
        };
        if (onEnd is not null)
            fade.AnimationEnd += (_, _) => onEnd();
        fade.Start();
        return fade;
    }

    sealed class Ticker(HighlightDrawable drawable, long epoch) : Java.Lang.Object, TimeAnimator.ITimeListener
    {
        public void OnTimeUpdate(TimeAnimator? animation, long totalTime, long deltaTime)
        {
            drawable.Elapsed = TimeSpan.FromMilliseconds(Android.OS.SystemClock.UptimeMillis() - epoch);
            drawable.InvalidateSelf();
        }
    }
}

/// <summary>Every kind in one drawable, painted from the time since the run started.</summary>
internal sealed class HighlightDrawable : Drawable
{
    readonly HighlightLook _look;
    readonly float _width;
    readonly float _height;
    readonly float _density;
    readonly float _radius;
    readonly APath _edge = new();
    readonly Paint _paint = new(PaintFlags.AntiAlias);
    readonly Paint _glow = new(PaintFlags.AntiAlias);
    readonly Matrix _matrix = new();
    readonly APath _segment = new();
    PathMeasure? _measure;
    PointF _origin = new();
    int _alpha = 255;

    public HighlightDrawable(HighlightLook look, int width, int height, float density)
    {
        _look = look;
        _width = width;
        _height = height;
        _density = density;
        _radius = (float)(look.Radius(0) * density);
        MoveTo(new PointF());
    }

    public TimeSpan Elapsed { get; set; }

    /// <summary>The view whose overlay holds the drawable.</summary>
    public AView? Holder { get; set; }

    /// <summary>Moves the drawing to the view's top left corner in the holder's coordinates.</summary>
    public void MoveTo(PointF origin)
    {
        _origin = origin;
        var outset = (int)Math.Ceiling(_look.Outset * _density * 2);
        SetBounds((int)origin.X - outset, (int)origin.Y - outset, (int)(origin.X + _width) + outset, (int)(origin.Y + _height) + outset);
        InvalidateSelf();
    }

    public override int Alpha
    {
        get => _alpha;
        set
        {
            _alpha = Math.Clamp(value, 0, 255);
            InvalidateSelf();
        }
    }

    public override int Opacity => (int)Format.Translucent;

    public override void SetAlpha(int alpha) => Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) { }

    public override void Draw(Canvas canvas)
    {
        if (_alpha == 0 || _width <= 0 || _height <= 0)
            return;

        canvas.Save();
        canvas.Translate(_origin.X, _origin.Y);
        switch (_look.Kind)
        {
            case HighlightKind.Edge: DrawEdge(canvas); break;
            case HighlightKind.Pulse: DrawPulse(canvas); break;
            case HighlightKind.Sheen: DrawSheen(canvas); break;
            case HighlightKind.Trace: DrawTrace(canvas); break;
            case HighlightKind.Breathe: DrawBreathe(canvas); break;
        }
        canvas.Restore();
    }

    double Phase => _look.Cycle.TotalMilliseconds > 0 ? Elapsed.TotalMilliseconds / _look.Cycle.TotalMilliseconds : 0;

    float Dp(double value) => (float)(value * _density);

    int Alpha255(double opacity) => (int)Math.Clamp(opacity * _alpha, 0, 255);

    static bool CanBlur => OperatingSystem.IsAndroidVersionAtLeast(28);

    void RoundRect(APath path, float inset, float radius)
    {
        path.Reset();
        path.AddRoundRect(new RectF(inset, inset, _width - inset, _height - inset), Math.Max(0, radius), Math.Max(0, radius), APath.Direction.Cw!);
    }

    // ── Edge ──

    void DrawEdge(Canvas canvas)
    {
        var rim = Dp(Math.Max(1, 2.5 * _look.Intensity));
        var colors = _look.Colors.Append(_look.Colors[0]).Select(color => (int)color.ToPlatform().ToArgb()).ToArray();
        var shader = new SweepGradient(_width / 2, _height / 2, colors, null);
        _matrix.Reset();
        _matrix.SetRotate((float)(Phase % 1 * 360) - 90, _width / 2, _height / 2);
        shader.SetLocalMatrix(_matrix);

        if (CanBlur && _look.Outset > 0 && _look.Intensity > 0)
        {
            RoundRect(_edge, 0, _radius);
            _glow.SetStyle(Paint.Style.Stroke);
            _glow.StrokeWidth = Dp(2 + 5 * _look.Intensity);
            _glow.SetShader(shader);
            _glow.SetMaskFilter(new BlurMaskFilter(Math.Max(1, Dp(_look.Outset * 0.45)), BlurMaskFilter.Blur.Normal!));
            _glow.Alpha = Alpha255(0.85 * _look.Intensity);
            canvas.DrawPath(_edge, _glow);
        }

        RoundRect(_edge, rim / 2, _radius - rim / 2);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = rim;
        _paint.SetShader(shader);
        _paint.Alpha = Alpha255(1);
        canvas.DrawPath(_edge, _paint);
        shader.Dispose();
    }

    // ── Pulse ──

    void DrawPulse(Canvas canvas)
    {
        var grow = Dp(Math.Max(4, _look.Outset));
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = Dp(Math.Max(1, 2 * _look.Intensity));
        _paint.SetShader(null);
        var color = _look.Primary.ToPlatform();

        if (_look.Still)
        {
            DrawRing(canvas, grow * 0.4f, color, 0.6 * _look.Intensity);
            return;
        }

        for (var ring = 0; ring < 2; ring++)
        {
            var t = Phase - ring * 0.32;
            if (t < 0)
                continue;

            t %= 1;
            var eased = 1 - Math.Pow(1 - t, 3);
            DrawRing(canvas, (float)(eased * grow), color, 0.9 * _look.Intensity * (1 - t));
        }
    }

    void DrawRing(Canvas canvas, float reach, AColor color, double opacity)
    {
        _paint.Color = color;
        _paint.Alpha = Alpha255(opacity);
        _edge.Reset();
        _edge.AddRoundRect(new RectF(-reach, -reach, _width + reach, _height + reach), _radius + reach, _radius + reach, APath.Direction.Cw!);
        canvas.DrawPath(_edge, _paint);
    }

    // ── Sheen ──

    void DrawSheen(Canvas canvas)
    {
        var color = _look.Primary.ToPlatform();
        RoundRect(_edge, 0, _radius);
        canvas.Save();
        canvas.ClipPath(_edge);
        _paint.SetStyle(Paint.Style.Fill);

        if (_look.Still)
        {
            _paint.SetShader(null);
            _paint.Color = color;
            _paint.Alpha = Alpha255(0.25 * _look.Intensity);
            canvas.DrawRect(0, 0, _width, _height, _paint);
            canvas.Restore();
            return;
        }

        // A 1.3 s sweep after a 0.2 s pause in each cycle.
        var into = Elapsed.TotalSeconds % _look.Cycle.TotalSeconds - 0.2;
        if (into is >= 0 and <= 1.3)
        {
            var p = into / 1.3;
            var eased = (float)(p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2);
            var offset = -1.2f * _width + eased * 2.4f * _width;
            var clear = (int)color.ToArgb() & 0x00FFFFFF;
            var bright = (Alpha255(0.75 * _look.Intensity) << 24) | clear;
            var shader = new LinearGradient(offset + 0.3f * _width, 0, offset + 0.7f * _width, _height * 0.25f,
                [clear, bright, clear], [0f, 0.5f, 1f], Shader.TileMode.Clamp!);
            _paint.SetShader(shader);
            _paint.Alpha = 255;
            canvas.DrawRect(0, 0, _width, _height, _paint);
            _paint.SetShader(null);
            shader.Dispose();
        }

        canvas.Restore();
    }

    // ── Trace ──

    void DrawTrace(Canvas canvas)
    {
        var width = Dp(Math.Max(1, 2.5 * _look.Intensity));
        RoundRect(_edge, width / 2, _radius - width / 2);
        var color = _look.Primary.ToPlatform();

        _paint.SetShader(null);
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = Math.Max(1, width * 0.6f);
        _paint.StrokeCap = Paint.Cap.Butt;
        _paint.Color = color;
        _paint.Alpha = Alpha255((_look.Still ? 0.5 : 0.22) * _look.Intensity);
        canvas.DrawPath(_edge, _paint);

        if (_look.Still)
            return;

        _measure ??= new PathMeasure(_edge, true);
        var length = _measure.Length;
        var head = (float)(Phase % 1) * length;

        _paint.StrokeWidth = width;
        _paint.StrokeCap = Paint.Cap.Round;
        // A faint tail and a solid head; a white head reads as a gap on a light card.
        DrawSegment(canvas, head - 0.12f * length, head, color, 0.45 * _look.Intensity);
        DrawSegment(canvas, head - 0.04f * length, head, color, _look.Intensity);
    }

    void DrawSegment(Canvas canvas, float start, float end, AColor color, double opacity)
    {
        var length = _measure!.Length;
        _paint.Color = color;
        _paint.Alpha = Alpha255(opacity);
        _segment.Reset();

        if (start < 0)
        {
            _measure.GetSegment(length + start, length, _segment, true);
            _measure.GetSegment(0, end, _segment, true);
        }
        else
        {
            _measure.GetSegment(start, end, _segment, true);
        }

        canvas.DrawPath(_segment, _paint);
    }

    // ── Breathe ──

    void DrawBreathe(Canvas canvas)
    {
        var outset = Math.Max(2, _look.Outset);
        var swing = _look.Still ? 0.5 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * Phase);
        var radius = Dp(outset * (0.2 + 0.55 * swing));
        var opacity = (0.25 + 0.65 * swing) * _look.Intensity;

        RoundRect(_edge, 0, _radius);
        _paint.SetShader(null);
        _paint.Color = _look.Primary.ToPlatform();
        _paint.Alpha = Alpha255(opacity);

        if (CanBlur)
        {
            // Outer: only outside the shape, so the view's content is not tinted.
            _paint.SetStyle(Paint.Style.Fill);
            _paint.SetMaskFilter(new BlurMaskFilter(Math.Max(1, radius), BlurMaskFilter.Blur.Outer!));
            canvas.DrawPath(_edge, _paint);
            _paint.SetMaskFilter(null);
            return;
        }

        // Before Android 9 the hardware renderer has no blur: a few fading rings stand in for the halo.
        _paint.SetStyle(Paint.Style.Stroke);
        _paint.StrokeWidth = Dp(1.5);
        for (var ring = 1; ring <= 3; ring++)
        {
            var reach = radius * ring / 3;
            _paint.Alpha = Alpha255(opacity * (1 - (ring - 1) / 3.0));
            _segment.Reset();
            _segment.AddRoundRect(new RectF(-reach, -reach, _width + reach, _height + reach), _radius + reach, _radius + reach, APath.Direction.Cw!);
            canvas.DrawPath(_segment, _paint);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _edge.Dispose();
            _paint.Dispose();
            _glow.Dispose();
            _matrix.Dispose();
            _segment.Dispose();
            _measure?.Dispose();
        }

        base.Dispose(disposing);
    }
}
