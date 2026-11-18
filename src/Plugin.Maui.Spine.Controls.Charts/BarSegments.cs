namespace Plugin.Maui.Spine.Controls;

/// <summary>How long <see cref="BarChart"/> draws its segments, free of MAUI so it can be tested.</summary>
internal static class BarSegments
{
    /// <summary>
    /// The segments' lengths along a bar <paramref name="length"/> long, in proportion to their total. A value above 0
    /// gets at least <paramref name="least"/>, taken from the longer segments in proportion, so a 0.4 % share still
    /// shows; a value of 0 or less gets none. The lengths add up to <paramref name="length"/> unless every value is 0.
    /// </summary>
    public static float[] Lengths(IReadOnlyList<double> values, float length, float least)
    {
        var lengths = new float[values.Count];
        var total = values.Where(v => v > 0).Sum();
        if (total <= 0 || length <= 0)
            return lengths;

        var shown = values.Count(v => v > 0);
        least = Math.Min(least, length / shown);
        for (var i = 0; i < values.Count; i++)
            lengths[i] = values[i] > 0 ? (float)(values[i] / total) * length : 0;

        var shortfall = lengths.Where(l => l > 0 && l < least).Sum(l => least - l);
        if (shortfall <= 0)
            return lengths;
        var spare = lengths.Where(l => l >= least).Sum(l => l - least);
        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] <= 0)
                continue;
            lengths[i] = lengths[i] < least
                ? least
                : lengths[i] - (spare > 0 ? (lengths[i] - least) / spare * shortfall : 0);
        }
        return lengths;
    }
}
