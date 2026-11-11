#if WINDOWS

using System.Buffers.Binary;
using Microsoft.UI.Xaml.Media.Imaging;
using WImageSource = Microsoft.UI.Xaml.Media.ImageSource;

namespace Plugin.Maui.Spine.Svg;

/// <summary>
/// Decodes an <see cref="SvgBitmapImageSource"/> at its own scale. MAUI's stream service gives WinUI a
/// <see cref="BitmapImage"/> that shows one pixel per point, so the bitmap had to be rendered at 1× and the display
/// stretched it: every SVG icon was soft on a 150 % or 200 % screen. Decoding to the icon's size in logical pixels
/// keeps the bitmap's own pixels and lays it out at its point size, as on iOS and Android.
/// </summary>
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
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = Math.Max(1, (int)Math.Round(pixelWidth / source.Scale)),
            DecodePixelHeight = Math.Max(1, (int)Math.Round(pixelHeight / source.Scale)),
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
