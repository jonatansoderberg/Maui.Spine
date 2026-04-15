using System.Reflection;

namespace Plugin.Maui.SvgIcon;

/// <summary>
/// Converts SVG content into platform icon assets.
/// Obtain an instance via dependency injection after registering the service with
/// <c>builder.UseSpine()</c> or <c>builder.UseSvgIcon()</c> in <c>MauiProgram.cs</c>.
/// </summary>
public interface ISvgIconService
{
    /// <summary>
    /// Creates a <see cref="SvgIcon"/> from the given SVG stream.
    /// The stream is read immediately; the caller may dispose it after this call returns.
    /// </summary>
    /// <param name="svgStream">A readable stream containing valid SVG XML.</param>
    /// <param name="name">
    /// Optional logical name used in generated file names (replaces the <c>{name}</c>
    /// placeholder in <see cref="SvgIconOptions.FileNameTemplate"/>).
    /// Defaults to <c>"icon"</c> when not supplied.
    /// </param>
    SvgIcon FromSvg(Stream svgStream, string? name = null);

    /// <summary>
    /// Creates a <see cref="SvgIcon"/> from the given SVG string.
    /// </summary>
    /// <param name="svgContent">A string containing valid SVG XML.</param>
    /// <param name="name">
    /// Optional logical name used in generated file names (replaces the <c>{name}</c>
    /// placeholder in <see cref="SvgIconOptions.FileNameTemplate"/>).
    /// Defaults to <c>"icon"</c> when not supplied.
    /// </param>
    SvgIcon FromSvg(string svgContent, string? name = null);

    /// <summary>
    /// Creates a <see cref="SvgIcon"/> by locating a short SVG file name through the
    /// <see cref="Plugin.Maui.SvgImage.ResourceNameCache"/> — the same resolution mechanism
    /// used by <c>SvgImageSource.Svg="fish.svg"</c> in XAML.
    /// Falls back to scanning all loaded assemblies if the cache has no entry.
    /// </summary>
    /// <param name="svgFileName">
    /// Short file name, e.g. <c>"fish.svg"</c>.
    /// </param>
    /// <param name="name">
    /// Optional logical name used in generated file names.
    /// Defaults to the file name without extension (e.g. <c>"fish"</c>).
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when no embedded resource matching <paramref name="svgFileName"/> is found.
    /// </exception>
    SvgIcon FromEmbeddedSvg(string svgFileName, string? name = null);
}
