using Android.Graphics;
using Android.Graphics.Drawables;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using AView = Android.Views.View;
using SurfaceView = Android.Views.SurfaceView;
using TextureView = Android.Views.TextureView;
using ViewGroup = Android.Views.ViewGroup;
using ViewStates = Android.Views.ViewStates;
using ViewTreeObserver = Android.Views.ViewTreeObserver;

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

        if (!Material.IsOn(visual))
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
/// <para>
/// This runs on the main thread before every frame, for every material on screen, so it does little:
/// what is behind is gathered once after each layout pass, and the node is recorded again only when the
/// view or something behind it has moved, shown, hidden or drawn itself anew. A view group's children
/// and a texture view's frames reach the node through their own display lists and layers, so a page
/// animating behind the material, or a leaf turning on the GPU, costs a few position checks a frame.
/// </para>
/// </remarks>
internal sealed class MaterialDrawable : Drawable, ViewTreeObserver.IOnPreDrawListener, ViewTreeObserver.IOnGlobalLayoutListener
{
    private readonly WeakReference<AView> _host;
    private readonly float _density;
    private readonly APaint _paint = new(PaintFlags.AntiAlias);
    private readonly APaint _edgePaint = new();
    private readonly APaint _maskPaint = new() { AntiAlias = true };
    private readonly List<Behind> _items = [];
    private readonly int[] _at = new int[2];
    private AColor _fill;
    private AColor? _edge;
    private float _fade;
    private IShape? _shape;
    private float _blurRadius;
    private float _effectRadius;
    private RenderNode? _behind;
    private bool _collected;
    private bool _recorded;
    private int _x, _y, _width, _height;
    private long _recordedAt;
    private Android.Graphics.Path? _clip;
    private (int Width, int Height) _clipSize;
    private bool _listening;
    private float _presence = 1;
    private (float Radius, Microsoft.Maui.Graphics.Color Fill) _full;

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

    /// <summary>How far a blur spreads at full intensity; the tint over it is what makes a material thicker.</summary>
    private const float BlurRadius = 24;

    /// <summary>How much of the surface lies over the thinnest blur, as over the system's ultra-thin material.</summary>
    private const float ThinnestMilk = 0.1f;

    /// <summary>How long what is behind may go without being recorded again while frames are drawn.</summary>
    private const long RefreshMs = 500;

    public void Update(VisualElement owner)
    {
        var kind = Material.Resolve(Material.GetKind(owner));
        var tint = Material.TintLayer(owner);

        if (kind == MaterialKind.Blur && OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var intensity = Material.BlurIntensity(owner);

            // The thinnest system material has a little milk of its own; the header bar's band is its tint alone.
            var milk = Material.GetSystemBlur(owner) == SystemBlur.None ? Material.Surface().WithAlpha((float)(ThinnestMilk * intensity)) : null;
            _full = ((float)(BlurRadius * intensity) * _density, Material.Over(tint, milk) ?? Colors.Transparent);
        }
        else
        {
            _full = (0, tint ?? Colors.Transparent);
        }

        ApplyPresence();

        _edge = Material.GetEdgeLine(owner)?.ToPlatform();
        _fade = (float)(Material.GetFade(owner) * _density);
        _shape = (owner as IBorderStroke)?.Shape;

        // A new theme may have changed the backgrounds behind the view, and the shape its corners.
        _collected = false;
        _clip = null;
        InvalidateSelf();
    }

    /// <summary>
    /// How much of the material shows, from 0 (none) to 1 (all of it): the blur radius and the tint
    /// scale together. The overlay behind a sheet follows the sheet in and out with it.
    /// </summary>
    public float Presence
    {
        get => _presence;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == _presence)
                return;

