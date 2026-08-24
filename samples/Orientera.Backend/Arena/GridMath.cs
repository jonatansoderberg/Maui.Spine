namespace Orientera.Backend.Arena;

/// <summary>Fältoperationerna renderaren räknar med — numpys motsvarigheter, inte fler.</summary>
public static class GridMath
{
    /// <summary>Percentil med linjär interpolation, som numpys standard.</summary>
    public static float Percentile(ReadOnlySpan<float> values, double q)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var rank = q / 100.0 * (sorted.Length - 1);
        var low = (int)rank;
        var fraction = rank - low;
        return low + 1 < sorted.Length
            ? (float)(sorted[low] * (1 - fraction) + sorted[low + 1] * fraction)
            : sorted[low];
    }

    public static float Mean(ReadOnlySpan<float> values)
    {
        double sum = 0;
        foreach (var value in values)
            sum += value;
        return (float)(sum / values.Length);
    }

    /// <summary>
    /// Gaussisk utjämning approximerad med tre boxfiltreringar per axel.
    /// </summary>
    /// <remarks>
    /// Tre pass av bredd w ger varians (w²−1)/4, vilket löser ut radien nedan. Fönstret
    /// summeras i dubbel precision och kanterna kläms — samma semantik som prototypens
    /// kumulativa summa över kantutfyllda arrayer.
    /// </remarks>
    public static ScalarGrid Smooth(ScalarGrid grid, double sigmaPixels)
    {
        if (sigmaPixels < 0.5)
            return grid;
        var width = Math.Sqrt(4 * sigmaPixels * sigmaPixels + 1);
        // Pythons round är bankersavrundning; samma här så radien blir densamma som facits.
        var radius = Math.Max(1, (int)Math.Round((width - 1) / 2, MidpointRounding.ToEven));

        var current = grid;
        for (var pass = 0; pass < 3; pass++)
            current = BoxAlongX(BoxAlongY(current, radius), radius);
        return current;
    }

    private static ScalarGrid BoxAlongX(ScalarGrid grid, int radius)
    {
        var result = new ScalarGrid(grid.Width, grid.Height);
        var window = 2 * radius + 1;
        Parallel.For(0, grid.Height, y =>
        {
            var row = grid.Values.AsSpan(y * grid.Width, grid.Width);
            var target = result.Values.AsSpan(y * grid.Width, grid.Width);
            double sum = 0;
            for (var i = -radius; i <= radius; i++)
                sum += row[Math.Clamp(i, 0, grid.Width - 1)];
            for (var x = 0; x < grid.Width; x++)
            {
                target[x] = (float)(sum / window);
                sum += row[Math.Clamp(x + radius + 1, 0, grid.Width - 1)]
                     - row[Math.Clamp(x - radius, 0, grid.Width - 1)];
            }
        });
        return result;
    }

    private static ScalarGrid BoxAlongY(ScalarGrid grid, int radius)
    {
        var result = new ScalarGrid(grid.Width, grid.Height);
        var window = 2 * radius + 1;
        // Kolumnvis i block om rader, så minnet läses i radordning trots kolumnfiltret.
        Parallel.For(0, grid.Width, x =>
        {
            double sum = 0;
            for (var i = -radius; i <= radius; i++)
                sum += grid.Values[Math.Clamp(i, 0, grid.Height - 1) * grid.Width + x];
            for (var y = 0; y < grid.Height; y++)
            {
                result.Values[y * grid.Width + x] = (float)(sum / window);
                sum += grid.Values[Math.Clamp(y + radius + 1, 0, grid.Height - 1) * grid.Width + x]
                     - grid.Values[Math.Clamp(y - radius, 0, grid.Height - 1) * grid.Width + x];
            }
        });
        return result;
    }
}
