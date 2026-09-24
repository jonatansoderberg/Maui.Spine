using System.Collections.ObjectModel;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// One named arrangement of columns, e.g. "Wide" or "Narrow". The active layout is picked by
/// <see cref="DataGrid.LayoutMode"/>, typically from a VisualStateManager setter. The row and
/// column definitions are shared by the static header and every item row, so they align.
/// </summary>
[ContentProperty(nameof(Placements))]
public class DataGridLayout : BindableObject
{
    public string Name { get; set; } = string.Empty;

    public DataGridHeaderMode HeaderMode { get; set; } = DataGridHeaderMode.StaticLayoutHeader;

    /// <summary>
    /// Fixed item height. Rows never resize while scrolling; unset, the height follows the
    /// Material list scale from <see cref="DataGridStyleOptions.ItemHeight"/>.
    /// </summary>
    public double ItemHeight { get; set; } = -1;

    [System.ComponentModel.TypeConverter(typeof(ColumnDefinitionCollectionTypeConverter))]
    public ColumnDefinitionCollection ColumnDefinitions { get; set; } = [];

    [System.ComponentModel.TypeConverter(typeof(RowDefinitionCollectionTypeConverter))]
    public RowDefinitionCollection RowDefinitions { get; set; } = [];

    public ObservableCollection<DataGridCellPlacement> Placements { get; } = [];

    /// <summary>
    /// A definition cannot be shared between the header grid and the recycled row grids, so each
    /// gets a copy. Auto columns become the measured content-fit width, the same for header and rows.
    /// </summary>
    internal ColumnDefinitionCollection CloneColumnDefinitions(IReadOnlyDictionary<int, double>? autoFitWidths)
    {
        var clone = new ColumnDefinitionCollection();
        for (var i = 0; i < ColumnDefinitions.Count; i++)
        {
            var width = ColumnDefinitions[i].Width;
            if (width.IsAuto && autoFitWidths is not null && autoFitWidths.TryGetValue(i, out var measured) && measured > 0)
                width = new GridLength(measured);
            clone.Add(new ColumnDefinition { Width = width });
        }
        if (clone.Count == 0)
            clone.Add(new ColumnDefinition { Width = GridLength.Star });
        return clone;
    }

    internal RowDefinitionCollection CloneRowDefinitions()
    {
        var clone = new RowDefinitionCollection();
        foreach (var definition in RowDefinitions)
            clone.Add(new RowDefinition { Height = definition.Height });
        if (clone.Count == 0)
            clone.Add(new RowDefinition { Height = GridLength.Star });
        return clone;
    }
}