            _presence = value;
            ApplyPresence();
            if (_host.TryGetTarget(out var host))
                host.Invalidate();
        }
    }

    private void ApplyPresence()
    {
        _blurRadius = _full.Radius * _presence;
        _fill = _full.Fill.WithAlpha(_full.Fill.Alpha * _presence).ToPlatform();
    }

    private void Listen(bool on)
    {
        if (on == _listening || !_host.TryGetTarget(out var host) || host.ViewTreeObserver is not { } observer)
            return;

        _listening = on;
        _collected = false;
        if (on)
        {
            observer.AddOnPreDrawListener(this);
            observer.AddOnGlobalLayoutListener(this);
        }
        else
        {
            observer.RemoveOnPreDrawListener(this);
            observer.RemoveOnGlobalLayoutListener(this);
            _items.Clear();
        }
    }

    // Views come and go, and grow or shrink, in a layout pass: what is behind is gathered again after one.
    void ViewTreeObserver.IOnGlobalLayoutListener.OnGlobalLayout() => _collected = false;

    // A hidden window gives up its texture views' layers, which the recording refers to.
    public override bool SetVisible(bool visible, bool restart)
    {
        if (visible)
            _collected = false;

        return base.SetVisible(visible, restart);
    }

    bool ViewTreeObserver.IOnPreDrawListener.OnPreDraw()
    {
        if (_blurRadius > 0 && OperatingSystem.IsAndroidVersionAtLeast(31)
            && _host.TryGetTarget(out var host) && host.IsShown)
            RecordBehind(host);

        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private void RecordBehind(AView host)
    {
        var width = host.Width;
        var height = host.Height;
        if (width <= 0 || height <= 0)
            return;

        if (!_collected)
            Collect(host);

        host.GetLocationInWindow(_at);
        var (x, y) = (_at[0], _at[1]);

        _behind ??= new RenderNode("SpineMaterial");
        if (_blurRadius != _effectRadius)
        {
            // A property of the node: a new radius needs no new recording.
            _effectRadius = _blurRadius;
            _behind.SetRenderEffect(RenderEffect.CreateBlurEffect(_blurRadius, _blurRadius, Shader.TileMode.Clamp!));
        }

        // What the checks below cannot see (a view group drawing something new of its own, an ancestor's
        // background changing in place) shows within a moment.
        var now = Environment.TickCount64;
        var record = !_recorded || now - _recordedAt >= RefreshMs || x != _x || y != _y || width != _width || height != _height;
        if (width != _width || height != _height)
            _behind.SetPosition(0, 0, width, height);
        (_x, _y, _width, _height) = (x, y, width, height);

        foreach (var item in _items)
        {
            var shows = true;
            if (item.Background is null)
            {
                var view = item.View;
                shows = view.Visibility == ViewStates.Visible && view.Alpha > 0;
                if (shows)
                {
                    view.GetLocationInWindow(_at);
                    shows = _at[0] < x + width && x < _at[0] + item.Width && _at[1] < y + height && y < _at[1] + item.Height;
                }

                // A view whose own drawing is copied into the node, not referred to through its display list.
                record |= shows && item.DrawsItself && view.IsDirty;
            }
            else
            {
                item.View.GetLocationInWindow(_at);
            }

            record |= shows != item.Shows || (shows && (_at[0] != item.X || _at[1] != item.Y));
            (item.Shows, item.X, item.Y) = (shows, _at[0], _at[1]);
        }

        if (!record)
            return;

        var canvas = _behind.BeginRecording();
        try
        {
            foreach (var item in _items)
            {
                if (!item.Shows)
                    continue;

                canvas.Save();
                canvas.Translate(item.X - x, item.Y - y);
                if (item.Background is { } background)
                    background.Draw(canvas);
                else
                    item.View.Draw(canvas);
                canvas.Restore();
            }
        }
        finally
        {
            _behind.EndRecording();
        }

        _recorded = true;
        _recordedAt = now;
    }

    /// <summary>
    /// From the window down: each ancestor's background, then the children it draws before the branch that
    /// leads to the view.
    /// </summary>
    private void Collect(AView host)
    {
        _items.Clear();
        var chain = new List<(ViewGroup Parent, AView Child)>();
        for (var child = host; child.Parent is ViewGroup parent; child = parent)
            chain.Add((parent, child));

        for (var level = chain.Count - 1; level >= 0; level--)
        {
            var (parent, child) = chain[level];
            if (parent.Background is { } background)
                _items.Add(new Behind(parent, background));

            for (var i = 0; i < parent.ChildCount && parent.GetChildAt(i) is { } sibling && sibling != child; i++)
            {
                if (sibling.Width > 0 && sibling.Height > 0)
                    _items.Add(new Behind(sibling, null) { Width = sibling.Width, Height = sibling.Height });
            }
        }

        _collected = true;
        _recorded = false;
    }

    /// <summary>Something drawn behind the view: an ancestor's background, or a sibling drawn before the view's branch.</summary>
    private sealed class Behind(AView view, Drawable? background)
    {
        public AView View { get; } = view;

        public Drawable? Background { get; } = background;

        /// <summary>
        /// Whether the view's own drawing goes into the node rather than being referred to: a view group's
        /// children are drawn through their display lists, and a texture view through its layer, so the node
        /// shows what they draw next without being recorded again.
        /// </summary>
        public bool DrawsItself { get; } = background is null && view is not ViewGroup and not TextureView and not SurfaceView;

        public int Width { get; init; }

        public int Height { get; init; }

        public int X { get; set; }

        public int Y { get; set; }

        public bool Shows { get; set; }
    }

    public override void Draw(Canvas canvas)
    {
        var bounds = Bounds;
        if (bounds.IsEmpty)
            return;

        canvas.Save();

        if (Clip(bounds.Width(), bounds.Height()) is { } clip)
        {
            canvas.Translate(bounds.Left, bounds.Top);
            canvas.ClipPath(clip);
            canvas.Translate(-bounds.Left, -bounds.Top);
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

    /// <summary>
    /// The view's shape at this size. A rounded rectangle goes in as one, which the GPU clips to exactly;
    /// any other path is drawn into a mask on the CPU and uploaded as a texture each time what it clips changes.
    /// </summary>
    private Android.Graphics.Path? Clip(int width, int height)
    {
        if (_shape is null)
            return null;

        if (_clip is not null && _clipSize == (width, height))
            return _clip;

        var bounds = new Microsoft.Maui.Graphics.Rect(0, 0, width / _density, height / _density);
        if (_shape is Microsoft.Maui.Controls.Shapes.RoundRectangle round)
        {
            var max = (float)Math.Min(bounds.Width, bounds.Height) / 2;
            var radius = round.CornerRadius;
            float Corner(double r) => Math.Clamp((float)r, 0, max) * _density;
            var (topLeft, topRight, bottomRight, bottomLeft) = (Corner(radius.TopLeft), Corner(radius.TopRight), Corner(radius.BottomRight), Corner(radius.BottomLeft));
            _clip = new Android.Graphics.Path();
            _clip.AddRoundRect(0, 0, width, height, [topLeft, topLeft, topRight, topRight, bottomRight, bottomRight, bottomLeft, bottomLeft], Android.Graphics.Path.Direction.Cw!);
        }
        else
        {
            _clip = _shape.PathForBounds(bounds).AsAndroidPath(scaleX: _density, scaleY: _density);
        }

        _clipSize = (width, height);
        return _clip;
    }

    public override void SetAlpha(int alpha) => _paint.Alpha = alpha;

    public override void SetColorFilter(ColorFilter? colorFilter) => _paint.SetColorFilter(colorFilter);

    public override int Opacity => (int)Format.Translucent;
}
