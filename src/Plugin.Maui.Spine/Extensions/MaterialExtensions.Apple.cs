#if IOS || MACCATALYST

using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    // The material follows the shape and the theme; the rest of the view is MAUI's.
    static readonly string[] MaterialKeys = [Material.MapperKey, nameof(IBorderStroke.Shape)];

    static void ConfigureMaterials()
    {
        foreach (var key in MaterialKeys)
        {
            BorderHandler.Mapper.AppendToMapping(key, ApplyMaterial);
            ContentViewHandler.Mapper.AppendToMapping(key, ApplyMaterial);
            LayoutHandler.Mapper.AppendToMapping(key, ApplyMaterial);
        }

        // A container, and interactive glass, move the content MAUI gives them into their effect view
        // whenever the content changes; MAUI looks for the old content among the view's own subviews
        // to remove it, so it is handed back first.
        ContentViewHandler.Mapper.PrependToMapping(nameof(IContentView.Content), ReleaseMaterialContent);
        BorderHandler.Mapper.PrependToMapping(nameof(IContentView.Content), ReleaseMaterialContent);
        ContentViewHandler.Mapper.AppendToMapping(nameof(IContentView.Content), ApplyMaterial);
        BorderHandler.Mapper.AppendToMapping(nameof(IContentView.Content), ApplyMaterial);
        LayoutHandler.CommandMapper.AppendToMapping(nameof(ILayoutHandler.Add), (handler, layout, _) => ApplyMaterial(handler, layout));
        LayoutHandler.CommandMapper.AppendToMapping(nameof(ILayoutHandler.Insert), (handler, layout, _) => ApplyMaterial(handler, layout));
    }

    static void ReleaseMaterialContent(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is UIView view && view.Subviews.OfType<MaterialSurfaceView>().FirstOrDefault() is { } surface)
            surface.ReleaseContent(view);
    }

    static void ApplyMaterial(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not UIView view || element is not VisualElement visual)
            return;

        if (visual is MaterialContainer container)
        {
            if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
                ApplyContainer(view, container);
            return;
        }

        var surface = view.Subviews.OfType<MaterialSurfaceView>().FirstOrDefault();
        if (!Material.IsOn(visual))
        {
            surface?.ReleaseContent(view);
            surface?.RemoveFromSuperview();
            return;
        }

        if (surface is null)
        {
            surface = new MaterialSurfaceView(visual)
            {
                Frame = view.Bounds,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                UserInteractionEnabled = false,
            };
            view.InsertSubview(surface, 0);
            SpineTheme.Track(visual, () => visual.Handler?.UpdateValue(Material.MapperKey));
        }

        surface.Update();

        if (surface.UserInteractionEnabled)
            surface.HoldContent(view);
        else
            surface.ReleaseContent(view);
    }
}

public static partial class SpineExtensions
{
    // The glass surfaces inside merge only when they sit in the container effect's content view, so
    // MAUI's content is moved there. Its frames stay right: the effect view is as large as the container.
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst26.0")]
    static void ApplyContainer(UIView view, MaterialContainer container)
    {
        var glass = view.Subviews.OfType<MaterialContainerView>().FirstOrDefault();
        if (glass is null)
        {
            glass = new MaterialContainerView
            {
                Frame = view.Bounds,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            };
            view.InsertSubview(glass, 0);
        }

        glass.Effect = new UIGlassContainerEffect { Spacing = (nfloat)container.Spacing };

        foreach (var child in view.Subviews)
        {
            if (child != glass)
                glass.ContentView.AddSubview(child);
        }
    }
}

/// <summary>The glass container effect of a <see cref="MaterialContainer"/>.</summary>
internal sealed class MaterialContainerView() : UIVisualEffectView((UIVisualEffect?)null);

/// <summary>
/// The material behind a view's content: one effect view at the back of the platform view, as large
/// as it, clipped to the view's shape and, for the header bar, faded out at the bottom.
/// </summary>
internal sealed class MaterialSurfaceView(VisualElement owner) : UIVisualEffectView((UIVisualEffect?)null)
{
    private readonly WeakReference<VisualElement> _owner = new(owner);
    private UIView? _edgeLine;
    private IShape? _shape;
    private double _fade;
    private bool _glass;
    private double _presence = 1;
    private (UIBlurEffect Effect, double Amount, Color? Tint)? _blur;

    /// <summary>
    /// How much of the material shows, from 0 (none) to 1 (all of it): the blur and the tint scale
    /// together. The overlay behind a sheet follows the sheet in and out with it.
    /// </summary>
    public double Presence
    {
        get => _presence;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == _presence)
                return;

