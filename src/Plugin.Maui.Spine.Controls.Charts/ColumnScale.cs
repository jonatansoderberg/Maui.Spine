namespace Plugin.Maui.Spine.Controls;

/// <summary>The height <see cref="ColumnChart"/> gives its columns, free of MAUI so it can be tested.</summary>
internal static class ColumnScale
{
    /// <summary>The value at the top of the columns: <paramref name="maximum"/> when set, else the highest value or the reference line, whichever is higher.</summary>
    public static double Top(IEnumerable<double> values, double? reference, double? maximum)
    {
        if (maximum is > 0)
            return maximum.Value;
        var top = values.DefaultIfEmpty(0).Max();
        return reference is { } r && r > top ? r : top;
    }

    /// <summary>A column's height out of <paramref name="height"/>: its share of <paramref name="top"/>, at least <paramref name="least"/>, at most all of it; 0 for no value.</summary>
    public static float Height(double value, double top, float height, float least) =>
        top <= 0 || value <= 0 ? 0 : Math.Clamp(height * (float)(value / top), least, height);
}
