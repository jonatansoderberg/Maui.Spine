namespace Orientera.Backend.Arena;

/// <summary>
/// Lanczos3-omsampling, samma kärna och viktning som PIL:s LANCZOS.
/// </summary>
/// <remarks>
/// Skia saknar Lanczos, och facitbilderna — och prototypens vimpel — är nedskalade med
/// PIL:s. En egen separabel implementation är sextio rader och gör porten mätbar mot facit;
/// en annan kärna hade kostat just den marginal kantkorrelationen lever på. Värdena
/// kvantiseras till 8 bitar före filtreringen, för det är vad PIL filtrerar.
/// </remarks>
public static class Lanczos
{
    public static float[] Resize(
        float[] source, int sourceWidth, int sourceHeight, int channels, int width, int height)
    {
        var quantized = new float[source.Length];
        for (var i = 0; i < source.Length; i++)
            quantized[i] = (byte)(Math.Clamp(source[i], 0f, 1f) * 255f) / 255f;

        var horizontal = Pass(quantized, sourceWidth, sourceHeight, channels, width, alongRows: true);
        return Pass(horizontal, width, sourceHeight, channels, height, alongRows: false);
    }

    private static float[] Pass(
        float[] source, int sourceWidth, int sourceHeight, int channels, int targetSize, bool alongRows)
    {
        var weights = Precompute(alongRows ? sourceWidth : sourceHeight, targetSize);
        var width = alongRows ? targetSize : sourceWidth;
        var height = alongRows ? sourceHeight : targetSize;
        var result = new float[width * height * channels];

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var (start, taps) = weights[alongRows ? x : y];
                var target = (y * width + x) * channels;
                for (var c = 0; c < channels; c++)
                {
                    var sum = 0.0;
                    for (var t = 0; t < taps.Length; t++)
                    {
                        var sourceIndex = alongRows
                            ? (y * sourceWidth + start + t) * channels + c
                            : ((start + t) * sourceWidth + x) * channels + c;
                        sum += source[sourceIndex] * taps[t];
                    }
                    result[target + c] = (float)Math.Clamp(sum, 0, 1);
                }
            }
        });
        return result;
    }

    /// <summary>Filterfönster och normerade vikter per målkoordinat, med PIL:s exakta fönstring.</summary>
    private static (int Start, double[] Weights)[] Precompute(int sourceSize, int targetSize)
    {
        var scale = (double)sourceSize / targetSize;
        var filterScale = Math.Max(scale, 1.0);
        var support = 3.0 * filterScale;

        var result = new (int, double[])[targetSize];
        for (var i = 0; i < targetSize; i++)
        {
            var center = (i + 0.5) * scale;
            var start = Math.Max(0, (int)(center - support + 0.5));
            var end = Math.Min(sourceSize, (int)(center + support + 0.5));
            var taps = new double[end - start];
            var total = 0.0;
            for (var t = 0; t < taps.Length; t++)
            {
                taps[t] = Kernel((t + start - center + 0.5) / filterScale);
                total += taps[t];
            }
            for (var t = 0; t < taps.Length; t++)
                taps[t] /= total;
            result[i] = (start, taps);
        }
        return result;
    }

    private static double Kernel(double x)
    {
        if (x == 0)
            return 1;
        if (Math.Abs(x) >= 3)
            return 0;
        var a = Math.PI * x;
        var b = a / 3;
        return Math.Sin(a) / a * (Math.Sin(b) / b);
    }
}
