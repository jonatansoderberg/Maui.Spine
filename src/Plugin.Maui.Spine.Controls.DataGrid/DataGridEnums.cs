namespace Plugin.Maui.Spine.Controls;

/// <summary>How a cell's value is rendered by the generated row template.</summary>
public enum DataGridColumnType
{
    Text,
    Number,
    Date,
    Price,
    Image,
    Glyph,
    Checkbox,
    Template,
}

/// <summary>How, and whether, headers are rendered for a layout.</summary>
public enum DataGridHeaderMode
{
    None,

    /// <summary>One traditional header row above the list (flat table layouts).</summary>
    TopHeaderRow,

    /// <summary>Header labels are repeated, muted, inside each item row.</summary>
    InsideItem,

    /// <summary>
    /// Headers are rendered once, above the list, in the same multi-row arrangement as the item rows.
    /// </summary>
    StaticLayoutHeader,
}

public enum DataGridSortDirection
{
    None,
    Ascending,
    Descending,
}

/// <summary>How rows whose <see cref="DataGrid.GroupByPath"/> value is null or empty render while grouping.</summary>
public enum DataGridUngroupedMode
{
    /// <summary>Ungrouped rows render as plain rows before the groups.</summary>
    Root,

    /// <summary>Ungrouped rows get a group of their own, titled <see cref="DataGrid.UngroupedGroupText"/>, placed first.</summary>
    Group,
}
