using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// A PNG rendered from an embedded SVG for one screen density. <see cref="Scale"/> is how many pixels
/// the bitmap holds per point, so the platform decodes a 44-point icon on a 3× screen from a
/// 132-pixel bitmap instead of stretching a 44-pixel one.
/// </summary>
public sealed class SvgBitmapImageSource : StreamImageSource
{
    internal SvgBitmapImageSource(string resourceName, ReadOnlyMemory<byte> png, float scale)
    {
        ResourceName = resourceName;
        Scale = scale;
        Stream = _ => Task.FromResult(OpenPng(png));
    }

    /// <summary>The manifest resource name the bitmap was rendered from.</summary>
    public string ResourceName { get; }

    /// <summary>Pixels per point in the bitmap.</summary>
    public float Scale { get; }

    static Stream OpenPng(ReadOnlyMemory<byte> png)
    {
        if (!MemoryMarshal.TryGetArray(png, out var segment))
            throw new InvalidOperationException("The rendered PNG is not array-backed.");

        return new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
    }
}
