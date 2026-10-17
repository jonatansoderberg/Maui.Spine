using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Plugin.Maui.Spine.Controls;

/// <summary>The visuals <see cref="VoiceView"/> can show, beyond the built-in <c>orb</c>, <c>bars</c>, <c>ring</c> and <c>avatar</c>.</summary>
public sealed class SpineVoiceViewOptions
{
    internal VoiceVisualRegistry Registry { get; } = new();

    /// <summary>A visual that draws on the canvas, by name; a new one for each view.</summary>
    public SpineVoiceViewOptions AddVisual<T>(string name) where T : VoiceVisual, new()
    {
        Registry.Add(name, static () => new T());
        return this;
    }

    /// <summary>A visual that hosts its own view, by name; a new one for each view.</summary>
    public SpineVoiceViewOptions AddVisualView<T>(string name) where T : VoiceVisualView, new()
    {
        Registry.Set(name, static () => new T());
        return this;
    }
}

/// <summary>Registers <c>Plugin.Maui.Spine.Controls.Voice</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineVoiceViewExtensions
{
    internal static readonly SpineVoiceViewOptions Options = new();

    /// <summary>
    /// Registers the SkiaSharp renderers <see cref="VoiceView"/> draws with, and the visuals it can show. <c>UseSpine()</c>
    /// calls it for an app that references this package; call it yourself to add visuals. Calling it again adds to them.
    /// </summary>
    public static MauiAppBuilder UseSpineVoiceView(this MauiAppBuilder builder, Action<SpineVoiceViewOptions>? configure = null)
    {
        configure?.Invoke(Options);
        if (builder.Services.Any(static d => d.ServiceType == typeof(SpineVoiceViewOptions)))
            return builder;

        builder.Services.TryAddSingleton(Options);
        builder.UseSkiaSharp();
        return builder;
    }
}
