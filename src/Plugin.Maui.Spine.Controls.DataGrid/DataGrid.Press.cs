using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Taps and long presses on rows, through one recognizer per row.
/// </summary>
/// <remarks>
/// <para>
/// A row carries a single <see cref="PointerGestureRecognizer"/>, made with the row template and reused
/// as the list recycles the row, and the grid has one timer. The press is timed here: released before
/// <see cref="DataGridStyleOptions.LongPressDuration"/> it is a tap, held past it a long press. Which
/// cell was hit is found by matching the press point against the row's cells, so a link cell, a
/// checkbox cell and the row itself share the recognizer, and a long-pressed text cell copies the text
/// its label already shows: no reflection, no extra binding and no extra view.
/// </para>
/// <para>
/// One recognizer rather than a tap recognizer per row plus a pointer recognizer per row and per link
/// cell: on Android every recognizer adds Java peers per realized row, and the separate recognizers
/// made copying a cost the rows paid whether or not anyone copied.
/// </para>
/// </remarks>
public partial class DataGrid
{
    /// <summary>
    /// Called after a long-pressed cell's text reached the clipboard, with the column and the text,
    /// instead of the grid's own "Copied" bubble. App-wide; set it once at start-up.
    /// </summary>
    public static Func<DataGridColumn, string, Task>? CellCopied { get; set; }

    /// <summary>How far the finger may drift before the press counts as the start of a scroll or a swipe.</summary>
    private const double PressSlop = 10;

    private IDispatcherTimer? _pressTimer;
    private RowPress? _press;

    internal enum RowCellKind
    {
        Text,
        Link,
        Checkbox,
    }

    /// <summary>A cell the row's press handler knows about: text to copy, a link to run or a checkbox to toggle.</summary>
    internal sealed record RowCell(View Area, DataGridColumn Column, RowCellKind Kind, Label? CopySource, CheckBox? CheckBox);

    /// <summary>
    /// A pending press. <see cref="Item"/> is what the row showed when the finger went down: a reload
    /// that hands the row another item in place must not act on the new one.
    /// </summary>
    private sealed class RowPress(Grid row, Point point, RowCell? cell, object? item)
    {
        public Grid Row { get; } = row;
        public Point Point { get; } = point;
        public RowCell? Cell { get; } = cell;
        public object? Item { get; } = item;
        public bool LongPressed { get; set; }
    }

    private void AttachRowPress(Grid row, RowCell[] cells)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, e) =>
        {
            if (e.GetPosition(row) is { } point)
                BeginRowPress(row, point, FindCell(cells, point));
        };
        pointer.PointerMoved += (_, e) =>
        {
            if (_press is { } press
                && ReferenceEquals(press.Row, row)
                && e.GetPosition(row) is { } point
                && point.Distance(press.Point) > PressSlop)
                CancelRowPress();
        };
        pointer.PointerReleased += (_, _) => EndRowPress(row);
        row.GestureRecognizers.Add(pointer);
    }

    private static RowCell? FindCell(RowCell[] cells, Point point)
    {
        // A cell is a direct child of its row, so its Frame is in the row's coordinates.
        foreach (var cell in cells)
        {
            if (cell.Area.Frame.Contains(point))
                return cell;
        }
        return null;
    }

    private void BeginRowPress(Grid row, Point point, RowCell? cell)
    {
        CancelRowPress();
        _press = new RowPress(row, point, cell, row.BindingContext);

        _pressTimer ??= CreatePressTimer();
        _pressTimer.Interval = ResolveOptions().LongPressDuration;
        _pressTimer.Start();
    }

    private IDispatcherTimer CreatePressTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => OnRowLongPress();
        return timer;
    }

    private void OnRowLongPress()
    {
        if (_press is not { } press)
            return;

        // From here the press is a long one, whatever it finds: the release must not also tap.
        press.LongPressed = true;

        if (press.Cell is not { CopySource: { } label } cell
            || label.Handler is null
            || !Equals(label.BindingContext, press.Item)
            || string.IsNullOrWhiteSpace(label.Text))
            return;

        _ = CopyCellTextAsync(cell.Column, label.Text);
    }

    private void EndRowPress(Grid row)
    {
        if (_press is not { } press || !ReferenceEquals(press.Row, row))
            return;

        _pressTimer?.Stop();
        _press = null;

        if (!press.LongPressed && Equals(row.BindingContext, press.Item))
            OnRowTapped(press);
    }

    /// <summary>Drops a pending press; cheap when none is pending, since every scroll step calls it.</summary>
    private void CancelRowPress()
    {
        if (_press is null)
            return;

        _pressTimer?.Stop();
        _press = null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimmingMessage)]
    private void OnRowTapped(RowPress press)
    {
        var item = press.Item;
        switch (press.Cell)
        {
            case { Kind: RowCellKind.Link, Column: var column } when column.CellCommand is { } command:
            {
                var parameter = column.GetCommandParameter(item);
                if (command.CanExecute(parameter))
                    command.Execute(parameter);
                return;
            }

            case { Kind: RowCellKind.Checkbox, Column: var column, CheckBox: { } checkBox }:
            {
                if (!checkBox.IsEnabled)
                    return;

                // With a command the view model owns the toggle; without one the cell toggles the bound value.
                if (column.CellCommand is { } command)
                {
                    var parameter = column.GetCommandParameter(item);
                    if (command.CanExecute(parameter))
                        command.Execute(parameter);
                }
                else
                {
                    checkBox.IsChecked = !checkBox.IsChecked;
                }
                return;
            }
        }

        if (RowTappedCommand is { } rowCommand && rowCommand.CanExecute(item))
            rowCommand.Execute(item);
    }

    private async Task CopyCellTextAsync(DataGridColumn column, string text)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(text);

            if (CellCopied is { } confirm)
                await confirm(column, text);
            else if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                // Android 13 and later confirm a copy with their own clipboard overlay.
                ShowBubble(SpineStrings.Current["Spine.DataGrid.Copied"], anchor: null, ResolveOptions());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataGrid] copying a cell failed: {ex}");
        }
    }
}
