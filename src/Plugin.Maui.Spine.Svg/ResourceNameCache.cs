using System.Collections.Concurrent;
using System.Reflection;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// A thread-safe cache that maps embedded SVG resource names to the assemblies that contain them.
/// </summary>
/// <remarks>
/// Register this class as a singleton via <see cref="MauiAppBuilderExtensions.UseEmbeddedSvgImages(MauiAppBuilder)"/>
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

    // The assemblies already scanned, so a later call (UseSpine after an explicit
    // UseEmbeddedSvgImages, or the other way round) adds only what is new.
    private static readonly HashSet<Assembly> _scanned = [];

    // Short file name -> resolved resource name (or null), cleared whenever an assembly is added.
    private static readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Scans the given assemblies for embedded <c>.svg</c> resources and adds them to the cache.
    /// When <paramref name="assemblies"/> is <see langword="null"/> or empty, the application
    /// entry assembly is used as a fallback.
    /// Assemblies already scanned are skipped, so calling it again only adds new ones.
    /// </summary>
    public void Initialize(IEnumerable<Assembly>? assemblies = null)
    {
        var sources = assemblies?.Where(a => a is not null).ToList() ?? [];

        // If the caller supplied nothing at all, use the app entry assembly.
        if (sources.Count == 0 && Assembly.GetEntryAssembly() is { } entryAssembly)
            sources.Add(entryAssembly);

        // Always include the plugin's own assembly so its built-in SVG resources are
        // discoverable regardless of what the caller passes in.
        sources.Add(typeof(SvgBitmapLoader).Assembly);

        lock (_scanned)
        {
            if (_scanned.Count == 0 && LoadIconsAssembly() is { } icons)
                sources.Add(icons);

            foreach (var assembly in sources)
            {
                if (!_scanned.Add(assembly))
                    continue;

                foreach (var name in assembly.GetManifestResourceNames()
                             .Where(n => n.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
                {
                    _resourceMap.TryAdd(name, assembly);
                }
            }

            _resolved.Clear();
        }
    }

    /// <summary>
    /// Resolves a short SVG file name to its fully-qualified manifest resource identifier.
    /// </summary>
    /// <param name="svgFileName">
    /// The short file name to look up, e.g. <c>"arrow.svg"</c>. The match is case-insensitive
    /// and must be the whole file name: <c>"lock.svg"</c> finds <c>App.Images.Lock.svg</c>, not
    /// <c>App.Images.Clock.svg</c>.
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
        if (theme == SvgTheme.Dark && Find(ToDarkFileName(svgFileName)) is { } dark)
            return dark;

        return Find(svgFileName);
    }

    /// <summary>
    /// Opens a readable stream for the embedded SVG named <paramref name="svgFileName"/>
    /// (case-insensitive; see <see cref="Resolve"/>).
    /// Returns <see langword="null"/> if no match is found.
    /// </summary>
    /// <param name="svgFileName">The short SVG file name, e.g. <c>"arrow.svg"</c>.</param>
    /// <param name="theme">
    /// The theme to resolve for. When <see cref="SvgTheme.Dark"/>, a resource whose name ends
    /// with <c>"_dark.svg"</c> is tried first before falling back to the unthemed name.
    /// </param>
    public Stream? OpenStream(string svgFileName, SvgTheme theme = SvgTheme.Light) =>
        Resolve(svgFileName, theme) is { } name && _resourceMap.TryGetValue(name, out var assembly)
            ? assembly.GetManifestResourceStream(name)
            : null;

    // Lookups run for every image source; the scan over all resource names is done once per name.
    private static string? Find(string fileName) => _resolved.GetOrAdd(fileName, static name =>
    {
        var match = SvgResourceMatch.Find(_resourceMap.Keys, name, out var ambiguous);

        if (ambiguous)
            System.Diagnostics.Debug.WriteLine($"[Spine.Svg] '{name}' matches more than one embedded SVG; using '{match}'.");

        return match;
    });

    // Plugin.Maui.Spine.Svg.Icons is a resource-only package: an app that uses its icons by file
    // name references no type in it, so it is loaded by name rather than found through a reference.
    private static Assembly? LoadIconsAssembly()
    {
        try
        {
            return Assembly.Load(new AssemblyName("Plugin.Maui.Spine.Svg.Icons"));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static string ToDarkFileName(string svgFileName)
    {
        var ext = Path.GetExtension(svgFileName);
        var stem = Path.GetFileNameWithoutExtension(svgFileName);
        return $"{stem}_dark{ext}";
    }
}



