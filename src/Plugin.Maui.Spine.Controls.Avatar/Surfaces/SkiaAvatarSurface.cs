using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Plugin.Maui.Spine.Controls;

/// <summary>The built-in renderer for <c>skia</c> representations: spine2d scenes and SkSL shaders, on every platform.</summary>
internal sealed class SkiaAvatarRenderer : AvatarRenderer
{
    public static SkiaAvatarRenderer Instance { get; } = new();

    public override string Renderer => "skia";

    public override bool CanDraw(AvatarRepresentation representation) =>
        representation.Renderer == "skia" && representation.Format is "spine2d" or "sksl";

    public override AvatarSurface CreateSurface(AvatarPackage package, AvatarRepresentation representation) =>
        new SkiaAvatarSurface(SkiaAvatarModel.Compile(package, representation));
}

/// <summary>
/// The built-in renderer for <c>native3d</c> GLB representations where SceneKit is not (Android, Windows): Skia on the
/// CPU, for models without alpha-masked materials (<see cref="GltfSkiaModel"/>).
/// </summary>
internal sealed class GltfSkiaAvatarRenderer : AvatarRenderer
{
    public static GltfSkiaAvatarRenderer Instance { get; } = new();

    public override string Renderer => "native3d";

    public override bool CanDraw(AvatarRepresentation representation) =>
        representation.Renderer == "native3d" && representation.Format == "glb";

    public override AvatarSurface CreateSurface(AvatarPackage package, AvatarRepresentation representation) =>
        new SkiaAvatarSurface(new GltfSkiaModel(package, representation));
}

/// <summary>
/// A skia avatar: the frame is evaluated and drawn in the paint pass. On iOS and Mac Catalyst it draws on the GPU through
/// an <c>SKMetalView</c>; on Android a shader draws on the GPU through an <see cref="SKGLView"/> (OpenGL ES) unless
/// <see cref="SpineAvatarOptions.GpuOnAndroid"/> is off; elsewhere, and for scenes, on the CPU through an <see cref="SKCanvasView"/>.
/// </summary>
/// <remarks>
/// The CPU path rasterised a full-size SkSL shader at 8–9 fps on an iPhone and spent 4–18 ms per frame on blurs, which
/// is why the GPU is used where SkiaSharp has a Metal view. On the CPU a shader draws into a small buffer scaled up
/// (<see cref="AvatarShaderRenderer.MaxPixels"/>), which a TV shows as soft, stepped edges. The GL view paints on a
/// thread of its own, so the frame and the renderer are handed over under a lock.
/// </remarks>
internal sealed class SkiaAvatarSurface : AvatarSurface
{
    private readonly ISkiaAvatarRenderer _renderer;
    private readonly AvatarRenderFrame _frame = new();
    private readonly object _gate = new();
#if !(IOS || MACCATALYST)
    private readonly SKCanvasView? _canvas;
#endif
#if ANDROID
    private readonly SKGLView? _gl;
#endif
    private bool _dark;
    private bool _disposed;
    private SKColor? _accent;
    private IReadOnlyDictionary<string, Color>? _themeColors;
    private IReadOnlyDictionary<string, string>? _options;
    private bool _hasFrame;

    public SkiaAvatarSurface(ISkiaAvatarModel model)
    {
        _renderer = model.CreateRenderer();
#if IOS || MACCATALYST
        _metal = new SkiaSharp.Views.iOS.SKMetalView
        {
            Opaque = false,
            BackgroundColor = UIKit.UIColor.Clear,
            // Drawn when the scheduler has a frame, not on the view's own clock.
            Paused = true,
            EnableSetNeedsDisplay = true,
            ClearColor = new Metal.MTLClearColor(0, 0, 0, 0),
            UserInteractionEnabled = false,
        };
        if (_metal.Layer is CoreAnimation.CAMetalLayer layer)
            layer.Opaque = false;
        _metal.PaintSurface += OnPaintMetal;
        View = new NativeViewHost(_metal) { InputTransparent = true };
        RendererName = "skia (Metal)";
        // On the GPU a shader draws at full resolution, so fine detail stays sharp.
        if (_renderer is AvatarShaderRenderer shader)
            shader.MaxPixels = null;
#else
#if ANDROID
        if (_renderer is AvatarShaderRenderer gpuShader && SpineAvatarExtensions.Options.GpuOnAndroid)
        {
            _gl = new SKGLView { HasRenderLoop = false, EnableTouchEvents = false, InputTransparent = true };
            _gl.PaintSurface += OnPaintGl;
            View = _gl;
            RendererName = "skia (OpenGL ES)";
            gpuShader.MaxPixels = null;
            return;
        }
#endif
        _canvas = new SKCanvasView { EnableTouchEvents = false, InputTransparent = true };
        _canvas.PaintSurface += OnPaintSurface;
        View = _canvas;
        RendererName = "skia (CPU)";
#endif
    }

    public override View View { get; }

    public override string RendererName { get; }

    public override void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        // Copied, because the paint pass comes later (on the GL view's thread on Android).
        lock (_gate)
        {
            frame.CopyTo(_frame);
            _dark = dark;
            _accent = accent?.ToSKColor();
            if (!ReferenceEquals(_themeColors, ThemeColors))
            {
                _themeColors = ThemeColors;
                _renderer.ThemeColors = ThemeColors.Count == 0 ? null : ThemeColors.ToDictionary(p => p.Key, p => p.Value.ToSKColor(), StringComparer.Ordinal);
            }
            if (!ReferenceEquals(_options, Options))
            {
                _options = Options;
                _renderer.Options = Options;
            }
            _hasFrame = true;
        }
#if IOS || MACCATALYST
        _metal.SetNeedsDisplay();
#else
#if ANDROID
        _gl?.InvalidateSurface();
#endif
        _canvas?.InvalidateSurface();
#endif
    }

#if !(IOS || MACCATALYST)
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) => Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
#endif
#if ANDROID
    private void OnPaintGl(object? sender, SKPaintGLSurfaceEventArgs e) => Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
#endif

    private void Paint(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
        lock (_gate)
        {
            if (!_hasFrame || _disposed)
                return;

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            _renderer.Evaluate(_frame);
            _renderer.Draw(canvas, new SKRect(0, 0, width, height), _dark, _accent);
            Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
    }

#if IOS || MACCATALYST
    private readonly SkiaSharp.Views.iOS.SKMetalView _metal;

    private void OnPaintMetal(object? sender, SkiaSharp.Views.iOS.SKPaintMetalSurfaceEventArgs e) => Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
#endif

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;
#if IOS || MACCATALYST
        _metal.PaintSurface -= OnPaintMetal;
        _metal.RemoveFromSuperview();
        _metal.Dispose();
#else
        if (_canvas is not null)
            _canvas.PaintSurface -= OnPaintSurface;
#endif
#if ANDROID
        if (_gl is not null)
            _gl.PaintSurface -= OnPaintGl;
#endif
        lock (_gate)
        {
            _disposed = true;
            _renderer.Dispose();
        }
    }
}
