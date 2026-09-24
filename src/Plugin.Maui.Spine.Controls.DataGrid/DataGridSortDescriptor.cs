namespace Plugin.Maui.Spine.Controls;

/// <summary>Passed to <see cref="DataGrid.SortChangedCommand"/> when the user changes the sort order.</summary>
public sealed record DataGridSortDescriptor(string? ColumnKey, DataGridSortDirection Direction);
