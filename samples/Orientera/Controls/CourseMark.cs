using Microsoft.Maui.Controls.Shapes;
using Orientera.Presentation;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Orientera.Controls;

/// <summary>
/// The course drawn into a card's own surface, behind its text.
/// </summary>
/// <remarks>
/// Decoration, and decoration that has to stay decoration: it sits under a heading and a button,
/// so it is drawn in <c>TopoInk</c> — white at low opacity — and taken out of the accessibility
/// tree. Nothing it shows is said only here.
/// </remarks>
public sealed class CourseMark : ContentView
{
    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(CourseMark), 150.0,
            propertyChanged: (b, _, _) => ((CourseMark)b).Apply());

    private readonly Path _path = new()
    {
        Data = new PathGeometryConverter().ConvertFromInvariantString(CourseGlyph.Course) as Geometry,
        Aspect = Stretch.Uniform,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    public CourseMark()
    {
        _path.SetDynamicResource(Shape.StrokeProperty, "TopoInk");

        AutomationProperties.SetIsInAccessibleTree(this, false);
        InputTransparent = true;

        Content = _path;

        Apply();
    }

    /// <summary>The side of the square the mark is drawn in.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Apply()
    {
        _path.WidthRequest = Size;
        _path.HeightRequest = Size;

        // The stroke is a share of the mark rather than a constant. A hairline that reads as a
        // route at 150 points is a scratch at 60, and one that reads at 60 is a rope at 150.
        _path.StrokeThickness = Math.Max(1.5, Size / 40);
    }
}
