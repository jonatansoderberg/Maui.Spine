using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Views.Animations;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using Color = Microsoft.Maui.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;
using View = Microsoft.Maui.Controls.View;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SharedElementFlight
{
    private readonly ViewGroup _container;
    private readonly AView _front;
    private readonly float _density;
    private readonly List<(PictureView Picture, RectF From, RectF To, float FromRadius, float ToRadius)> _pictures = [];

    // The view a zooming page grows out of or shrinks into: its place in the container, its corner
    // radius, and a picture of it over the page while the page is small.
    private (RectF Frame, float Radius, PictureView Picture)? _zoom;

    // The part of the zooming page that lines up with that view, in the page's coordinates, and
    // its corner radius.
    private RectF? _zoomFocus;
    private float _zoomFocusRadius;

    // Whether the focus is a picture the view shows a crop of (a lightbox's image and its
    // thumbnail), so the view's picture is that crop of the focus rather than all of it.
    private bool _cropsFocus;

    // Where the page is now, and the outline that cuts it; the outline it had before the zoom.
    private ZoomState? _zoomNow;
    private ZoomOutline? _outline;
    private ViewOutlineProvider? _previousOutline;
    private bool _previousClip;

    /// <summary>The part of a zoom during which the view's picture fades without a focus.</summary>
    private const double ZoomFadeShare = 0.2;

    /// <summary>How long a push waits for the page arriving to be laid out, in milliseconds.</summary>
    private const int MaxSettle = 100;

    private SharedElementFlight(ViewGroup container, AView front)
    {
        _container = container;
        _front = front;
        _density = container.Resources?.DisplayMetrics?.Density ?? 1f;
    }

    private static async partial Task NextLayoutAsync(View layer)
    {
        if (layer.Handler?.PlatformView is not AView view || view.ViewTreeObserver is not { IsAlive: true } observer)
        {
            await Task.Yield();
            return;
        }

        // The layout pass runs just before a frame is drawn; a change of alpha asks for one.
        var laidOut = new TaskCompletionSource();
        var listener = new PreDrawListener(() => laidOut.TrySetResult());
        observer.AddOnPreDrawListener(listener);
        view.Invalidate();

        await Task.WhenAny(laidOut.Task, Task.Delay(MaxSettle));

        if (view.ViewTreeObserver is { IsAlive: true } current)
            current.RemoveOnPreDrawListener(listener);
    }

    private static partial SharedElementFlight? Create(View container, View front)
    {
        if (container.Handler?.PlatformView is not ViewGroup containerView || NativeOf(front) is not { } frontView)
            return null;

        // The pictures fly right above the front layer, under the header bar.
        while (frontView.Parent is AView parent && parent != containerView)
            frontView = parent;

        return frontView.Parent == containerView ? new(containerView, frontView) : null;
    }

    private partial void Add(List<VisualElement> sources, List<VisualElement> targets)
    {
        if (Place(sources) is not { } source || Place(targets) is not { } target)
            return;

        var picture = new PictureView(_container.Context!, Picture(source.View, FillOf(source.Element)), Picture(target.View, FillOf(target.Element)));
        var fromRadius = RadiusOf(source.Element);
        picture.Radius = fromRadius;
        AddPicture(picture, source.Frame);

        _pictures.Add((picture, source.Frame, target.Frame, fromRadius, RadiusOf(target.Element)));
        Hide(source.Element, target.Element);
    }

    public partial bool IsZoom => _zoom is not null;

    private partial bool AddZoom(List<VisualElement> views, bool push, VisualElement? focus)
    {
        if (Place(views) is not { } view)
            return false;

        // The focus in the page's own coordinates, at rest; one scrolled out of the page is no focus.
        _cropsFocus = focus is IZoomFocus;
        if (focus is IZoomFocus own)
        {
            if (own.FocusIn(_front) is { } rect && rect.Width() > 0 && rect.Height() > 0)
                _zoomFocus = rect;
        }
        else if (focus is not null && NativeOf(focus) is { IsAttachedToWindow: true } focusView)
        {
            var rect = RectIn(focusView, _front);
            if (rect.Width() > 0 && rect.Height() > 0 && RectF.Intersects(rect, new RectF(0, 0, _front.Width, _front.Height)))
            {
                _zoomFocus = rect;
                _zoomFocusRadius = RadiusOf(focus);
            }
        }

        var radius = RadiusOf(view.Element);
        var picture = new PictureView(_container.Context!, Picture(view.View, FillOf(view.Element)), null)
        {
            Radius = radius,
            Alpha = push ? 1 : 0,
        };
        AddPicture(picture, view.Frame);

        _zoom = (view.Frame, radius, picture);
        Hide(view.Element);
        return true;
    }

    public partial Task FlyAsync(uint length, Easing easing)
    {
        if (_pictures.Count == 0)
            return Task.CompletedTask;

        return Animate(length, new EasingInterpolator(easing), progress =>
        {
            foreach (var (picture, from, to, fromRadius, toRadius) in _pictures)
            {
                // The picture of the view as it lands fades in over the one it left as.
                picture.Radius = Lerp(fromRadius, toRadius, progress);
                picture.ToAlpha = progress;
                Move(picture, Lerp(from, to, progress));
            }
        });
    }

    /// <remarks>
    /// The front layer itself is scaled about its centre and moved until the focus (or the page's
    /// middle) covers the view, and cut to the view's size and corners by its outline, which is in
    /// the layer's own coordinates and scales with it. MAUI lays the region out with the layer's
    /// position and size only, so its scale and translation stay the zoom's.
    /// </remarks>
    public partial Task ZoomAsync(bool push, uint length)
    {
        if (ZoomGeometry() is not { } small || _zoom is not { } zoom)
            return Task.CompletedTask;

        // A pop starts where the page is: at rest, or wherever a back-swipe has left it.
        var full = FullState();
        var from = push ? small : _zoomNow ?? full;
        var to = push ? full : small;

        RectF? fromFrame = null, toFrame = null;
        float fromRadius = 0, toRadius = 0;
        if (_zoomFocus is not null)
        {
            // The view's picture rides on the focus and fades over the whole zoom, as a shared
            // element's pictures do, so the little that differs between them never shows as a jump.
            // A thumbnail is the crop of the focus that the outline closes on, so it rides on that
            // crop and its corners are the outline's: where it lies, the page shows the same.
            var track = _cropsFocus ? small.Mask : _zoomFocus;
            (fromFrame, toFrame) = push ? (zoom.Frame, OnScreen(track, to)) : (OnScreen(track, from), zoom.Frame);
            (fromRadius, toRadius) = _cropsFocus
                ? (push ? (zoom.Radius, 0f) : (from.Radius * from.Scale, zoom.Radius))
                : (push ? (zoom.Radius, _zoomFocusRadius) : (_zoomFocusRadius * from.Scale, zoom.Radius));
        }
        else
        {
            // Without a focus the page shrinks as a miniature of itself: the picture fades in only as
            // the page lands, and out as it starts to grow.
            zoom.Picture.Animate()?
                .Alpha(push ? 0 : 1)
                .SetDuration((long)(length * ZoomFadeShare))
                .SetStartDelay(push ? 0 : (long)(length * (1 - ZoomFadeShare)))
                .Start();
        }

        return Animate(length, new PathInterpolator(0.2f, 0.9f, 0.25f, 1f), progress =>
        {
            Apply(ZoomState.Lerp(from, to, progress));

            if (fromFrame is { } a && toFrame is { } b)
            {
                zoom.Picture.Alpha = push ? 1 - progress : progress;
                zoom.Picture.Radius = Lerp(fromRadius, toRadius, progress);
                Move(zoom.Picture, Lerp(a, b, progress));
            }
        });
    }

    /// <summary>How far a back-swipe across the whole page shrinks it: to this share of its size.</summary>
    private const float FollowScale = 0.7f;

    /// <summary>The corner radius a page has, on screen, once a back-swipe has shrunk it all the way, in dp.</summary>
    private const float FollowRadius = 36;

    public partial void Follow(double x, double y, double progress)
    {
        if (ZoomGeometry() is null)
            return;

        var scale = (float)(1 - (1 - FollowScale) * progress);

        // The radius in the page's own coordinates, which the scale shrinks.
        Apply(new ZoomState(scale, (float)x * _density, (float)y * _density,
            new RectF(0, 0, _front.Width, _front.Height), FollowRadius * _density * (float)progress / scale));
    }

    /// <summary>How far a drag down the whole page shrinks a lightbox's image.</summary>
    private const float CarryScale = 0.5f;

    public partial void Carry(Microsoft.Maui.Graphics.Point anchor, double x, double y, double progress)
    {
        if (ZoomGeometry() is null)
            return;

        // The scale is about the layer's centre, so the move puts the anchor back under the finger.
        var scale = (float)(1 - (1 - CarryScale) * Math.Clamp(progress, 0, 1));
        var (cx, cy) = (_front.Width / 2f, _front.Height / 2f);
        var (ax, ay) = ((float)anchor.X * _density, (float)anchor.Y * _density);
        var tx = ax + (float)x * _density - cx - scale * (ax - cx);
        var ty = ay + (float)y * _density - cy - scale * (ay - cy);

        Apply(new ZoomState(scale, tx, ty, new RectF(0, 0, _front.Width, _front.Height), 0));
    }

    public partial Task RestoreAsync(uint length)
    {
        if (_zoomNow is not { } from)
            return Task.CompletedTask;

        var to = FullState();
        return Animate(length, new PathInterpolator(0.2f, 0.9f, 0.25f, 1f), progress => Apply(ZoomState.Lerp(from, to, progress)));
    }

    private partial void RemovePictures()
    {
        foreach (var (picture, _, _, _, _) in _pictures)
            _container.RemoveView(picture);

        _pictures.Clear();

        if (_zoom is { } zoom)
        {
            _container.RemoveView(zoom.Picture);

            if (_outline is not null)
            {
                _front.ScaleX = 1;
                _front.ScaleY = 1;
                _front.TranslationX = 0;
                _front.TranslationY = 0;
                _front.OutlineProvider = _previousOutline;
                _front.ClipToOutline = _previousClip;
            }

            _zoom = null;
            _zoomNow = null;
            _outline = null;
            _zoomFocus = null;
            _zoomFocusRadius = 0;
            _cropsFocus = false;
        }
    }

    /// <summary>
    /// The state in which the focus (or the page's middle) covers the view, worked out with the page
    /// at rest; and the outline, in place, the first time it is asked for.
    /// </summary>
    private ZoomState? ZoomGeometry()
    {
        if (_zoom is not { } zoom)
            return null;

        var bounds = new RectF(0, 0, _front.Width, _front.Height);

        if (_outline is null)
        {
            _previousOutline = _front.OutlineProvider;
            _previousClip = _front.ClipToOutline;
            _outline = new ZoomOutline();
            _front.PivotX = bounds.CenterX();
            _front.PivotY = bounds.CenterY();
            _front.OutlineProvider = _outline;
            _front.ClipToOutline = true;
            Apply(FullState());
        }

        // The view's place in the page's own coordinates; the part of the page that lines up with
        // it; and the scale at which that part covers the view.
        var place = new RectF(zoom.Frame);
        place.Offset(-_front.Left, -_front.Top);
        var focus = _zoomFocus ?? bounds;
        var scale = Math.Max(place.Width() / focus.Width(), place.Height() / focus.Height());

        // Scaled about the page's centre, then moved so that the focus's centre lands on the view's.
        var x = place.CenterX() - bounds.CenterX() - scale * (focus.CenterX() - bounds.CenterX());
        var y = place.CenterY() - bounds.CenterY() - scale * (focus.CenterY() - bounds.CenterY());

        // The outline scales with the layer, so it is the view's place before that scale: the view's
        // size and corners over the scale, about the focus's centre.
        var mask = new RectF(
            focus.CenterX() - place.Width() / scale / 2,
            focus.CenterY() - place.Height() / scale / 2,
            focus.CenterX() + place.Width() / scale / 2,
            focus.CenterY() + place.Height() / scale / 2);

        return new ZoomState(scale, x, y, mask, zoom.Radius / scale);
    }

    private ZoomState FullState() => new(1, 0, 0, new RectF(0, 0, _front.Width, _front.Height), 0);

    private void Apply(ZoomState state)
    {
        _zoomNow = state;
        _front.ScaleX = state.Scale;
        _front.ScaleY = state.Scale;
        _front.TranslationX = state.X;
        _front.TranslationY = state.Y;
        _outline!.Rect = state.Mask;
        _outline.Radius = state.Radius;
        _front.InvalidateOutline();
    }

    /// <summary>Where the focus is in the container under <paramref name="state"/>.</summary>
    private RectF OnScreen(RectF focus, ZoomState state)
    {
        var centerX = _front.Width / 2f;
        var centerY = _front.Height / 2f;
        var x = _front.Left + centerX + (focus.CenterX() - centerX) * state.Scale + state.X;
        var y = _front.Top + centerY + (focus.CenterY() - centerY) * state.Scale + state.Y;
        var width = focus.Width() * state.Scale;
        var height = focus.Height() * state.Scale;
        return new RectF(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
    }

    /// <summary>The first view in <paramref name="views"/> with a place inside the region, and that place.</summary>
    private (VisualElement Element, AView View, RectF Frame)? Place(List<VisualElement> views)
    {
        var region = new RectF(0, 0, _container.Width, _container.Height);

        foreach (var element in views)
        {
            if (!element.IsVisible || NativeOf(element) is not { IsAttachedToWindow: true, IsShown: true } view)
                continue;

            var frame = RectIn(view, _container);
            if (frame.Width() > 0 && frame.Height() > 0 && RectF.Intersects(frame, region))
                return (element, view, frame);
        }

        return null;
    }

    private static RectF RectIn(AView view, AView ancestor)
    {
        // On screen: a lightbox's overlay is a window of its own, over the sheet the view is in.
        var at = new int[2];
        var origin = new int[2];
        view.GetLocationOnScreen(at);
        ancestor.GetLocationOnScreen(origin);
        var left = at[0] - origin[0];
        var top = at[1] - origin[1];
        return new RectF(left, top, left + view.Width, top + view.Height);
    }

    private static AView? NativeOf(VisualElement element) =>
        element.Handler is IViewHandler { ContainerView: AView container } ? container : element.Handler?.PlatformView as AView;

    private float RadiusOf(VisualElement element) => (float)(CornerRadiusOf(element) ?? 0) * _density;

    /// <summary>Draws <paramref name="view"/> on <paramref name="fill"/>, which fills the corners its shape cuts off.</summary>
    private static Bitmap Picture(AView view, Color? fill)
    {
        var bitmap = Bitmap.CreateBitmap(Math.Max(1, view.Width), Math.Max(1, view.Height), Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);
        if (fill is not null)
            canvas.DrawColor(fill.ToPlatform());

        view.Draw(canvas);
        return bitmap;
    }

    private void AddPicture(PictureView picture, RectF frame)
    {
        // Laid out here, not by MAUI's layout, which only arranges the views it knows.
        _container.AddView(picture, _container.IndexOfChild(_front) + 1);
        Move(picture, frame);
    }

    private static void Move(AView view, RectF frame)
    {
        view.Measure(
            AView.MeasureSpec.MakeMeasureSpec((int)Math.Round(frame.Width()), MeasureSpecMode.Exactly),
            AView.MeasureSpec.MakeMeasureSpec((int)Math.Round(frame.Height()), MeasureSpecMode.Exactly));
        view.Layout((int)Math.Round(frame.Left), (int)Math.Round(frame.Top), (int)Math.Round(frame.Right), (int)Math.Round(frame.Bottom));
        view.Invalidate();
    }

    /// <summary>Runs <paramref name="step"/> with the eased progress on every frame for <paramref name="length"/> milliseconds.</summary>
    private static Task Animate(uint length, ITimeInterpolator interpolator, Action<float> step)
    {
        var done = new TaskCompletionSource();
        var animator = ValueAnimator.OfFloat(0f, 1f)!;
        animator.SetDuration(length);
        animator.SetInterpolator(interpolator);
        animator.Update += (_, e) => step((float)e.Animation.AnimatedValue!);
        animator.AnimationEnd += (_, _) =>
        {
            step(1);
            done.TrySetResult();
        };
        animator.AnimationCancel += (_, _) => done.TrySetResult();
        animator.Start();
        return done.Task;
    }

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;

    private static RectF Lerp(RectF from, RectF to, float progress) => new(
        Lerp(from.Left, to.Left, progress),
        Lerp(from.Top, to.Top, progress),
        Lerp(from.Right, to.Right, progress),
        Lerp(from.Bottom, to.Bottom, progress));

    /// <summary>The front layer's scale and move, and the outline that cuts it, in pixels.</summary>
    private readonly record struct ZoomState(float Scale, float X, float Y, RectF Mask, float Radius)
    {
        public static ZoomState Lerp(ZoomState from, ZoomState to, float progress) => new(
            SharedElementFlight.Lerp(from.Scale, to.Scale, progress),
            SharedElementFlight.Lerp(from.X, to.X, progress),
            SharedElementFlight.Lerp(from.Y, to.Y, progress),
            SharedElementFlight.Lerp(from.Mask, to.Mask, progress),
            SharedElementFlight.Lerp(from.Radius, to.Radius, progress));
    }

    /// <summary>A rounded rectangle that a zooming layer is clipped to.</summary>
    private sealed class ZoomOutline : ViewOutlineProvider
    {
        public RectF Rect { get; set; } = new();

        public float Radius { get; set; }

        public override void GetOutline(AView? view, Outline? outline) =>
            outline?.SetRoundRect(
                (int)Math.Round(Rect.Left), (int)Math.Round(Rect.Top),
                (int)Math.Round(Rect.Right), (int)Math.Round(Rect.Bottom),
                Radius);
    }

    /// <summary>
    /// A picture of a view, or two that it fades from one to the other: each filling the view's
    /// size as it changes (cropped about its middle), with rounded corners.
    /// </summary>
    private sealed class PictureView(Context context, Bitmap from, Bitmap? to) : AView(context)
    {
        private readonly Paint _paint = new(PaintFlags.FilterBitmap | PaintFlags.AntiAlias);
        private readonly Path _clip = new();

        public float Radius { get; set; }

        public float ToAlpha { get; set; }

        protected override void OnDraw(Canvas canvas)
        {
            _clip.Reset();
            _clip.AddRoundRect(0, 0, Width, Height, Radius, Radius, Path.Direction.Cw!);

            canvas.Save();
            canvas.ClipPath(_clip);
            Draw(canvas, from, 1);
            if (to is not null && ToAlpha > 0)
                Draw(canvas, to, ToAlpha);
            canvas.Restore();
        }

        private void Draw(Canvas canvas, Bitmap bitmap, float alpha)
        {
            var scale = Math.Max(Width / (float)bitmap.Width, Height / (float)bitmap.Height);
            var width = Width / scale;
            var height = Height / scale;
            var left = (bitmap.Width - width) / 2;
            var top = (bitmap.Height - height) / 2;

            _paint.Alpha = (int)(alpha * 255);
            canvas.DrawBitmap(bitmap, new Rect((int)left, (int)top, (int)(left + width), (int)(top + height)), new RectF(0, 0, Width, Height), _paint);
        }
    }

    private sealed class EasingInterpolator(Easing easing) : Java.Lang.Object, ITimeInterpolator
    {
        public float GetInterpolation(float input) => (float)easing.Ease(input);
    }

    private sealed class PreDrawListener(Action drawn) : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
    {
        public bool OnPreDraw()
        {
            drawn();
            return true;
        }
    }
}
