using Android.Graphics;
using Android.Graphics.Drawables;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using ARect = Android.Graphics.Rect;
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
            drawable = new MaterialDrawable(view, view.Context?.Resources?.DisplayMetrics?.Density ?? 1);
            view.Background = own.Item2 is null
                ? new LayerDrawable([drawable])
                : new LayerDrawable([drawable, own.Item2]);
            SpineTheme.Track(visual, () => visual.Handler?.UpdateValue(Material.MapperKey));
        }

        drawable.Update(visual);
    }
}

/// <summary>
/// The material under a view's own background: a blur of what is behind the view (Android 12 and
/// later) or the surface colour, clipped to the view's shape and, for the header bar, faded out at the
/// bottom with a hairline.
/// </summary>
/// <remarks>
/// Android cannot blur what is behind a view by itself. Before each frame is drawn, what is behind the
/// view is recorded into a <see cref="RenderNode"/>: the backgrounds of its ancestors and the siblings
/// drawn before it, at every level up to the window, never the view itself or anything over it, so
/// the blur cannot feed on its own output. The GPU blurs that node with a <see cref="RenderEffect"/>,
/// and the view's own display list draws it; only the node is recorded again as content moves, not
/// the view.
/// </remarks>
internal sealed class MaterialDrawable : Drawable
{
    private readonly WeakReference<AView> _host;
    private readonly float _density;
    private readonly APaint _paint = new(PaintFlags.AntiAlias);
    private readonly APaint _edgePaint = new();
    private readonly APaint _maskPaint = new() { AntiAlias = true };
    private AColor _fill;
    private AColor? _edge;
    private float _fade;
    private IShape? _shape;
    private float _blurRadius;
    private RenderNode? _behind;
    private bool _listening;

