#if IOS || MACCATALYST

using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Gives UIKit's soft scroll edge on iOS and Mac Catalyst 27 the reach it had on 26: over the status
/// bar and the whole header bar.
/// </summary>
/// <remarks>
/// On 27 the soft edge's progressive blur, a backdrop layer inside UIKit's scroll edge effect view, is
/// cut to the height of the status bar, while the effect view around it still spans the header. Made
/// as tall as the header again, the blur and its mask stretch with it, and UIKit draws the iOS 26 soft
/// edge: strongest at the top, gone at the header's bottom. UIKit sets the layer's height back as it
/// lays the edge out, so it is watched. iOS 26's soft edge also darkened what it blurred, by about a
/// quarter at the top (a colour matrix 27 no longer applies), which a gradient over the blur puts back.
/// The layer is found by its structure; on a system that builds the edge differently nothing is found,
/// and the edge stays UIKit's own.
/// </remarks>
internal sealed class SoftEdgeStretch : IDisposable
{
    private readonly UIScrollView _scrollView;
    private readonly Func<double> _height;
    private readonly IDisposable _offsetObserver;
    private IDisposable? _boundsObserver;
    private CALayer? _blur;
    private CAGradientLayer? _dim;

    /// <summary>
    /// How much darker iOS 26's soft edge made what it blurred, over the bar, measured against the
    /// same rows on iOS 27: about a fifth in light mode and two fifths in dark mode.
    /// </summary>
    private const float LightDim = 0.22f, DarkDim = 0.4f;

    public SoftEdgeStretch(UIScrollView scrollView, Func<double> height)
    {
        _scrollView = scrollView;
        _height = height;

        // UIKit builds the effect view once there is something under the edge; look for it as the list scrolls.
        _offsetObserver = scrollView.AddObserver("contentOffset", NSKeyValueObservingOptions.New, _ => Apply());
        Apply();
    }

    public void Apply()
    {
        if (_blur?.SuperLayer is null)
            Find();

        if (_blur is null)
            return;

        UpdateDim();

        var height = _height();
        var frame = _blur.Frame;
        if (height <= 0 || Math.Abs(frame.Height - height) < 0.5)
            return;

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        _blur.Frame = new CGRect(frame.X, frame.Y, frame.Width, height);
        if (_dim is not null)
            _dim.Frame = _blur.Frame;
        CATransaction.Commit();
    }

    private void Find()
    {
        _boundsObserver?.Dispose();
        _boundsObserver = null;
        _dim?.RemoveFromSuperLayer();
        _dim = null;
        _blur = null;

        // UIKit puts the effect view in a container of its own inside the scroll view.
        foreach (var view in _scrollView.Subviews.SelectMany(static child => child.Subviews.Prepend(child)))
        {
            if (view.Class.Name != "UIKit.ScrollEdgeEffectView")
                continue;

            _blur = Layers(view.Layer).FirstOrDefault(static layer =>
                layer.Class.Name == "CABackdropLayer"
                && layer.ValueForKey(new NSString("filters"))?.Description?.Contains("variableBlur") == true);

            if (_blur is not null)
            {
                _boundsObserver = _blur.AddObserver("bounds", NSKeyValueObservingOptions.New, _ => Apply());
                AddDim();
                return;
            }
        }
    }

    // As dark over the bar as iOS 26 made it, gone at the edge's bottom.
    private void AddDim()
    {
        _dim?.RemoveFromSuperLayer();
        _dim = new CAGradientLayer { Locations = [0, 0.65, 1], Frame = _blur!.Frame };
        _dimAlpha = -1;
        UpdateDim();
        _blur.SuperLayer?.InsertSublayerAbove(_dim, _blur);
    }

    private float _dimAlpha = -1;

    private void UpdateDim()
    {
        var alpha = _scrollView.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? DarkDim : LightDim;
        if (_dim is null || alpha == _dimAlpha)
            return;

        _dimAlpha = alpha;
        var dim = UIColor.Black.ColorWithAlpha(alpha).CGColor;
        _dim.Colors = [dim, dim, UIColor.Clear.CGColor];
    }

    private static IEnumerable<CALayer> Layers(CALayer root)
    {
        yield return root;
        foreach (var sublayer in root.Sublayers ?? [])
        {
            foreach (var layer in Layers(sublayer))
                yield return layer;
        }
    }

    public void Dispose()
    {
        _offsetObserver.Dispose();
        _boundsObserver?.Dispose();
        _boundsObserver = null;
        _dim?.RemoveFromSuperLayer();
        _dim = null;
        _blur = null;
    }
}

#endif
