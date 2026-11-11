using Microsoft.Maui.Layouts;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>
/// Cards in columns that fill one at a time: as many columns as fit at <see cref="MinColumnWidth"/> or wider (at most
/// <see cref="MaxColumns"/>), each item in the column that is shortest at that moment. Items of different heights then
/// leave no holes, as a row-by-row FlexLayout does. The order follows the reading direction. Works with
/// <c>BindableLayout.ItemsSource</c> and <c>Reorder.Mode</c>.
/// </summary>
public sealed class MasonryLayout : Layout, ILayoutManager
{
    // A window exactly as wide as a number of columns needs can round one away (and AppKit's frames round as well).
    const double WidthSlack = 4;

    public static readonly BindableProperty MinColumnWidthProperty = BindableProperty.Create(
        nameof(MinColumnWidth), typeof(double), typeof(MasonryLayout), 320.0,
        propertyChanged: (b, _, _) => ((MasonryLayout)b).InvalidateMeasure());

    public static readonly BindableProperty MaxColumnsProperty = BindableProperty.Create(
        nameof(MaxColumns), typeof(int), typeof(MasonryLayout), 3,
        propertyChanged: (b, _, _) => ((MasonryLayout)b).InvalidateMeasure());

    public static readonly BindableProperty SpacingProperty = BindableProperty.Create(
        nameof(Spacing), typeof(double), typeof(MasonryLayout), 16.0,
        propertyChanged: (b, _, _) => ((MasonryLayout)b).InvalidateMeasure());

    /// <summary>The narrowest a column may be. 320 by default.</summary>
    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }

    /// <summary>The most columns, however wide the layout. 3 by default.</summary>
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    /// <summary>The gap between columns and between the items in a column. 16 by default.</summary>
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    /// <summary>
    /// How wide the layout must be for <paramref name="columns"/> columns at <see cref="MinColumnWidth"/>, with its padding
    /// and a little slack: for window sizes that fit the content (<c>ViewModelBase.SuggestWindowSizes</c>).
    /// </summary>
    public double WidthFor(int columns) =>
        columns * MinColumnWidth + (columns - 1) * Spacing + Padding.HorizontalThickness + WidthSlack;

    readonly List<Rect> _frames = [];
    double _measuredWidth;
    int _measuredColumns;
    // The column each item was measured into, -1 for a collapsed one.
    readonly List<int> _columnOf = [];

    protected override ILayoutManager CreateLayoutManager() => this;

    Size ILayoutManager.Measure(double widthConstraint, double heightConstraint)
    {
        var width = _measuredWidth = double.IsInfinity(widthConstraint) ? MinColumnWidth : widthConstraint;
        var columns = _measuredColumns = ColumnsFor(width);
        var columnWidth = ColumnWidth(width, columns);
        var heights = new double[columns];

        _frames.Clear();
        _frames.AddRange(Enumerable.Repeat(Rect.Zero, Count));
        _columnOf.Clear();
        _columnOf.AddRange(Enumerable.Repeat(-1, Count));
        for (var index = 0; index < Count; index++)
        {
            var child = this[index];
            if (child.Visibility == Visibility.Collapsed)
                continue;
            var size = child.Measure(columnWidth, double.PositiveInfinity);
            var column = Array.IndexOf(heights, heights.Min());
            var top = heights[column] > 0 ? heights[column] + Spacing : 0;
            _frames[index] = new Rect(Padding.Left + column * (columnWidth + Spacing), Padding.Top + top, columnWidth, size.Height);
            _columnOf[index] = column;
            heights[column] = top + size.Height;
        }
        return new Size(width, heights.Max() + Padding.VerticalThickness);
    }

    int ColumnsFor(double width) =>
        Math.Clamp((int)((width - Padding.HorizontalThickness + Spacing) / (MinColumnWidth + Spacing)), 1, Math.Max(1, MaxColumns));

    double ColumnWidth(double width, int columns) =>
        (width - Padding.HorizontalThickness - Spacing * (columns - 1)) / columns;

    // The measured items, in the columns they were measured in, over another width with as many columns: each keeps its
    // place and height, its column takes the new column width. Nothing is measured.
    void Spread(double width)
    {
        var columnWidth = ColumnWidth(width, _measuredColumns);
        for (var i = 0; i < _frames.Count && i < _columnOf.Count; i++)
        {
            if (_columnOf[i] < 0)
                continue;
            var frame = _frames[i];
            _frames[i] = new Rect(Padding.Left + _columnOf[i] * (columnWidth + Spacing), frame.Y, columnWidth, frame.Height);
        }
        _measuredWidth = width;
    }

    Size ILayoutManager.ArrangeChildren(Rect bounds)
    {
        // Arranged at another width than it was measured at. With as many columns there, the measured items are only
        // spread over it: measuring them again in the arrange pass, as this did, gave them another size whenever the
        // platform rounded the width to its pixels (a pixel or two at 150 %), the next measure pass gave the old one back,
        // and WinUI gave up with a layout cycle while the window was resized. Another number of columns is measured again;
        // that width is far enough from the measured one not to flip back.
        if (Math.Abs(bounds.Width - _measuredWidth) > 0.01)
        {
            if (ColumnsFor(bounds.Width) != _measuredColumns)
                ((ILayoutManager)this).Measure(bounds.Width, bounds.Height);
            else
                Spread(bounds.Width);
        }
        for (var i = 0; i < Count && i < _frames.Count; i++)
            this[i].Arrange(_frames[i].Offset(bounds.X, bounds.Y));
        return bounds.Size;
    }
}
