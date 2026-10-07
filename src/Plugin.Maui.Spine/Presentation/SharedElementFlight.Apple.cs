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

    /// <summary>The part of a zoom during which the view's picture fades, out at its start or in at its end.</summary>
    private const double ZoomFadeShare = 0.35;

    private SharedElementFlight(UIView container, UIView front)
    {
        _container = container;
        _front = front;
    }

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

    private partial bool AddZoom(List<VisualElement> views, bool push)
    {
        if (Place(views) is not { } view)
            return false;

        var radius = RadiusOf(view.Element, view.View);

        // On a pop the view is on the page that just came back into the back layer, not drawn there yet.
        var image = new UIImageView(Picture(view.View, onScreen: push, FillOf(view.Element)))
        {
            ContentMode = UIViewContentMode.ScaleAspectFill,
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
        };
        var picture = new UIView(view.Frame) { UserInteractionEnabled = false, ClipsToBounds = true, Alpha = push ? 1 : 0 };
        picture.Layer.CornerRadius = radius;
        picture.Layer.CornerCurve = CoreAnimation.CACornerCurve.Circular;
        image.Frame = picture.Bounds;
        picture.AddSubview(image);

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
        var task = push
            ? AnimateZoom(geometry.Small, geometry.SmallMask, geometry.SmallRadius, CoreAnimation.CATransform3D.Identity, _front.Bounds, 0, length)
            : AnimateZoom(_front.Layer.SublayerTransform, _zoomMask!.Frame, _zoomMask.CornerRadius, geometry.Small, geometry.SmallMask, geometry.SmallRadius, length);

        var fade = new UIViewPropertyAnimator(length * ZoomFadeShare / 1000.0, UIViewAnimationCurve.EaseInOut, () => zoom.Picture.Alpha = push ? 0 : 1);
        fade.StartAnimation(push ? 0 : length * (1 - ZoomFadeShare) / 1000.0);

        return task;
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

        // The view's place in the page's own coordinates, and the scale at which the page covers it.
        var place = new CGRect(zoom.Frame.X - frame.X, zoom.Frame.Y - frame.Y, zoom.Frame.Width, zoom.Frame.Height);
        var scale = nfloat.Max(place.Width / bounds.Width, place.Height / bounds.Height);

        // Scaled about the page's centre, then moved so that the centre lands on the view's.
        var small = CoreAnimation.CATransform3D.MakeScale(scale, scale, 1)
            .Concat(CoreAnimation.CATransform3D.MakeTranslation(place.GetMidX() - bounds.GetMidX(), place.GetMidY() - bounds.GetMidY(), 0));

        // The mask is scaled with the content, so it is the view's place before that scale: the
        // view's size and corners over the scale, about the page's centre.
        var smallMask = new CGRect(
            bounds.GetMidX() - place.Width / scale / 2,
            bounds.GetMidY() - place.Height / scale / 2,
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
        }

        foreach (var (picture, _, _, _) in _pictures)
            picture.RemoveFromSuperview();

        _pictures.Clear();
    }
}
#endif
