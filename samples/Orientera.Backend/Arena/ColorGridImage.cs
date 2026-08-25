using SkiaSharp;

namespace Orientera.Backend.Arena;

/// <summary>
/// Bryggan mellan renderarens flyttalsbilder och riktiga bildfiler, på SkiaSharp.
/// Allt går genom ett standardiserat RGBA-format så att ingen kod behöver bry sig om
/// vilken pixelordning plattformens avkodare råkar föredra.
/// </summary>
public static class ColorGridImage
{
    /// <summary>
    /// Till 8 bitar per kanal. Avhuggning snarare än avrundning — samma som prototypens
    /// <c>astype(uint8)</c>, så facitbilden och portens skiljer sig inte med en nivå.
    /// </summary>
    public static SKBitmap ToBitmap(this ColorGrid grid)
    {
        var bitmap = new SKBitmap(new SKImageInfo(grid.Width, grid.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var pixels = new byte[grid.Width * grid.Height * 4];
        for (int p = 0, i = 0; p < grid.Width * grid.Height; p++, i += 3)
        {
            pixels[p * 4] = (byte)(Math.Clamp(grid.Values[i], 0f, 1f) * 255f);
            pixels[p * 4 + 1] = (byte)(Math.Clamp(grid.Values[i + 1], 0f, 1f) * 255f);
            pixels[p * 4 + 2] = (byte)(Math.Clamp(grid.Values[i + 2], 0f, 1f) * 255f);
            pixels[p * 4 + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    public static ColorGrid ToColorGrid(this SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Rgba8888)
            throw new ArgumentException($"väntade Rgba8888, fick {bitmap.ColorType} — gå genom DecodeBitmap");
        var grid = new ColorGrid(bitmap.Width, bitmap.Height);
        var pixels = bitmap.GetPixelSpan();
        for (int p = 0, i = 0; p < grid.Width * grid.Height; p++, i += 3)
        {
            grid.Values[i] = pixels[p * 4] / 255f;
            grid.Values[i + 1] = pixels[p * 4 + 1] / 255f;
            grid.Values[i + 2] = pixels[p * 4 + 2] / 255f;
        }
        return grid;
    }

    public static byte[] ToPng(this ColorGrid grid)
    {
        using var bitmap = grid.ToBitmap();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Avkodar till standardiserat RGBA, oavsett vad filen själv bär.</summary>
    public static SKBitmap DecodeBitmap(byte[] bytes)
    {
        var bounds = SKBitmap.DecodeBounds(bytes);
        if (bounds.IsEmpty)
            throw new InvalidDataException("bilddatat gick inte att avkoda");
        return SKBitmap.Decode(bytes,
                new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            ?? throw new InvalidDataException("bilddatat gick inte att avkoda");
    }

    public static ColorGrid Decode(byte[] bytes)
    {
        using var bitmap = DecodeBitmap(bytes);
        return bitmap.ToColorGrid();
    }

    /// <summary>Lanczos3, som PIL — Skia saknar den kärnan och facit är nedskalat med den.</summary>
    public static ColorGrid Resized(this ColorGrid grid, int width, int height)
    {
        if (grid.Width == width && grid.Height == height)
            return grid;
        var result = new ColorGrid(width, height);
        Lanczos.Resize(grid.Values, grid.Width, grid.Height, 3, width, height)
            .CopyTo(result.Values, 0);
        return result;
    }
}
