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

    protected override ILayoutManager CreateLayoutManager() => this;

    Size ILayoutManager.Measure(double widthConstraint, double heightConstraint)
    {
        var width = _measuredWidth = double.IsInfinity(widthConstraint) ? MinColumnWidth : widthConstraint;
        var inner = width - Padding.HorizontalThickness;
        var columns = Math.Clamp((int)((inner + Spacing) / (MinColumnWidth + Spacing)), 1, Math.Max(1, MaxColumns));
        var columnWidth = (inner - Spacing * (columns - 1)) / columns;
        var heights = new double[columns];

        _frames.Clear();
        _frames.AddRange(Enumerable.Repeat(Rect.Zero, Count));
        for (var index = 0; index < Count; index++)
        {
            var child = this[index];
            if (child.Visibility == Visibility.Collapsed)
                continue;
            var size = child.Measure(columnWidth, double.PositiveInfinity);
            var column = Array.IndexOf(heights, heights.Min());
            var top = heights[column] > 0 ? heights[column] + Spacing : 0;
            _frames[index] = new Rect(Padding.Left + column * (columnWidth + Spacing), Padding.Top + top, columnWidth, size.Height);
            heights[column] = top + size.Height;
        }
        return new Size(width, heights.Max() + Padding.VerticalThickness);
    }

    Size ILayoutManager.ArrangeChildren(Rect bounds)
    {
        if (Math.Abs(bounds.Width - _measuredWidth) > 0.5)
            ((ILayoutManager)this).Measure(bounds.Width, bounds.Height);
        for (var i = 0; i < Count && i < _frames.Count; i++)
            this[i].Arrange(_frames[i].Offset(bounds.X, bounds.Y));
        return bounds.Size;
    }
}
