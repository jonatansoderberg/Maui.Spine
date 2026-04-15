using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Plugin.Maui.AnimatedLabel;

public static class AnimatedLabelExtensions
{
    /// <summary>
    /// Registers the SkiaSharp renderers required by <see cref="AnimatedLabel"/>.
    /// Call this in your <c>MauiProgram.CreateMauiApp</c> builder chain.
    /// </summary>
    public static MauiAppBuilder UseAnimatedLabel(this MauiAppBuilder builder)
    {
        builder.UseSkiaSharp();
        return builder;
    }
}
