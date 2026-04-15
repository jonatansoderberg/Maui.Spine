using System.Collections.Concurrent;
using System.Reflection;

namespace Plugin.Maui.SvgImage;

/// <summary>
/// A thread-safe cache that maps embedded SVG resource names to the assemblies that contain them.
/// </summary>
/// <remarks>
/// Register this class as a singleton via <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages"/>
/// which also calls <see cref="Initialize"/> at startup. Inject it into any service that needs to
/// resolve short SVG file names (e.g. <c>"icon.svg"</c>) to their fully-qualified manifest
/// resource identifiers or to open the corresponding stream.
/// </remarks>
public sealed class ResourceNameCache
{
    // Maps fully-qualified resource name -> the assembly that contains it.
    private static readonly ConcurrentDictionary<string, Assembly> _resourceMap =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises a new instance of <see cref="ResourceNameCache"/>.
    /// </summary>
    public ResourceNameCache() { }

    /// <summary>
    /// Scans the given assemblies for embedded <c>.svg</c> resources and populates the cache.
    /// When <paramref name="assemblies"/> is <see langword="null"/> or empty, the application
    /// entry assembly is used as a fallback.
    /// This method is idempotent — subsequent calls are no-ops once the cache has been populated.
    /// </summary>
    public void Initialize(IEnumerable<Assembly>? assemblies = null)
    {
        if (_resourceMap.Count > 0) return;

        // Always include the plugin's own assembly so its built-in SVG resources are
        // discoverable regardless of what the caller passes in.
        var pluginAssembly = Assembly.GetAssembly(typeof(SvgBitmapLoader));

        var sources = (assemblies?.Where(a => a is not null) ?? Enumerable.Empty<Assembly>())
            .Concat(pluginAssembly is not null ? [pluginAssembly] : [])
            .Distinct()
            .ToList();

        // If the caller supplied nothing at all, also include the app entry assembly.
        if (assemblies is null || !assemblies.Any())
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly is not null && !sources.Contains(entryAssembly))
                sources.Add(entryAssembly);
        }

        foreach (var assembly in sources)
        {
            foreach (var name in assembly.GetManifestResourceNames()
                         .Where(n => n.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
            {
                _resourceMap.TryAdd(name, assembly);
            }
        }
    }

    /// <summary>
    /// Resolves a short SVG file name to its fully-qualified manifest resource identifier.
    /// </summary>
    /// <param name="svgFileName">
    /// The short file name to look up, e.g. <c>"arrow.svg"</c>. The match is case-insensitive
    /// and performed against the trailing segment of each resource name.
    /// </param>
    /// <param name="theme">
    /// The theme to resolve for. When <see cref="SvgTheme.Dark"/>, a resource whose name ends
    /// with <c>"_dark.svg"</c> (e.g. <c>"arrow_dark.svg"</c>) is tried first before falling
    /// back to the unthemed name.
    /// </param>
    /// <returns>
    /// The fully-qualified manifest resource name (e.g. <c>"MyApp.Images.arrow.svg"</c>),
    /// or <see langword="null"/> if no match is found.
    /// </returns>
    public string? Resolve(string svgFileName, SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            var darkName = ToDarkFileName(svgFileName);
            var darkMatch = _resourceMap.Keys.FirstOrDefault(k =>
                k.EndsWith(darkName, StringComparison.OrdinalIgnoreCase));
            if (darkMatch is not null) return darkMatch;
        }

        return _resourceMap.Keys.FirstOrDefault(k =>
            k.EndsWith(svgFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens a readable stream for the embedded SVG whose name ends with
    /// <paramref name="svgFileName"/> (case-insensitive).
    /// Returns <see langword="null"/> if no match is found.
    /// </summary>
    /// <param name="svgFileName">The short SVG file name, e.g. <c>"arrow.svg"</c>.</param>
    /// <param name="theme">
    /// The theme to resolve for. When <see cref="SvgTheme.Dark"/>, a resource whose name ends
    /// with <c>"_dark.svg"</c> is tried first before falling back to the unthemed name.
    /// </param>
    public Stream? OpenStream(string svgFileName, SvgTheme theme = SvgTheme.Light)
    {
        if (theme == SvgTheme.Dark)
        {
            var darkName = ToDarkFileName(svgFileName);
            var darkEntry = _resourceMap.FirstOrDefault(kv =>
                kv.Key.EndsWith(darkName, StringComparison.OrdinalIgnoreCase));
            if (darkEntry.Key is not null)
                return darkEntry.Value.GetManifestResourceStream(darkEntry.Key);
        }

        var entry = _resourceMap.FirstOrDefault(kv =>
            kv.Key.EndsWith(svgFileName, StringComparison.OrdinalIgnoreCase));

        return entry.Key is null ? null : entry.Value.GetManifestResourceStream(entry.Key);
    }

    private static string ToDarkFileName(string svgFileName)
    {
        var ext = Path.GetExtension(svgFileName);
        var stem = Path.GetFileNameWithoutExtension(svgFileName);
        return $"{stem}_dark{ext}";
    }
}



