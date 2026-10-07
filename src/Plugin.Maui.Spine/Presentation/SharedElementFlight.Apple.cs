#if IOS || MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Extensions;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SharedElementFlight
{
    private readonly UIView _container;
    private readonly UIView _front;
    private readonly List<(UIView Picture, UIImageView To, CGRect End, nfloat EndRadius)> _pictures = [];

    // The view a zooming page grows out of or shrinks into: its place, its corner radius, and a
    // picture of it over the page while the page is small.
    private (CGRect Frame, nfloat Radius, UIView Picture)? _zoom;

    // The part of the zooming page that lines up with that view, in the page's coordinates, and its
    // corner radius.
    private CGRect? _zoomFocus;
    private nfloat _zoomFocusRadius;

    /// <summary>
    /// The part of a zoom during which the view's picture fades, out at its start or in at its end:
    /// short, so that a focus that is not quite the view (its text a little elsewhere) is not seen
    /// twice for long.
    /// </summary>
    private const double ZoomFadeShare = 0.2;

    private SharedElementFlight(UIView container, UIView front)
    {
        _container = container;
        _front = front;
    }

    private static async partial Task NextLayoutAsync(View layer) => await Task.Yield();

    private static partial SharedElementFlight? Create(View container, View front)
    {
        if (container.Handler?.PlatformView is not UIView containerView || NativeOf(front) is not { } frontView)
            return null;

        // The pictures fly right above the front layer, under the header bar.
        while (frontView.Superview is { } parent && parent != containerView)
            frontView = parent;

        if (frontView.Superview != containerView)
            return null;

        // The page arriving was only just put in its layer.
        containerView.LayoutIfNeeded();
        return new(containerView, frontView);
    }

    private partial void Add(List<VisualElement> sources, List<VisualElement> targets)
    {
        if (Place(sources) is not { } source || Place(targets) is not { } target)
            return;

        var from = new UIImageView(Picture(source.View, onScreen: true, FillOf(source.Element))) { ContentMode = UIViewContentMode.ScaleAspectFill, ClipsToBounds = true };
        var to = new UIImageView(Picture(target.View, onScreen: false, FillOf(target.Element))) { ContentMode = UIViewContentMode.ScaleAspectFill, ClipsToBounds = true, Alpha = 0 };

        // The pictures are square; the shape is the picture's own, which turns from the one view's
        // corners into the other's as it flies. Corners drawn into the pictures would scale with
        // them, and the smaller ones of the picture underneath would show past the larger ones of
        // the picture over it.
        var picture = new UIView(source.Frame) { UserInteractionEnabled = false, ClipsToBounds = true };
        picture.Layer.CornerRadius = RadiusOf(source.Element, source.View);

        // A Border's rounded rectangle is drawn with circular arcs.
        picture.Layer.CornerCurve = CoreAnimation.CACornerCurve.Circular;

        foreach (var image in (UIImageView[])[from, to])
        {
            image.Frame = picture.Bounds;
            image.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            picture.AddSubview(image);
        }

        _container.InsertSubviewAbove(picture, _front);
        _pictures.Add((picture, to, target.Frame, RadiusOf(target.Element, target.View)));

        Hide(source.Element, target.Element);
    }

    public partial bool IsZoom => _zoom is not null;

    private partial bool AddZoom(List<VisualElement> views, bool push, VisualElement? focus)
    {
        if (Place(views) is not { } view)
            return false;

        // The focus in the page's own coordinates, at rest; one scrolled out of the page is no focus.
        if (focus is not null && NativeOf(focus) is { Window: not null } focusView)
        {
            var rect = focusView.ConvertRectToView(focusView.Bounds, _front);
            if (rect.Width > 0 && rect.Height > 0 && rect.IntersectsWith(_front.Bounds))
            {
                _zoomFocus = rect;
                _zoomFocusRadius = RadiusOf(focus, focusView);
            }
        }

        var radius = RadiusOf(view.Element, view.View);

        // One layer, so that Core Animation can move its frame and corners with the page's and the
        // picture fills whatever size it has on the way. On a pop the view is on the page that
        // just came back into the back layer, not drawn there yet.
        var picture = new UIImageView(Picture(view.View, onScreen: push, FillOf(view.Element)))
        {
            Frame = view.Frame,
            ContentMode = UIViewContentMode.ScaleAspectFill,
            UserInteractionEnabled = false,
            ClipsToBounds = true,
            Alpha = push ? 1 : 0,
        };
        picture.Layer.CornerRadius = radius;
        picture.Layer.CornerCurve = CoreAnimation.CACornerCurve.Circular;

        _container.InsertSubviewAbove(picture, _front);
        _zoom = (view.Frame, radius, picture);
        Hide(view.Element);
        return true;
    }

    /// <remarks>
    /// The page's content is scaled until it covers the view, centred on it, and cut to the
    /// view's size and corners by a mask that is scaled with it; then the scale, the mask and its corners move to
    /// the full page together (or from it, on a pop). The view's picture lies over the small page
    /// and fades out as the page starts to grow, or fades in as it lands.
    /// <para>
    /// The scale is the layer's <c>sublayerTransform</c>, not the view's transform: MAUI may lay
    /// the region out again while the page moves (a pop pads the page coming back), and setting
    /// the frame of a view under a transform scales it once more. MAUI never touches the
    /// sublayer transform or the mask, and Core Animation moves them.
    /// </para>
    /// </remarks>
    public partial Task ZoomAsync(bool push, uint length)
    {
        if (ZoomGeometry() is not { } geometry || _zoom is not { } zoom)
            return Task.CompletedTask;

        // A pop starts where the page is: at rest, or wherever a back-swipe has left it.
        var fromTransform = push ? geometry.Small : _front.Layer.SublayerTransform;
        var toTransform = push ? CoreAnimation.CATransform3D.Identity : geometry.Small;

        if (_zoomFocus is not null)
        {
            // The view's picture rides on the focus, from wherever the focus is to the view's place
            // (or back), and fades in over the whole of a pop (out over a push), as a shared
            // element's pictures do: the focus and the view differ a little (their padding, their
            // text), and spread over the zoom that difference never shows as a jump.
            var focusNow = FocusOnScreen(fromTransform);
            var (fromFrame, toFrame) = push ? (zoom.Frame, FocusOnScreen(toTransform)) : (focusNow, zoom.Frame);
            var (fromRadius, toRadius) = push
                ? (zoom.Radius, _zoomFocusRadius)
                : (_zoomFocusRadius * fromTransform.M11, zoom.Radius);

            var picture = zoom.Picture.Layer;
            picture.Frame = fromFrame;
            AnimateLayer(picture, fromFrame, toFrame, fromRadius, toRadius, push ? 1 : 0, push ? 0 : 1, length);
        }
        else
        {
            // Without a focus the page shrinks as a miniature of itself, which looks nothing like
            // the view: its picture fades in only as the page lands, and out as it starts to grow.
            var fade = new UIViewPropertyAnimator(length * ZoomFadeShare / 1000.0, UIViewAnimationCurve.EaseInOut, () => zoom.Picture.Alpha = push ? 0 : 1);
            fade.StartAnimation(push ? 0 : length * (1 - ZoomFadeShare) / 1000.0);
        }

        return push
            ? AnimateZoom(fromTransform, geometry.SmallMask, geometry.SmallRadius, toTransform, _front.Bounds, 0, length)
            : AnimateZoom(fromTransform, _zoomMask!.Frame, _zoomMask.CornerRadius, toTransform, geometry.SmallMask, geometry.SmallRadius, length);
    }

    /// <summary>
    /// Where the focus is on screen, in the container's coordinates, under
    /// <paramref name="transform"/>: a scale about the page's centre and a move, nothing else.
    /// </summary>
    private CGRect FocusOnScreen(CoreAnimation.CATransform3D transform)
    {
        var focus = _zoomFocus!.Value;
        var bounds = _front.Bounds;
        var frame = _front.Frame;
        var scale = transform.M11;

        var x = bounds.GetMidX() + (focus.GetMidX() - bounds.GetMidX()) * scale + transform.M41;
        var y = bounds.GetMidY() + (focus.GetMidY() - bounds.GetMidY()) * scale + transform.M42;
        return new CGRect(frame.X + x - focus.Width * scale / 2, frame.Y + y - focus.Height * scale / 2, focus.Width * scale, focus.Height * scale);
    }

    /// <summary>Moves a layer's frame, corners and opacity on the zoom's curve.</summary>
    private static void AnimateLayer(CoreAnimation.CALayer layer, CGRect fromFrame, CGRect toFrame, nfloat fromRadius, nfloat toRadius, float fromOpacity, float toOpacity, uint length)
    {
        CoreAnimation.CATransaction.Begin();
        CoreAnimation.CATransaction.DisableActions = true;

        layer.Frame = toFrame;
        layer.CornerRadius = toRadius;
        layer.Opacity = toOpacity;

        var duration = length / 1000.0;
        layer.AddAnimation(ZoomAnimation("bounds", Foundation.NSValue.FromCGRect(new CGRect(CGPoint.Empty, fromFrame.Size)), Foundation.NSValue.FromCGRect(new CGRect(CGPoint.Empty, toFrame.Size)), duration), "spine.zoom.bounds");
        layer.AddAnimation(ZoomAnimation("position", Foundation.NSValue.FromCGPoint(new CGPoint(fromFrame.GetMidX(), fromFrame.GetMidY())), Foundation.NSValue.FromCGPoint(new CGPoint(toFrame.GetMidX(), toFrame.GetMidY())), duration), "spine.zoom.position");
        layer.AddAnimation(ZoomAnimation("cornerRadius", Foundation.NSNumber.FromNFloat(fromRadius), Foundation.NSNumber.FromNFloat(toRadius), duration), "spine.zoom.radius");
        layer.AddAnimation(ZoomAnimation("opacity", Foundation.NSNumber.FromFloat(fromOpacity), Foundation.NSNumber.FromFloat(toOpacity), duration), "spine.zoom.opacity");

        CoreAnimation.CATransaction.Commit();
    }

    /// <summary>How far a back-swipe across the whole page shrinks it: to this share of its size.</summary>
    private const double FollowScale = 0.7;

    /// <summary>The corner radius a page has, on screen, once a back-swipe has shrunk it all the way.</summary>
    private const double FollowRadius = 36;

    /// <remarks>
    /// The page shrinks about its centre as the finger moves right, and moves with the finger
    /// itself, sideways and up or down, its corners rounding as it shrinks.
    /// </remarks>
    public partial void Follow(double x, double y, double progress)
    {
        if (ZoomGeometry() is null)
            return;

        var scale = (nfloat)(1 - (1 - FollowScale) * progress);

        CoreAnimation.CATransaction.Begin();
        CoreAnimation.CATransaction.DisableActions = true;
        _front.Layer.SublayerTransform = CoreAnimation.CATransform3D.MakeScale(scale, scale, 1)
            .Concat(CoreAnimation.CATransform3D.MakeTranslation((nfloat)x, (nfloat)y, 0));
        _zoomMask!.Frame = _front.Bounds;

        // In the page's own coordinates, which the scale shrinks.
        _zoomMask.CornerRadius = (nfloat)(FollowRadius * progress) / scale;
        CoreAnimation.CATransaction.Commit();
    }

    public partial Task RestoreAsync(uint length) =>
        _zoomMask is null
            ? Task.CompletedTask
            : AnimateZoom(_front.Layer.SublayerTransform, _zoomMask.Frame, _zoomMask.CornerRadius, CoreAnimation.CATransform3D.Identity, _front.Bounds, 0, length);

    // The scale, mask and corners at which the front layer's page covers just its view, worked out
    // once with the page at rest; and the mask, in place over the whole page.
    private (CoreAnimation.CATransform3D Small, CGRect SmallMask, nfloat SmallRadius)? _zoomGeometry;
    private CoreAnimation.CALayer? _zoomMask;

    private (CoreAnimation.CATransform3D Small, CGRect SmallMask, nfloat SmallRadius)? ZoomGeometry()
    {
        if (_zoomGeometry is { } known)
            return known;

        if (_zoom is not { } zoom)
            return null;

        var frame = _front.Frame;
        var bounds = _front.Bounds;

        // The view's place in the page's own coordinates; the part of the page that lines up with
        // it (the focus, or the whole page about its middle); and the scale at which that part
        // covers the view.
        var place = new CGRect(zoom.Frame.X - frame.X, zoom.Frame.Y - frame.Y, zoom.Frame.Width, zoom.Frame.Height);
        var focus = _zoomFocus ?? bounds;
        var scale = nfloat.Max(place.Width / focus.Width, place.Height / focus.Height);

        // Scaled about the page's centre, then moved so that the focus's centre lands on the view's.
        var small = CoreAnimation.CATransform3D.MakeScale(scale, scale, 1)
            .Concat(CoreAnimation.CATransform3D.MakeTranslation(
                place.GetMidX() - bounds.GetMidX() - scale * (focus.GetMidX() - bounds.GetMidX()),
                place.GetMidY() - bounds.GetMidY() - scale * (focus.GetMidY() - bounds.GetMidY()),
                0));

        // The mask is scaled with the content, so it is the view's place before that scale: the
        // view's size and corners over the scale, about the focus's centre.
        var smallMask = new CGRect(
            focus.GetMidX() - place.Width / scale / 2,
            focus.GetMidY() - place.Height / scale / 2,
            place.Width / scale,
            place.Height / scale);

        CoreAnimation.CATransaction.Begin();
        CoreAnimation.CATransaction.DisableActions = true;
        _zoomMask = new CoreAnimation.CALayer
        {
            BackgroundColor = UIColor.Black.CGColor,
            CornerCurve = CoreAnimation.CACornerCurve.Circular,
            Frame = bounds,
        };
        _front.Layer.Mask = _zoomMask;
        CoreAnimation.CATransaction.Commit();

        _zoomGeometry = (small, smallMask, zoom.Radius / scale);
        return _zoomGeometry;
    }

    /// <summary>Moves the page's scale, mask and corners from one state to another together.</summary>
    private Task AnimateZoom(
        CoreAnimation.CATransform3D fromTransform, CGRect fromRect, nfloat fromRadius,
        CoreAnimation.CATransform3D toTransform, CGRect toRect, nfloat toRadius, uint length)
    {
        var page = _front.Layer;
        var mask = _zoomMask!;
        var done = new TaskCompletionSource();

        CoreAnimation.CATransaction.Begin();
        CoreAnimation.CATransaction.DisableActions = true;
        CoreAnimation.CATransaction.CompletionBlock = () => done.TrySetResult();

        page.SublayerTransform = toTransform;
        mask.Frame = toRect;
        mask.CornerRadius = toRadius;

        var duration = length / 1000.0;
        page.AddAnimation(ZoomAnimation("sublayerTransform", Foundation.NSValue.FromCATransform3D(fromTransform), Foundation.NSValue.FromCATransform3D(toTransform), duration), "spine.zoom");
        mask.AddAnimation(ZoomAnimation("bounds", Foundation.NSValue.FromCGRect(new CGRect(CGPoint.Empty, fromRect.Size)), Foundation.NSValue.FromCGRect(new CGRect(CGPoint.Empty, toRect.Size)), duration), "spine.zoom.bounds");
        mask.AddAnimation(ZoomAnimation("position", Foundation.NSValue.FromCGPoint(new CGPoint(fromRect.GetMidX(), fromRect.GetMidY())), Foundation.NSValue.FromCGPoint(new CGPoint(toRect.GetMidX(), toRect.GetMidY())), duration), "spine.zoom.position");
        mask.AddAnimation(ZoomAnimation("cornerRadius", Foundation.NSNumber.FromNFloat(fromRadius), Foundation.NSNumber.FromNFloat(toRadius), duration), "spine.zoom.radius");

        CoreAnimation.CATransaction.Commit();
        return done.Task;
    }

    /// <summary>
    /// A move that starts fast and settles gently, as UIKit's zoom transition does: the curve of a
    /// spring with no bounce.
    /// </summary>
    private static CoreAnimation.CABasicAnimation ZoomAnimation(string keyPath, Foundation.NSObject from, Foundation.NSObject to, double duration)
    {
        var animation = CoreAnimation.CABasicAnimation.FromKeyPath(keyPath);
        animation.From = from;
        animation.To = to;
        animation.Duration = duration;
        animation.TimingFunction = CoreAnimation.CAMediaTimingFunction.FromControlPoints(0.2f, 0.9f, 0.25f, 1f);
        return animation;
    }

    /// <summary>The first view in <paramref name="views"/> with a place inside the region, and that place.</summary>
    private (VisualElement Element, UIView View, CGRect Frame)? Place(List<VisualElement> views)
    {
        foreach (var element in views)
        {
            if (!element.IsVisible || NativeOf(element) is not { Window: not null } view)
                continue;

            var frame = view.ConvertRectToView(view.Bounds, _container);
            if (frame.Width > 0 && frame.Height > 0 && frame.IntersectsWith(_container.Bounds))
                return (element, view, frame);
        }

        return null;
    }

    private static UIView? NativeOf(VisualElement element) =>
        element.Handler is IViewHandler { ContainerView: UIView container } ? container : element.Handler?.PlatformView as UIView;

    private static nfloat RadiusOf(VisualElement element, UIView view) =>
        CornerRadiusOf(element) is { } radius ? (nfloat)radius : view.Layer.CornerRadius;

    /// <summary>
    /// Draws <paramref name="view"/> on <paramref name="fill"/>, which fills the corners its shape
    /// cuts off. A view on screen is drawn as the screen shows it; one that is not (the page
    /// arriving, or a page just moved into the back layer, which UIKit has not drawn there yet and
    /// would draw empty) from its layers.
    /// </summary>
    private static UIImage Picture(UIView view, bool onScreen, Color? fill) =>
        new UIGraphicsImageRenderer(view.Bounds.Size).CreateImage(context =>
        {
            if (fill is not null)
            {
                context.CGContext.SetFillColor(fill.ToPlatform().CGColor);
                context.CGContext.FillRect(view.Bounds);
            }

            if (!onScreen || !view.DrawViewHierarchy(view.Bounds, afterScreenUpdates: false))
                view.Layer.RenderInContext(context.CGContext);
        });

    public partial Task FlyAsync(uint length, Easing easing)
    {
        if (_pictures.Count == 0)
            return Task.CompletedTask;

        var curve = SpineAnimation.CurveOf(easing) ?? SpineAnimation.CurveOf(Easing.CubicOut)!.Value;
        var done = new TaskCompletionSource();
        var animator = new UIViewPropertyAnimator(length / 1000.0, new UICubicTimingParameters(curve.Item1, curve.Item2));
        animator.AddAnimations(() =>
        {
            foreach (var (picture, to, end, radius) in _pictures)
            {
                // The picture of the view as it lands fades in over the one it left as, so the
                // flight never turns see-through.
                picture.Frame = end;
                picture.Layer.CornerRadius = radius;
                to.Alpha = 1;
            }
        });
        animator.AddCompletion(_ => done.TrySetResult());
        animator.StartAnimation();
        return done.Task;
    }

    private partial void RemovePictures()
    {
        if (_zoom is { } zoom)
        {
            zoom.Picture.RemoveFromSuperview();
            _front.Layer.SublayerTransform = CoreAnimation.CATransform3D.Identity;
            _front.Layer.Mask = null;
            _zoom = null;
            _zoomGeometry = null;
            _zoomMask = null;
            _zoomFocus = null;
            _zoomFocusRadius = 0;
        }

        foreach (var (picture, _, _, _) in _pictures)
            picture.RemoveFromSuperview();

        _pictures.Clear();
    }
}
#endif
