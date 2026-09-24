using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// WHAT a piece of row data is: its binding, type, formatting and sort behaviour. WHERE it renders
/// is decided per layout by a <see cref="DataGridCellPlacement"/>.
/// </summary>
public class DataGridColumn : BindableObject
{
    public static readonly BindableProperty HeaderProperty =
        BindableProperty.Create(nameof(Header), typeof(string), typeof(DataGridColumn), string.Empty);

    public static readonly BindableProperty CellCommandProperty =
        BindableProperty.Create(nameof(CellCommand), typeof(ICommand), typeof(DataGridColumn));

    /// <summary>Unique key referenced by <see cref="DataGridCellPlacement.ColumnKey"/> and <see cref="DataGrid.SortColumnKey"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Header text. Bindable, so a <c>{String}</c> or other binding can supply it.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Property path on the row item. Dotted paths work; flat ones are cheaper.</summary>
    public string BindingPath { get; set; } = string.Empty;

    public DataGridColumnType Type { get; set; } = DataGridColumnType.Text;

    /// <summary>
    /// .NET format string for Date, Price and Number cells, e.g. <c>"d"</c> or <c>"N2"</c>. Defaults
    /// to <c>"d"</c> for dates and <c>"N2"</c> for prices.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Culture name for <see cref="Format"/>; the current culture when unset.</summary>
    public string? FormatCulture { get; set; }

    public bool IsSortable { get; set; }

    /// <summary>
    /// Property path compared by the local sort instead of <see cref="BindingPath"/>. Use it when the
    /// cell shows a formatted string but the order should follow the raw value.
    /// </summary>
    public string? SortMemberPath { get; set; }

    /// <summary>Width in the flat layout the grid generates when it has no <see cref="DataGrid.Layouts"/>.</summary>
    public GridLength Width { get; set; } = GridLength.Star;

    public FontAttributes FontAttributes { get; set; } = FontAttributes.None;

    /// <summary>Font family for this column's cells; required for <see cref="DataGridColumnType.Glyph"/> cells.</summary>
    public string? FontFamily { get; set; }

    public double FontSize { get; set; } = -1;

    public Color? TextColor { get; set; }

    public LineBreakMode LineBreakMode { get; set; } = LineBreakMode.TailTruncation;

    /// <summary>Maximum lines of text. Rows have a fixed height; text never grows a row.</summary>
    public int MaxLines { get; set; } = 1;

    /// <summary>
    /// The value must always be readable in full, e.g. article numbers. Only works in a
    /// <c>Width="Auto"</c> column of its own: every loaded row is measured (not just the first 200),
    /// the width gets twice the safety margin, and rows appended by load more are measured too; when
    /// one needs more room the grid re-renders at the cost of the scroll position.
    /// </summary>
    public bool NeverTruncate { get; set; }

    public TextAlignment HorizontalTextAlignment { get; set; } = TextAlignment.Start;

    public TextAlignment VerticalTextAlignment { get; set; } = TextAlignment.Center;

    /// <summary>
    /// Run when the cell is tapped. Text cells render as links, the whole cell is the target and it
    /// takes precedence over <see cref="DataGrid.RowTappedCommand"/>. A checkbox cell with a command
    /// leaves the toggle to the command.
    /// </summary>
    public ICommand? CellCommand
    {
        get => (ICommand?)GetValue(CellCommandProperty);
        set => SetValue(CellCommandProperty, value);
    }

    /// <summary>A path on the row item used as the <see cref="CellCommand"/> parameter. Defaults to the row item.</summary>
    public string? CellCommandParameterPath { get; set; }

    /// <summary>A bool path on the row item that enables an interactive (checkbox) cell.</summary>
    public string? IsEnabledPath { get; set; }

    /// <summary>A fully custom cell. Its binding context is the row item.</summary>
    public DataTemplate? CellTemplate { get; set; }

    private PropertyPathGetter? _getter;
    private PropertyPathGetter? _sortGetter;
    private PropertyPathGetter? _parameterGetter;

    [RequiresUnreferencedCode(DataGrid.TrimmingMessage)]
    internal object? GetItemValue(object? item)
    {
        if (_getter is null || _getter.Path != BindingPath)
            _getter = new PropertyPathGetter(BindingPath);
        return _getter.GetValue(item);
    }

    [RequiresUnreferencedCode(DataGrid.TrimmingMessage)]
    internal object? GetSortValue(object? item)
    {
        if (SortMemberPath is not { } path)
            return GetItemValue(item);
        if (_sortGetter is null || _sortGetter.Path != path)
            _sortGetter = new PropertyPathGetter(path);
        return _sortGetter.GetValue(item);
    }

    [RequiresUnreferencedCode(DataGrid.TrimmingMessage)]
    internal object? GetCommandParameter(object? item)
    {
        if (CellCommandParameterPath is not { } path)
            return item;
        if (_parameterGetter is null || _parameterGetter.Path != path)
            _parameterGetter = new PropertyPathGetter(path);
        return _parameterGetter.GetValue(item);
    }
}
