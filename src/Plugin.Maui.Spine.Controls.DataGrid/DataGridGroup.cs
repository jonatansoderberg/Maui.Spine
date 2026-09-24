using System.ComponentModel;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// One group of rows while <see cref="DataGrid.GroupByPath"/> is set: the binding context of a
/// group header and the parameter of <see cref="DataGrid.GroupTappedCommand"/>. The grid reuses
/// an instance per key across re-renders, so <see cref="IsExpanded"/> survives sorting, reloads
/// and layout switches.
/// </summary>
public sealed class DataGridGroup : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private string _displayText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The group key read through <see cref="DataGrid.GroupByPath"/>; null for the ungrouped group.</summary>
    public object? Key { get; internal set; }

    /// <summary>Header text: <see cref="DataGrid.GroupDisplayPath"/> on the first row, or the key.</summary>
    public string DisplayText
    {
        get => _displayText;
        internal set
        {
            if (_displayText == value)
                return;
            _displayText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }

    /// <summary>The group's rows in display (sorted) order.</summary>
    public IReadOnlyList<object> Items { get; private set; } = [];

    public int Count => Items.Count;

    /// <summary>The first row, for group-level data in a command without indexing <see cref="Items"/>.</summary>
    public object? FirstItem => Items.Count > 0 ? Items[0] : null;

    /// <summary>True for the group that holds ungrouped rows (<see cref="DataGridUngroupedMode.Group"/>).</summary>
    public bool IsUngrouped { get; internal set; }

    /// <summary>
    /// Whether the rows are shown. Two-way bindable from a custom header template; the grid inserts
    /// or removes the rows in place, so the scroll position is kept.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    internal void Update(string displayText, IReadOnlyList<object> items)
    {
        DisplayText = displayText;
        Items = items;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FirstItem)));
    }
}
