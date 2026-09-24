using System.Globalization;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Formats Date, Price and Number cells. One instance per column, created when the template is
/// built and shared by every recycled row.
/// </summary>
internal sealed class DataGridFormatConverter(DataGridColumn column) : IValueConverter
{
    private readonly CultureInfo? _culture = column.FormatCulture is { } name ? CultureInfo.GetCultureInfo(name) : null;

    internal static bool Applies(DataGridColumn column) =>
        column.Type is DataGridColumnType.Date or DataGridColumnType.Price
        || (column.Type == DataGridColumnType.Number && column.Format is not null);

    public string? Format(object? value)
    {
        if (value is null)
            return string.Empty;

        var format = column.Format ?? column.Type switch
        {
            DataGridColumnType.Date => "d",
            DataGridColumnType.Price => "N2",
            _ => null,
        };

        return format is not null && value is IFormattable formattable
            ? formattable.ToString(format, _culture ?? CultureInfo.CurrentCulture)
            : value.ToString();
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Format(value);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
