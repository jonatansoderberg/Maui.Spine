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
        foreach (var (picture, _, _, _) in _pictures)
            picture.RemoveFromSuperview();

        _pictures.Clear();
    }
}
#endif
