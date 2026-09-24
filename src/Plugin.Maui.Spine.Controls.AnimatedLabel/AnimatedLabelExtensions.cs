using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Plugin.Maui.Spine.Controls;

public static class AnimatedLabelExtensions
{
    /// <summary>
    /// Registers the SkiaSharp renderers required by <see cref="AnimatedLabel"/>. <c>UseSpine()</c>
    /// calls it for an app that references this package; an app without Spine calls it itself.
    /// Calling it more than once is harmless.
    /// </summary>
    public static MauiAppBuilder UseAnimatedLabel(this MauiAppBuilder builder)
    {
        if (builder.Services.Any(static d => d.ServiceType == typeof(Registered)))
            return builder;

        builder.Services.AddSingleton(new Registered());
        builder.UseSkiaSharp();
        return builder;
    }

    private sealed class Registered;
}
