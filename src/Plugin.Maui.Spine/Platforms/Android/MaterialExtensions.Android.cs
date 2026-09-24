using Android.Graphics;
using Android.Graphics.Drawables;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using AView = Android.Views.View;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    // MAUI sets a view's background drawable for these; the material is layered under whatever it set.
    static readonly string[] MaterialKeys =
    [
        Material.MapperKey,
        nameof(IView.Background),
        nameof(IBorderStroke.Shape),
        nameof(IBorderStroke.Stroke),
        nameof(IBorderStroke.StrokeThickness),
    ];

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
        if (handler.PlatformView is not AView view || element is not VisualElement visual)
            return;

        var own = view.Background as LayerDrawable is { NumberOfLayers: > 0 } layers && layers.GetDrawable(0) is MaterialDrawable material
            ? (material, layers.NumberOfLayers > 1 ? layers.GetDrawable(1) : null)
            : ((MaterialDrawable?)null, view.Background);

        if (Material.GetKind(visual) == MaterialKind.None)
        {
            if (own.Item1 is not null)
                view.Background = own.Item2;
            return;
        }

        var drawable = own.Item1;
        if (drawable is null)
        {
            drawable = new MaterialDrawable(view.Context?.Resources?.DisplayMetrics?.Density ?? 1);
            view.Background = own.Item2 is null
                ? new LayerDrawable([drawable])
                : new LayerDrawable([drawable, own.Item2]);
            SpineTheme.Track(visual, () => visual.Handler?.UpdateValue(Material.MapperKey));
        }

        drawable.Update(visual);
    }
}

/// <summary>
/// The material under a view's own background: the surface colour, clipped to the view's shape and,
/// for the header bar, faded out at the bottom with a hairline.
/// </summary>
internal sealed class MaterialDrawable(float density) : Drawable
{
    private readonly APaint _paint = new(PaintFlags.AntiAlias);
    private readonly APaint _edgePaint = new();
    private AColor _fill;
    private AColor? _edge;
    private float _fade;
    private IShape? _shape;

    public void Update(VisualElement owner)
    {
        var kind = Material.Resolve(Material.GetKind(owner));
        _fill = Material.SurfaceColour(Material.GetTint(owner), kind != MaterialKind.Solid).ToPlatform();
        _edge = Material.GetEdgeLine(owner)?.ToPlatform();
        _fade = (float)(Material.GetFade(owner) * density);
        _shape = (owner as IBorderStroke)?.Shape;
        InvalidateSelf();
    }

    public override void Draw(Canvas canvas)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty)
            return;

        canvas.Save();

        if (_shape is not null)
        {
            using var path = _shape.PathForBounds(new Microsoft.Maui.Graphics.Rect(0, 0, bounds.Width() / density, bounds.Height() / density)).AsAndroidPath(scaleX: density, scaleY: density);
            path.Offset(bounds.Left, bounds.Top);
            canvas.ClipPath(path);
        }

        var height = bounds.Height();
        if (_fade > 0 && height > 0)
        {
            var transparent = new AColor((byte)_fill.R, (byte)_fill.G, (byte)_fill.B, (byte)0);
            _paint.SetShader(new LinearGradient(0, bounds.Top, 0, bounds.Bottom,
                [_fill.ToArgb(), _fill.ToArgb(), transparent.ToArgb()],
                [0, Math.Max(0, 1 - _fade / height), 1],
                Shader.TileMode.Clamp!));
        }
        else
        {
            _paint.SetShader(null);
            _paint.Color = _fill;
        }

        canvas.DrawRect(bounds, _paint);

        if (_edge is { } edge)
        {
            _edgePaint.Color = edge;
            var bottom = bounds.Bottom - _fade;
            canvas.DrawRect(bounds.Left, bottom - 1, bounds.Right, bottom, _edgePaint);
        }

        canvas.Restore();
    }

    public override void SetAlpha(int alpha) => _paint.Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);

    public override int Opacity => (int)Format.Translucent;
}
