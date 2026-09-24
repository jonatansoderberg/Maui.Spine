namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Where a column renders within one <see cref="DataGridLayout"/>. <see cref="Row"/> and
/// <see cref="Column"/> place the value; the header in a static header block mirrors the same
/// position. A column without a placement in the active layout is not rendered there.
/// </summary>
public class DataGridCellPlacement : BindableObject
{
    public string ColumnKey { get; set; } = string.Empty;

    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;

    public bool HeaderIsVisible { get; set; } = true;
    public TextAlignment? HeaderHorizontalTextAlignment { get; set; }

    // Per-placement overrides; null inherits from the column.
    public TextAlignment? HorizontalTextAlignment { get; set; }
    public TextAlignment? VerticalTextAlignment { get; set; }
    public FontAttributes? FontAttributes { get; set; }
    public int? MaxLines { get; set; }
    public double FontSize { get; set; } = -1;

    // The XAML source generator has no converter for a nullable Thickness; without this
    // Padding="0" fails the build (MAUIX2002).
    [System.ComponentModel.TypeConverter(typeof(Microsoft.Maui.Converters.ThicknessTypeConverter))]
    public Thickness? Padding { get; set; }
}
