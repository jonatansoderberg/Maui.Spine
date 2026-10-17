using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Avatar.Tool;

/// <summary>
/// Draws a package with its own Skia renderers: the first representation one of them takes (a vector scene, a shader, a
/// glTF model without alpha-masked materials). A pose is the scheduler's after a moment in a state, under Reduce Motion so
/// nothing is caught mid-blink.
/// </summary>
internal sealed class AvatarPainter : IDisposable
{
    private readonly AvatarPackage _package;
    private readonly AvatarRepresentation _representation;
    private readonly ISkiaAvatarRenderer _renderer;

    private AvatarPainter(AvatarPackage package, AvatarRepresentation representation, ISkiaAvatarRenderer renderer)
    {
        _package = package;
        _representation = representation;
        _renderer = renderer;
    }

    public string Representation => $"{_representation.Id} ({_representation.Format})";

    /// <summary>A painter for <paramref name="package"/>, or <see langword="null"/> with the reason when no Skia renderer takes it.</summary>
    public static AvatarPainter? Create(AvatarPackage package, out string? reason)
    {
        reason = null;
        foreach (var representation in package.Manifest.Representations)
        {
            ISkiaAvatarModel? model = representation switch
            {
                { Renderer: "skia", Format: "spine2d" or "sksl" } => SkiaAvatarModel.Compile(package, representation),
                { Format: "glb" } => GltfSkiaModel.TryCreate(package, representation),
                _ => null,
            };
            if (model is not null)
                return new AvatarPainter(package, representation, model.CreateRenderer());
        }

        reason = package.Manifest.Representations.Any(r => r.Format == "glb")
            ? "its glTF model has alpha-masked materials (hair cards, lashes), which only SceneKit draws: capture the posters on Apple and pass --poster-light and --poster-dark"
            : "no representation has a Skia renderer";
        return null;
    }

    /// <summary>The avatar in <paramref name="state"/> with <paramref name="expression"/>, on a transparent square.</summary>
    public SKImage Draw(int size, bool dark, AvatarState state = AvatarState.Idle, AvatarExpression expression = AvatarExpression.Neutral)
    {
        var time = new ManualTime();
        var scheduler = new AvatarScheduler(_package, _representation, time);
        scheduler.SetReducedMotion(true);
        scheduler.SetState(state);
        if (expression != AvatarExpression.Neutral)
            scheduler.SetExpression(new AvatarExpressionRequest(expression, 1, TimeSpan.FromMinutes(1)));
        if (state == AvatarState.Speaking)
            scheduler.SetOutputLevel(0.6f);

        AvatarRenderFrame frame = null!;
        for (var i = 0; i < 45; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(33));
            frame = scheduler.Update();
        }

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        _renderer.Evaluate(frame);
        _renderer.Draw(surface.Canvas, new SKRect(0, 0, size, size), dark);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>A grid of the avatar, one cell per label, each with its label under it.</summary>
    public SKImage Sheet(IReadOnlyList<(string Label, AvatarState State, AvatarExpression Expression)> cells, int cell, bool dark)
    {
        const int columns = 4;
        const int caption = 28;
        var rows = (cells.Count + columns - 1) / columns;
        using var surface = SKSurface.Create(new SKImageInfo(columns * cell, rows * (cell + caption)));
        var canvas = surface.Canvas;
        canvas.Clear(dark ? new SKColor(0x1C, 0x1C, 0x1E) : SKColors.White);
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { IsAntialias = true, Color = dark ? SKColors.White : SKColors.Black };

        for (var i = 0; i < cells.Count; i++)
        {
            var (label, state, expression) = cells[i];
            float x = i % columns * cell, y = i / columns * (cell + caption);
            using var image = Draw(cell, dark, state, expression);
            canvas.DrawImage(image, x, y);
            canvas.DrawText(label, x + cell / 2f, y + cell + 20, SKTextAlign.Center, font, paint);
        }
        canvas.Flush();
        return surface.Snapshot();
    }

    public void Dispose() => _renderer.Dispose();

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
