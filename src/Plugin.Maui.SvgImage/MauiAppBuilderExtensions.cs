using System.Reflection;

namespace Plugin.Maui.SvgImage;

/// <summary>
/// Provides <see cref="MauiAppBuilder"/> extension methods for registering the
/// <c>Plugin.Maui.SvgImage</c> services.
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
    /// </remarks>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <param name="assemblies">
    /// The assemblies to scan for embedded SVG resources.
    /// Pass the app assembly via <c>typeof(MauiProgram).Assembly</c>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance to allow method chaining.</returns>
    public static MauiAppBuilder UseEmbeddedSvgImages(this MauiAppBuilder builder, params Assembly[] assemblies)
    {
        if (!builder.Services.Any(sd => sd.ServiceType == typeof(ResourceNameCache)))
            builder.Services.AddSingleton<ResourceNameCache>();

        using var scope = builder.Services.BuildServiceProvider();
        var registry = scope.GetRequiredService<ResourceNameCache>();
        registry.Initialize(assemblies.Length > 0 ? assemblies : null);
        SvgBitmapLoader.Registry = registry;

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