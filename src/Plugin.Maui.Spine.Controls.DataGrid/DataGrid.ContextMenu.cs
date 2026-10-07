using System.Collections.Specialized;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The rows' context menu: <see cref="RowContextMenu"/> behind a "Copy <i>column</i>" row for the
/// cell the menu was opened on.
/// </summary>
/// <remarks>
/// Every row carries the same <see cref="MenuItems"/> through <c>ContextMenu.Items</c>, the platform's
/// own context menu, with the row's item as the fallback parameter. The menu is built when it opens, so
/// the copy row only has to be right by then: a touch says which cell it is on when it goes down, a
/// mouse while it hovers, ahead of the right click.
/// </remarks>
public partial class DataGrid
{
    private MenuItems? _rowMenu;
    private MenuAction? _copyRow;
    private (DataGridColumn Column, Label Label, object? Item)? _copyTarget;

    private void OnRowContextMenuChanged(MenuItems? oldMenu, MenuItems? newMenu)
    {
        if (oldMenu is not null)
            oldMenu.CollectionChanged -= OnRowContextMenuCollectionChanged;
        if (newMenu is not null)
            newMenu.CollectionChanged += OnRowContextMenuCollectionChanged;

        ComposeRowMenu();

        // Rows built with a menu attach it, and their recognizer listens to the right button too.
        if ((oldMenu is null) != (newMenu is null))
            QueueRebuild();
    }

    private void OnRowContextMenuCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ComposeRowMenu();

    /// <summary>The copy row in a section of its own, then the app's elements: the same instances, so their changes show.</summary>
    private void ComposeRowMenu()
    {
        if (RowContextMenu is not { } menu)
        {
            _rowMenu = null;
            return;
        }

        _copyRow ??= new MenuAction(string.Empty, "copy.svg", new Command(CopyFromMenu)) { IsVisible = false };
        _rowMenu ??= [];
        _rowMenu.Clear();
        _rowMenu.Add(new MenuSection { _copyRow });
        foreach (var element in menu)
            _rowMenu.Add(element);
    }

    private void AttachRowMenu(Grid row)
    {
        if (_rowMenu is null)
            return;

        ContextMenu.SetItems(row, _rowMenu);
        row.SetBinding(ContextMenu.CommandParameterProperty, Binding.SelfPath);
    }

    /// <summary>
    /// Points the copy row at the cell a press landed on, ahead of the menu it may open: a text cell
    /// showing text gets "Copy <i>column</i>", anything else hides the row.
    /// </summary>
    private void TargetCopyRow(Grid row, RowCell? cell)
    {
        if (_copyRow is null)
            return;

        if (cell is { CopySource: { } label, Column: var column } && !string.IsNullOrWhiteSpace(label.Text))
        {
            // Hovering calls this on every move; only a new cell or item changes the row.
            if (_copyTarget is { } current && ReferenceEquals(current.Label, label) && Equals(current.Item, row.BindingContext))
                return;

            _copyTarget = (column, label, row.BindingContext);
            _copyRow.Title = SpineStrings.Current.Get("Spine.DataGrid.CopyColumn", column.Header);
            _copyRow.IsVisible = true;
        }
        else
        {
            _copyTarget = null;
            _copyRow.IsVisible = false;
        }
    }

    private void CopyFromMenu()
    {
        // A reload may have handed the row another item while the menu was open.
        if (_copyTarget is not { } target
            || target.Label.Handler is null
            || !Equals(target.Label.BindingContext, target.Item)
            || string.IsNullOrWhiteSpace(target.Label.Text))
            return;

        _ = CopyCellTextAsync(target.Column, target.Label.Text);
    }
}
