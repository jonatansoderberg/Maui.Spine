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
    }

    static void ApplyMaterial(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not UIView view || element is not VisualElement visual)
            return;

        var surface = view.Subviews.OfType<MaterialSurfaceView>().FirstOrDefault();
        var kind = Material.GetKind(visual);

        if (kind == MaterialKind.None)
        {
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
    }
}

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

    public void Update()
    {
        if (!_owner.TryGetTarget(out var owner))
            return;

        var kind = Material.Resolve(Material.GetKind(owner));
        var tint = Material.GetTint(owner);

        Effect = null;
        BackgroundColor = null;
        ContentView.BackgroundColor = null;

        switch (kind)
        {
            case MaterialKind.Glass when OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26):
                var glass = UIGlassEffect.Create(UIGlassEffectStyle.Regular);
                glass.TintColor = tint?.ToPlatform();
                glass.Interactive = Material.GetInteractive(owner);
                Effect = glass;
                break;

            case MaterialKind.Blur:
                Effect = UIBlurEffect.FromStyle(Material.GetThickness(owner) switch
                {
                    MaterialThickness.UltraThin => UIBlurEffectStyle.SystemUltraThinMaterial,
                    MaterialThickness.Thin => UIBlurEffectStyle.SystemThinMaterial,
                    MaterialThickness.Thick => UIBlurEffectStyle.SystemThickMaterial,
                    MaterialThickness.Chrome => UIBlurEffectStyle.SystemChromeMaterial,
                    _ => UIBlurEffectStyle.SystemMaterial,
                });
                ContentView.BackgroundColor = tint?.ToPlatform();
                break;

            default:
                BackgroundColor = Material.SurfaceColour(tint, kind == MaterialKind.Tinted).ToPlatform();
                break;
        }

        _shape = (owner as IBorderStroke)?.Shape;
        _fade = Material.GetFade(owner);
        UpdateEdgeLine(Material.GetEdgeLine(owner));
        SetNeedsLayout();
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

        if (_shape is not null && bounds.Width > 0 && bounds.Height > 0)
        {
            var shape = MaskLayer<CAShapeLayer>(bounds);
            shape.Path = _shape.PathForBounds(new Rect(0, 0, bounds.Width, bounds.Height)).AsCGPath();
            shape.FillColor = UIColor.Black.CGColor;
            return;
        }

        MaskView = null;
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
