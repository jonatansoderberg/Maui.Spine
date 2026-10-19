using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

/// <summary>A representation compiled for the skia renderer: a spine2d scene or an SkSL shader.</summary>
internal interface ISkiaAvatarModel
{
    ISkiaAvatarRenderer CreateRenderer();
}

/// <summary>Draws one avatar's frames: evaluate a frame, then draw it into a rectangle.</summary>
internal interface ISkiaAvatarRenderer : IDisposable
{
    void Evaluate(AvatarRenderFrame frame);

    void Draw(SKCanvas canvas, SKRect bounds, bool dark, SKColor? accent = null);

    /// <summary>Theme slot colours by name over the package's own; replaced as a whole when they change.</summary>
    IReadOnlyDictionary<string, SKColor>? ThemeColors { get; set; }
}

internal static class SkiaAvatarModel
{
    public static ISkiaAvatarModel Compile(AvatarPackage package, AvatarRepresentation representation) => representation.Format switch
    {
        "spine2d" => Spine2dModel.Compile(package, representation),
        "sksl" => AvatarShaderModel.Compile(package, representation),
        var format => throw new AvatarFormatException(representation.Model, $"the skia renderer draws spine2d and sksl, not {format}"),
    };
}
