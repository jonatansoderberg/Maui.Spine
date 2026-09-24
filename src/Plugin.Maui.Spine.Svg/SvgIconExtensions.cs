namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Provides <see cref="MauiAppBuilder"/> extension methods for registering the
/// <c>Plugin.Maui.Spine.Svg</c> services.
/// </summary>
public static class SvgIconExtensions
{
    /// <summary>
    /// Registers the SVG icon service with the MAUI application.
    /// Call this method inside <c>CreateMauiApp</c> before <c>builder.Build()</c>.
    /// </summary>
    /// <remarks>
    /// When used alongside <c>builder.UseSpine()</c> you do not need to call this method —
    /// Spine registers <see cref="ISvgIconService"/> automatically. Call it only to change
    /// <see cref="SvgIconOptions"/>, before or after <c>UseSpine()</c>: every call configures the
    /// same options instance, so calling it more than once is harmless.
    /// </remarks>
    /// <param name="builder">The <see cref="MauiAppBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise rendering and caching options via <see cref="SvgIconOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance to allow method chaining.</returns>
    public static MauiAppBuilder UseSvgIcon(this MauiAppBuilder builder, Action<SvgIconOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.FirstOrDefault(static d => d.ServiceType == typeof(SvgIconOptions) && !d.IsKeyedService)?.ImplementationInstance
            is not SvgIconOptions options)
        {
            options = new SvgIconOptions();
            services.AddSingleton(options);
        }

        configure?.Invoke(options);

        if (!services.Any(static d => d.ServiceType == typeof(ISvgIconService)))
            services.AddSingleton<ISvgIconService, SvgIconService>();

        return builder;
    }
}
