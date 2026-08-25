namespace Orientera.Backend.Arena;

/// <summary>En fristående RGBA-bild i flyttal: färg och täckning var för sig, rak alfa.</summary>
internal sealed class RgbaLayer(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public ColorGrid Color { get; } = new(width, height);
    public ScalarGrid Alpha { get; } = new(width, height);

    /// <summary>Alfa-över mot en täckande bakgrund.</summary>
    public void CompositeOver(ColorGrid image, int offsetX, int offsetY)
    {
        for (var y = 0; y < Height; y++)
        {
            var targetY = y + offsetY;
            if (targetY < 0 || targetY >= image.Height)
                continue;
            for (var x = 0; x < Width; x++)
            {
                var targetX = x + offsetX;
                if (targetX < 0 || targetX >= image.Width)
                    continue;
                var alpha = Alpha.Values[y * Width + x];
                if (alpha <= 0)
                    continue;
                var from = (y * Width + x) * 3;
                var to = image.IndexOf(targetX, targetY);
                for (var c = 0; c < 3; c++)
                    image.Values[to + c] += (Color.Values[from + c] - image.Values[to + c]) * alpha;
            }
        }
    }

    public void Blur(double sigma)
    {
        var blurredAlpha = GridMath.Smooth(Alpha, sigma);
        Array.Copy(blurredAlpha.Values, Alpha.Values, Alpha.Values.Length);
    }

    /// <summary>Lanczos-omsampling med rak alfa, kanalerna var för sig — som PIL skalar RGBA.</summary>
    public RgbaLayer Resize(int width, int height)
    {
        var interleaved = new float[Width * Height * 4];
        for (int p = 0, i = 0; p < Width * Height; p++, i += 3)
        {
            interleaved[p * 4] = Color.Values[i];
            interleaved[p * 4 + 1] = Color.Values[i + 1];
            interleaved[p * 4 + 2] = Color.Values[i + 2];
            interleaved[p * 4 + 3] = Alpha.Values[p];
        }

        var resized = Lanczos.Resize(interleaved, Width, Height, 4, width, height);
        var result = new RgbaLayer(width, height);
        for (int p = 0, i = 0; p < width * height; p++, i += 3)
        {
            result.Color.Values[i] = resized[p * 4];
            result.Color.Values[i + 1] = resized[p * 4 + 1];
            result.Color.Values[i + 2] = resized[p * 4 + 2];
            result.Alpha.Values[p] = resized[p * 4 + 3];
        }
        return result;
    }
}
