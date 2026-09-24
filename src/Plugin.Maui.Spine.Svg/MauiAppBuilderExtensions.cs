using System.Reflection;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Provides <see cref="MauiAppBuilder"/> extension methods for registering the
/// <c>Plugin.Maui.Spine.Svg</c> services.
/// </summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers the embedded SVG image services with the MAUI application and scans
    /// <paramref name="assemblies"/> for embedded <c>.svg</c> resources.
    /// </summary>
    /// <remarks>
    /// Call this method inside <c>CreateMauiApp</c> before <c>builder.Build()</c>.
    /// When no assemblies are provided the application entry assembly is used.
    /// <c>UseSpine()</c> calls it with its assemblies; calling it again only scans the new ones.
    /// </remarks>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <param name="assemblies">
    /// The assemblies to scan for embedded SVG resources.
    /// Pass the app assembly via <c>typeof(MauiProgram).Assembly</c>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance to allow method chaining.</returns>
    public static MauiAppBuilder UseEmbeddedSvgImages(this MauiAppBuilder builder, params Assembly[] assemblies)
    {
        if (builder.Services.FirstOrDefault(static sd => sd.ServiceType == typeof(ResourceNameCache) && !sd.IsKeyedService)?.ImplementationInstance
            is ResourceNameCache registered)
        {
            // Already set up by an earlier call: only the assemblies are new.
            registered.Initialize(assemblies.Length > 0 ? assemblies : null);
            return builder;
        }

        var registry = new ResourceNameCache();
        builder.Services.AddSingleton(registry);
        registry.Initialize(assemblies.Length > 0 ? assemblies : null);
        SvgBitmapLoader.Registry = registry;

        // The first rasterization loads Svg.Skia and SkiaSharp (about 150 ms on a phone); pay it
        // here, off the main thread, rather than when the first icon on the first page asks.
        _ = Task.Run(SvgBitmapLoader.WarmUp);

#if IOS || MACCATALYST || ANDROID
        builder.ConfigureImageSources(static services =>
            services.AddService<SvgBitmapImageSource, SvgBitmapImageSourceService>());
#endif

        return builder;
    }

    /// <summary>
    /// Registers the embedded SVG image services with the MAUI application, using the
    /// application entry assembly for resource discovery.
    /// </summary>
    /// <remarks>
    /// Prefer the <c>params Assembly[]</c> overload when the entry assembly cannot be
    /// determined automatically (e.g. on Android where <c>Assembly.GetEntryAssembly()</c>
    /// returns <see langword="null"/>).
    /// </remarks>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <returns>The same <paramref name="builder"/> instance to allow method chaining.</returns>
    public static MauiAppBuilder UseEmbeddedSvgImages(this MauiAppBuilder builder)
        => builder.UseEmbeddedSvgImages(assemblies: []);
}