using System.Diagnostics;
using Plugin.Maui.Spine.Controls.Avatar.Core;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>A skia avatar (spine2d scene or shader) on an <see cref="SKCanvasView"/>: the frame is evaluated and drawn in the paint pass.</summary>
internal sealed class SkiaAvatarSurface : IAvatarSurface
{
    private readonly SKCanvasView _canvas;
    private readonly ISkiaAvatarRenderer _renderer;
    private readonly AvatarRenderFrame _frame = new();
    private bool _dark;
    private SKColor? _accent;
    private bool _hasFrame;

    public SkiaAvatarSurface(ISkiaAvatarModel model)
    {
        _renderer = model.CreateRenderer();
        _canvas = new SKCanvasView { EnableTouchEvents = false, InputTransparent = true };
        _canvas.PaintSurface += OnPaintSurface;
    }

    public View View => _canvas;

    public string RendererName => "skia";

    public AvatarFrameStats Stats { get; } = new();

    public void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        // Copied, because the paint pass comes later and mirrors share the leader's frame.
        frame.CopyTo(_frame);
        _dark = dark;
        _accent = accent?.ToSKColor();
        _hasFrame = true;
        _canvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (!_hasFrame)
            return;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        _renderer.Evaluate(_frame);
        _renderer.Draw(canvas, new SKRect(0, 0, e.Info.Width, e.Info.Height), _dark, _accent);
        Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    public void Dispose()
    {
        _canvas.PaintSurface -= OnPaintSurface;
        _renderer.Dispose();
    }
}
