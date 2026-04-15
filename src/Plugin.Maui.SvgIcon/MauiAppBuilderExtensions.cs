using Plugin.Maui.SvgImage;

namespace Plugin.Maui.SvgIcon;

/// <summary>
/// Provides <see cref="MauiAppBuilder"/> extension methods for registering the
/// <c>Plugin.Maui.SvgIcon</c> services.
/// </summary>
public static class SvgIconExtensions
{
    /// <summary>
    /// Registers the SVG icon service with the MAUI application.
    /// Call this method inside <c>CreateMauiApp</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// When used alongside <c>builder.UseSpine()</c> you do not need to call this method —
    /// Spine registers <see cref="ISvgIconService"/> automatically.
    /// </remarks>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise rendering and caching options via <see cref="SvgIconOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance to allow method chaining.</returns>
    public static MauiAppBuilder UseSvgIcon(this MauiAppBuilder builder, Action<SvgIconOptions>? configure = null)
    {
        var options = new SvgIconOptions();
        configure?.Invoke(options);

        if (!builder.Services.Any(sd => sd.ServiceType == typeof(SvgIconOptions)))
            builder.Services.AddSingleton(options);

        if (!builder.Services.Any(sd => sd.ServiceType == typeof(ISvgIconService)))
            builder.Services.AddSingleton<ISvgIconService, SvgIconService>();

        return builder;
    }
}