            _presence = value;
            if (_blur is not null)
                ShowBlur();
            else
                Update();
        }
    }

    public void Update()
    {
        if (!_owner.TryGetTarget(out var owner))
            return;

        var kind = Material.Resolve(Material.GetKind(owner));
        var tintLayer = Material.TintLayer(owner);
        var tint = Scaled(tintLayer);

        StopPartialEffect();
        Effect = null;
        BackgroundColor = null;
        ContentView.BackgroundColor = null;
        _blur = null;

        // Only interactive glass takes touches: it reacts to those on views inside it.
        UserInteractionEnabled = false;

        switch (kind)
        {
            // UIKit morphs clear glass into regular glass, so the intensity between them is the morph paused.
            case MaterialKind.Glass when OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26):
                var interactive = Material.GetInteractive(owner);
                UIGlassEffect Glass(UIGlassEffectStyle style)
                {
                    var glass = UIGlassEffect.Create(style);
                    glass.TintColor = tint;
                    glass.Interactive = interactive;
                    return glass;
                }

                var intensity = Material.GetIntensity(owner);
                if (intensity <= 0)
                    Effect = Glass(UIGlassEffectStyle.Clear);
                else if (intensity >= 1)
                    Effect = Glass(UIGlassEffectStyle.Regular);
                else
                    StartPartialEffect(Glass(UIGlassEffectStyle.Clear), Glass(UIGlassEffectStyle.Regular), intensity);
                UserInteractionEnabled = interactive;
                break;

            case MaterialKind.Blur:
                // The thinnest material, the most blur for the least milk, with the tint over it; the
                // header bar's scroll edge keeps the system material it is tuned against.
                var blur = UIBlurEffect.FromStyle(Material.GetSystemBlur(owner) switch
                {
                    SystemBlur.Thin => UIBlurEffectStyle.SystemThinMaterial,
                    SystemBlur.Regular => UIBlurEffectStyle.SystemMaterial,
                    _ => UIBlurEffectStyle.SystemUltraThinMaterial,
                });
                _blur = (blur, Material.BlurIntensity(owner), tintLayer);
                ShowBlur();
                break;

            default:
                BackgroundColor = tint;
                break;
        }

        _shape = (owner as IBorderStroke)?.Shape;
        _glass = kind == MaterialKind.Glass;
        _fade = Material.GetFade(owner);
        UpdateEdgeLine(Material.GetEdgeLine(owner));
        SetNeedsLayout();
    }

    /// <summary>
    /// The blur at its intensity times <see cref="Presence"/>. While only the presence changes, the paused
    /// animation already running is moved to the new fraction instead of being started again.
    /// </summary>
    private void ShowBlur()
    {
        var (blur, intensity, tint) = _blur!.Value;
        var amount = intensity * _presence;

        if (amount < 1 && _partial is not null && _partialEffect is { From: null } partial && partial.To == blur)
        {
            _partial.FractionComplete = (nfloat)amount;
            _partialEffect = (null, blur, amount);
        }
        else
        {
            StopPartialEffect();
            Effect = null;
            if (amount >= 1)
                Effect = blur;
            else
                StartPartialEffect(null, blur, amount);
        }

        ContentView.BackgroundColor = Scaled(tint);
    }

    private UIColor? Scaled(Color? tint) => tint?.WithAlpha(tint.Alpha * (float)_presence).ToPlatform();

    /// <summary>
    /// Interactive glass reacts only to touches on views inside it, so while it is interactive it holds
    /// the view's content. MAUI's frames stay right: the effect view is as large as the view.
    /// </summary>
    public void HoldContent(UIView view)
    {
        foreach (var child in view.Subviews)
        {
            if (child != this)
                ContentView.AddSubview(child);
        }
    }

    /// <summary>Hands the content <see cref="HoldContent"/> took back to the view, in front of the material.</summary>
    public void ReleaseContent(UIView view)
    {
        var index = Array.IndexOf(view.Subviews, this) + 1;
        foreach (var child in ContentView.Subviews)
        {
            if (child != _edgeLine)
                view.InsertSubview(child, index++);
        }
    }

    private void UpdateEdgeLine(Color? colour)
    {
        if (colour is null)
        {
            _edgeLine?.RemoveFromSuperview();
            _edgeLine = null;
            return;
        }

        _edgeLine ??= new UIView { AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleTopMargin };
        _edgeLine.BackgroundColor = colour.ToPlatform();
        if (_edgeLine.Superview is null)
            ContentView.AddSubview(_edgeLine);
    }

    // UIKit has no blur radius and no glass frosting. A material part of the way is the animation
    // from the one effect to the other, paused there; it is started again whenever the view comes back
    // on screen or the app to the foreground, as UIKit finishes paused animations when either leaves.
    private UIViewPropertyAnimator? _partial;
    private (UIVisualEffect? From, UIVisualEffect To, double Fraction)? _partialEffect;
    private NSObject? _foreground;

    private void StartPartialEffect(UIVisualEffect? from, UIVisualEffect to, double fraction)
    {
        _partialEffect = (from, to, fraction);
        if (Window is null)
            return;

        StopPartialEffect(forget: false);
        Effect = from;
        _partial = new UIViewPropertyAnimator(1, UIViewAnimationCurve.Linear, () => Effect = to)
        {
            PausesOnCompletion = true,
            FractionComplete = (nfloat)fraction,
        };
        _foreground ??= UIApplication.Notifications.ObserveWillEnterForeground((_, _) => RestartPartialEffect());
    }

    private void RestartPartialEffect()
    {
        if (_partialEffect is { } partial)
            StartPartialEffect(partial.From, partial.To, partial.Fraction);
    }

    // A paused animator must be stopped before it is released, or UIKit throws.
    private void StopPartialEffect(bool forget = true)
    {
        if (_partial is not null)
        {
            _partial.StopAnimation(true);
            _partial = null;
        }

        if (forget)
        {
            _partialEffect = null;
            _foreground?.Dispose();
            _foreground = null;
        }
    }

    public override void MovedToWindow()
    {
        base.MovedToWindow();

        if (Window is null)
            StopPartialEffect(forget: false);
        else
            RestartPartialEffect();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            StopPartialEffect();

        base.Dispose(disposing);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        var bounds = Bounds;

        if (_edgeLine is not null)
        {
            var hairline = 1 / (Window?.Screen.Scale ?? UIScreen.MainScreen.Scale);
            _edgeLine.Frame = new CGRect(0, bounds.Height - _fade - hairline, bounds.Width, hairline);
        }

        // An effect view is masked through its MaskView; a mask on its layer breaks the blur. A fade
        // and a shape are never asked for together: the fade is the header bar's, which has no shape.
        if (_fade > 0 && bounds.Height > 0)
        {
            var gradient = MaskLayer<CAGradientLayer>(bounds);
            gradient.Colors = [UIColor.Black.CGColor, UIColor.Black.CGColor, UIColor.Clear.CGColor];
            gradient.Locations = [0, (NSNumber)(1 - Math.Min(_fade, bounds.Height) / bounds.Height), 1];
            return;
        }

        // Glass keeps its edge highlights only when UIKit shapes it; a mask cuts them off.
        if (_glass && (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26)) && GlassCorners(bounds) is { } corners)
        {
            CornerConfiguration = corners;
            MaskView = null;
            return;
        }

        if (_shape is not null && bounds.Width > 0 && bounds.Height > 0)
        {
            var shape = MaskLayer<CAShapeLayer>(bounds);
            shape.Path = _shape.PathForBounds(new Rect(0, 0, bounds.Width, bounds.Height)).AsCGPath();
            shape.FillColor = UIColor.Black.CGColor;
            return;
        }

        MaskView = null;
    }

    /// <summary>
    /// The shape as UIKit's corners, where it is one: a capsule for an ellipse or a rounded
    /// rectangle rounded all the way, fixed corners for any other rounded rectangle, square corners
    /// with no shape.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst26.0")]
    private UICornerConfiguration? GlassCorners(CGRect bounds)
    {
        var half = (nfloat)(Math.Min(bounds.Width, bounds.Height) / 2);
        return _shape switch
        {
            null => UICornerConfiguration.CreateUniformCorners(UICornerRadius.CreateFixed(0)),
            Microsoft.Maui.Controls.Shapes.Ellipse => UICornerConfiguration.CreateCapsule(half),
            Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius: var r } when r.TopLeft >= half && r.TopRight >= half && r.BottomLeft >= half && r.BottomRight >= half
                => UICornerConfiguration.CreateCapsule(half),
            Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius: var r }
                => UICornerConfiguration.CreateCorners(
                    UICornerRadius.CreateFixed((nfloat)r.TopLeft), UICornerRadius.CreateFixed((nfloat)r.TopRight),
                    UICornerRadius.CreateFixed((nfloat)r.BottomLeft), UICornerRadius.CreateFixed((nfloat)r.BottomRight)),
            _ => null,
        };
    }

    private T MaskLayer<T>(CGRect bounds) where T : CALayer, new()
    {
        if (MaskView?.Layer.Sublayers?.FirstOrDefault() is not T layer)
        {
            layer = new T();
            MaskView = new UIView();
            MaskView.Layer.AddSublayer(layer);
        }

        MaskView!.Frame = bounds;
        layer.Frame = bounds;
        return layer;
    }
}

#endif
