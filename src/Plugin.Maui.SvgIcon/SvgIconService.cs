using Plugin.Maui.SvgImage;
using System.Text;

namespace Plugin.Maui.SvgIcon;

internal sealed class SvgIconService : ISvgIconService
{
    private readonly SvgIconOptions _options;
    private readonly ResourceNameCache _cache;

    public SvgIconService(SvgIconOptions options, ResourceNameCache cache)
    {
        _options = options;
        _cache = cache;
    }

    /// <inheritdoc/>
    public SvgIcon FromSvg(Stream svgStream, string? name = null)
    {
        using var ms = new MemoryStream();
        svgStream.CopyTo(ms);
        return new SvgIcon(ms.ToArray(), _options, name ?? "icon");
    }

    /// <inheritdoc/>
    public SvgIcon FromSvg(string svgContent, string? name = null) =>
        new SvgIcon(Encoding.UTF8.GetBytes(svgContent), _options, name ?? "icon");

    /// <inheritdoc/>
    public SvgIcon FromEmbeddedSvg(string svgFileName, string? name = null)
    {
        // Primary: delegate to the shared ResourceNameCache (same resolution as SvgImageSource.Svg).
        var stream = _cache.OpenStream(svgFileName);

        // Fallback: scan all loaded assemblies (covers standalone UseSvgIcon() without UseSpine).
        if (stream is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var fullName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(svgFileName, StringComparison.OrdinalIgnoreCase));

                if (fullName is not null)
                {
                    stream = assembly.GetManifestResourceStream(fullName);
                    break;
                }
            }
        }

        if (stream is null)
            throw new FileNotFoundException(
                $"Embedded SVG resource '{svgFileName}' was not found. " +
                $"Ensure the file is an EmbeddedResource and the assembly is registered via UseSpine/UseEmbeddedSvgImages.");

        using (stream)
            return FromSvg(stream, name ?? Path.GetFileNameWithoutExtension(svgFileName));
    }
}
