using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar.Core;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// A skia avatar (spine2d scene or shader): the frame is evaluated and drawn in the paint pass. On
/// iOS and Mac Catalyst it draws on the GPU through an <c>SKMetalView</c>; elsewhere, or when asked,
/// on the CPU through an <see cref="SKCanvasView"/>.
/// </summary>
/// <remarks>
/// The CPU path rasterised a full-size SkSL shader at 8–9 fps on an iPhone and spent 4–18 ms per
/// frame on blurs, which is why the GPU is the default where SkiaSharp has a Metal view.
/// </remarks>
internal sealed class SkiaAvatarSurface : IAvatarSurface
{
    private readonly ISkiaAvatarRenderer _renderer;
    private readonly AvatarRenderFrame _frame = new();
    private readonly SKCanvasView? _canvas;
    private bool _dark;
    private SKColor? _accent;
    private bool _hasFrame;

    public SkiaAvatarSurface(ISkiaAvatarModel model, bool gpu)
    {
        _renderer = model.CreateRenderer();
#if IOS || MACCATALYST
        if (gpu)
        {
            _metal = new SkiaSharp.Views.iOS.SKMetalView
            {
                Opaque = false,
                BackgroundColor = UIKit.UIColor.Clear,
                // Drawn when the scheduler has a frame, not on the view's own clock.
                Paused = true,
                EnableSetNeedsDisplay = true,
                ClearColor = new Metal.MTLClearColor(0, 0, 0, 0),
            };
            if (_metal.Layer is CoreAnimation.CAMetalLayer layer)
                layer.Opaque = false;
            _metal.PaintSurface += OnPaintMetal;
            View = new NativeViewHost(_metal);
            RendererName = "skia (Metal)";
            // Light avatars render at full resolution on the GPU, so fine detail stays sharp.
            if (_renderer is AvatarShaderRenderer shader)
                shader.MaxPixels = null;
            return;
        }
#endif
        _canvas = new SKCanvasView { EnableTouchEvents = false, InputTransparent = true };
        _canvas.PaintSurface += OnPaintSurface;
        View = _canvas;
        RendererName = "skia (CPU)";
    }

    public View View { get; }

    public string RendererName { get; }

    public AvatarFrameStats Stats { get; } = new();

    public void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        // Copied, because the paint pass comes later and mirrors share the leader's frame.
        frame.CopyTo(_frame);
        _dark = dark;
        _accent = accent?.ToSKColor();
        _hasFrame = true;
        _canvas?.InvalidateSurface();
#if IOS || MACCATALYST
        _metal?.SetNeedsDisplay();
#endif
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) => Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);

    private void Paint(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
        if (!_hasFrame)
            return;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        _renderer.Evaluate(_frame);
        _renderer.Draw(canvas, new SKRect(0, 0, width, height), _dark, _accent);
        Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

#if IOS || MACCATALYST
    private readonly SkiaSharp.Views.iOS.SKMetalView? _metal;

    private void OnPaintMetal(object? sender, SkiaSharp.Views.iOS.SKPaintMetalSurfaceEventArgs e) => Paint(e.Surface.Canvas, e.Info.Width, e.Info.Height);
#endif

    public void Dispose()
    {
        if (_canvas is not null)
            _canvas.PaintSurface -= OnPaintSurface;
#if IOS || MACCATALYST
        if (_metal is not null)
        {
            _metal.PaintSurface -= OnPaintMetal;
            _metal.RemoveFromSuperview();
            _metal.Dispose();
        }
#endif
        _renderer.Dispose();
    }
}
