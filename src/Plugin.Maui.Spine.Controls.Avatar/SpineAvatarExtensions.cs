using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Defaults for <see cref="AvatarView"/>.</summary>
public sealed class SpineAvatarOptions
{
    /// <summary>
    /// Renderers beyond the built-in ones (Skia on every platform, SceneKit on iOS and Mac Catalyst), asked first.
    /// A renderer package adds itself here.
    /// </summary>
    [Experimental(AvatarRenderer.DiagnosticId)]
    public IList<AvatarRenderer> Renderers { get; } = [];

    /// <summary>
    /// On Android, whether an <c>sksl</c> avatar draws on the GPU (OpenGL ES, a texture view with a render thread of its
    /// own) at full size, rather than on the CPU in a buffer of 112 pixels scaled up. On by default; off keeps every
    /// avatar on the CPU.
    /// </summary>
    public bool GpuOnAndroid { get; set; } = true;
}

/// <summary>Registers <c>Plugin.Maui.Spine.Controls.Avatar</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineAvatarExtensions
{
    internal static readonly SpineAvatarOptions Options = new();

    /// <summary>
    /// Registers the SkiaSharp and native views <see cref="AvatarView"/> draws with, and its defaults. <c>UseSpine()</c>
    /// calls it for an app that references this package; call it yourself to set defaults. Calling it again changes them.
    /// </summary>
    public static MauiAppBuilder UseSpineAvatar(this MauiAppBuilder builder, Action<SpineAvatarOptions>? configure = null)
    {
        configure?.Invoke(Options);
        if (builder.Services.Any(static d => d.ServiceType == typeof(SpineAvatarOptions)))
            return builder;

        builder.Services.TryAddSingleton(Options);
        builder.UseSkiaSharp();
#if IOS || MACCATALYST
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<NativeViewHost, NativeViewHostHandler>());
#endif
        return builder;
    }
}
