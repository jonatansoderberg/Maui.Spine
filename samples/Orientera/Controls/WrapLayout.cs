using Microsoft.Maui.Layouts;

namespace Orientera.Controls;

/// <summary>
/// Children laid out left to right, wrapping onto a new line when the row runs out.
/// </summary>
/// <remarks>
/// Twenty-four districts in a horizontal scroller is twenty-four chips behind a gesture nobody
/// makes. Wrapped, they are all on screen at once.
/// <para>
/// MAUI's own <c>FlexLayout</c> with <c>Wrap</c> draws this correctly and then refuses taps:
/// inside a vertical <c>ScrollView</c> its children land outside its measured bounds, and the
/// platform hit-tests the measurement rather than what is on screen. Two chip rows in this app
/// were built as horizontal scrollers to get around it. Measuring and arranging here fixes the
/// cause — the height reported is the height used.
/// </para>
/// </remarks>
public sealed class WrapLayout : Layout
{
    public static readonly BindableProperty SpacingProperty =
        BindableProperty.Create(nameof(Spacing), typeof(double), typeof(WrapLayout), 8.0,
            propertyChanged: (b, _, _) => ((WrapLayout)b).InvalidateMeasure());

    public static readonly BindableProperty LineSpacingProperty =
        BindableProperty.Create(nameof(LineSpacing), typeof(double), typeof(WrapLayout), 8.0,
            propertyChanged: (b, _, _) => ((WrapLayout)b).InvalidateMeasure());

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The gap between lines, which is not always the gap between chips.</summary>
    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    protected override ILayoutManager CreateLayoutManager() => new WrapLayoutManager(this);

    private sealed class WrapLayoutManager(WrapLayout _layout) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var padding = _layout.Padding;
            var content = Flow(widthConstraint - padding.HorizontalThickness, origin: null);

            return new Size(
                content.Width + padding.HorizontalThickness,
                content.Height + padding.VerticalThickness);
        }

        public Size ArrangeChildren(Rect bounds)
        {
            var padding = _layout.Padding;

            Flow(
                bounds.Width - padding.HorizontalThickness,
                new Point(padding.Left, padding.Top));

            return bounds.Size;
        }

        /// <summary>
        /// One pass over the children, measuring them into lines — and placing them too when an
        /// origin is given. Measure and arrange have to agree on where every child goes, so they
        /// are the same walk rather than two that look alike.
        /// </summary>
        private Size Flow(double available, Point? origin)
        {
            if (double.IsNaN(available) || available <= 0)
                available = double.PositiveInfinity;

            double x = 0, y = 0, lineHeight = 0, widest = 0;

            foreach (var child in _layout)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                var desired = child.Measure(double.PositiveInfinity, double.PositiveInfinity);

                // A child wider than the line still gets its own line rather than being squeezed
                // onto the end of the one before it.
                if (x > 0 && x + desired.Width > available)
                {
                    y += lineHeight + _layout.LineSpacing;
                    x = 0;
                    lineHeight = 0;
                }

                if (origin is { } start)
                    child.Arrange(new Rect(start.X + x, start.Y + y, desired.Width, desired.Height));

                x += desired.Width + _layout.Spacing;
                lineHeight = Math.Max(lineHeight, desired.Height);
                widest = Math.Max(widest, x - _layout.Spacing);
            }

            return new Size(widest, y + lineHeight);
        }
    }
}
