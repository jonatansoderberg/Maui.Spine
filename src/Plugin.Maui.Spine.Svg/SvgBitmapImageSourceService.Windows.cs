#if WINDOWS

using System.Buffers.Binary;
using Microsoft.UI.Xaml.Media.Imaging;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Decodes an <see cref="SvgBitmapImageSource"/> at its own scale. MAUI's stream service gives WinUI a
/// <see cref="BitmapImage"/> that shows one pixel per point, so the bitmap had to be rendered at 1× and the display
/// stretched it: every SVG icon was soft on a 150 % or 200 % screen. The bitmap is decoded to its own pixels; the
/// view's point size (the SVG is rendered for it) lays it out, one bitmap pixel per screen pixel.
/// </summary>
/// <remarks>
/// Not <see cref="DecodePixelType.Logical"/>: a bitmap decoded before it is in a window gets scale 1, so a 39-pixel
/// icon at 150 % was decoded to 26 pixels and stretched back up, as soft as before.
/// </remarks>
internal sealed class SvgBitmapImageSourceService : ImageSourceService, IImageSourceService<SvgBitmapImageSource>
{
    // MAUI's image source factory instantiates services with Activator.CreateInstance, which needs
    // a real parameterless constructor; an optional logger parameter does not count.
    public SvgBitmapImageSourceService()
    {
    }

    public override async Task<IImageSourceServiceResult<WImageSource>?> GetImageSourceAsync(IImageSource imageSource, float scale = 1, CancellationToken cancellationToken = default)
    {
        if (imageSource is not SvgBitmapImageSource source || source.IsEmpty)
            return null;

        using var stream = await ((IStreamImageSource)source).GetStreamAsync(cancellationToken);
        var (pixelWidth, pixelHeight) = PngSize(stream)
            ?? throw new InvalidOperationException($"The bitmap rendered from \"{source.ResourceName}\" is not a PNG.");

        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = pixelWidth,
            DecodePixelHeight = pixelHeight,
        };
        stream.Position = 0;
        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

        return new ImageSourceServiceResult(bitmap);
    }

    // The width and height in a PNG's IHDR chunk, which always comes first: 8 bytes of signature, the chunk's length
    // and type, then the two sizes as big-endian 32-bit numbers.
    static (int Width, int Height)? PngSize(Stream stream)
    {
        Span<byte> header = stackalloc byte[24];
        stream.Position = 0;
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length
            || header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
            return null;

        return (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }
}

#endif