    public MaterialDrawable(AView host, float density)
    {
        _host = new(host);
        _density = density;
        _maskPaint.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstIn!));
        host.ViewAttachedToWindow += (_, _) => Listen(true);
        host.ViewDetachedFromWindow += (_, _) => Listen(false);
        if (host.IsAttachedToWindow)
            Listen(true);
    }

    /// <summary>How far a blur of each thickness spreads, and how much of the surface colour lies over it.</summary>
    private static (float Radius, float Overlay) BlurOf(MaterialThickness thickness) => thickness switch
    {
        MaterialThickness.UltraThin => (8, 0.1f),
        MaterialThickness.Thin => (16, 0.25f),
        MaterialThickness.Thick => (32, 0.55f),
        MaterialThickness.Chrome => (40, 0.7f),
        _ => (24, 0.4f),
    };

    public void Update(VisualElement owner)
    {
        var kind = Material.Resolve(Material.GetKind(owner));
        var tint = Material.GetTint(owner);

        if (kind == MaterialKind.Blur && OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var (radius, overlay) = BlurOf(Material.GetThickness(owner));
            _blurRadius = radius * _density;
            _fill = (tint ?? Material.SurfaceColour(null, tinted: false).WithAlpha(overlay)).ToPlatform();
        }
        else
        {
            _blurRadius = 0;
            _fill = Material.SurfaceColour(tint, kind != MaterialKind.Solid).ToPlatform();
        }

        _edge = Material.GetEdgeLine(owner)?.ToPlatform();
        _fade = (float)(Material.GetFade(owner) * _density);
        _shape = (owner as IBorderStroke)?.Shape;
        InvalidateSelf();
    }

    private void Listen(bool on)
    {
        if (on == _listening || !_host.TryGetTarget(out var host) || host.ViewTreeObserver is not { } observer)
            return;

        _listening = on;
        if (on)
            observer.PreDraw += OnPreDraw;
        else
            observer.PreDraw -= OnPreDraw;
    }

    private void OnPreDraw(object? sender, Android.Views.ViewTreeObserver.PreDrawEventArgs e)
    {
        e.Handled = true;

        if (_blurRadius > 0 && OperatingSystem.IsAndroidVersionAtLeast(31)
            && _host.TryGetTarget(out var host) && host.IsShown && host.Width > 0 && host.Height > 0)
            RecordBehind(host);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private void RecordBehind(AView host)
    {
        _behind ??= new RenderNode("SpineMaterial");
        _behind.SetPosition(0, 0, host.Width, host.Height);
        _behind.SetRenderEffect(RenderEffect.CreateBlurEffect(_blurRadius, _blurRadius, Shader.TileMode.Clamp!));

        var canvas = _behind.BeginRecording();
        try
        {
            var origin = new int[2];
            host.GetLocationInWindow(origin);

            // From the window down: each ancestor's background, then the children it draws before the
            // branch that leads to the view.
            var chain = new List<(Android.Views.ViewGroup Parent, AView Child)>();
            for (var child = host; child.Parent is Android.Views.ViewGroup parent; child = parent)
                chain.Add((parent, child));

            var bounds = new ARect(origin[0], origin[1], origin[0] + host.Width, origin[1] + host.Height);
            for (var level = chain.Count - 1; level >= 0; level--)
            {
                var (parent, child) = chain[level];
                DrawAt(canvas, parent, origin, parent.Background);

                for (var i = 0; i < parent.ChildCount && parent.GetChildAt(i) is { } sibling && sibling != child; i++)
                {
                    if (sibling.Visibility != Android.Views.ViewStates.Visible || sibling.Alpha <= 0)
                        continue;

                    var at = new int[2];
                    sibling.GetLocationInWindow(at);
                    if (!ARect.Intersects(bounds, new ARect(at[0], at[1], at[0] + sibling.Width, at[1] + sibling.Height)))
                        continue;

                    canvas.Save();
                    canvas.Translate(at[0] - origin[0], at[1] - origin[1]);
                    sibling.Draw(canvas);
                    canvas.Restore();
                }
            }
        }
        finally
        {
            _behind.EndRecording();
        }
    }

    private static void DrawAt(Canvas canvas, AView view, int[] origin, Drawable? drawable)
    {
        if (drawable is null)
            return;

        var at = new int[2];
        view.GetLocationInWindow(at);
        canvas.Save();
        canvas.Translate(at[0] - origin[0], at[1] - origin[1]);
        drawable.Draw(canvas);
        canvas.Restore();
    }

    public override void Draw(Canvas canvas)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty)
            return;

        canvas.Save();

        if (_shape is not null)
        {
            using var path = _shape.PathForBounds(new Microsoft.Maui.Graphics.Rect(0, 0, bounds.Width() / _density, bounds.Height() / _density)).AsAndroidPath(scaleX: _density, scaleY: _density);
            path.Offset(bounds.Left, bounds.Top);
            canvas.ClipPath(path);
        }

        var height = bounds.Height();
        var fades = _fade > 0 && height > 0;
        var blurs = _blurRadius > 0 && _behind is not null && canvas.IsHardwareAccelerated && OperatingSystem.IsAndroidVersionAtLeast(31);

        // The fade applies to the blur and the colour over it alike: both go into a layer that the
        // gradient then cuts away towards the bottom.
        var layer = fades && blurs ? canvas.SaveLayer(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, null) : -1;

        if (blurs && OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            canvas.Save();
            canvas.Translate(bounds.Left, bounds.Top);
            canvas.DrawRenderNode(_behind!);
            canvas.Restore();
        }

        if (fades && !blurs)
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

        if (layer >= 0)
        {
            _maskPaint.SetShader(new LinearGradient(0, bounds.Top, 0, bounds.Bottom,
                [AColor.Black.ToArgb(), AColor.Black.ToArgb(), AColor.Transparent.ToArgb()],
                [0, Math.Max(0, 1 - _fade / height), 1],
                Shader.TileMode.Clamp!));
            canvas.DrawRect(bounds, _maskPaint);
            canvas.RestoreToCount(layer);
        }

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
