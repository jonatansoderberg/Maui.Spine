using Microsoft.Maui.Controls.Shapes;
using Orientera.Presentation;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Orientera.Controls;

/// <summary>
/// One collapsible group of choices: a heading, what is chosen right now, and a chevron.
/// </summary>
/// <remarks>
/// Collapsed by default. A filter with five groups laid out flat is a sheet nobody can see the
/// bottom of, and four of the five groups are almost always untouched. The summary line is what
/// makes closing them safe: a collapsed group still says "Mitt distrikt" or "Mästerskap +2", so
/// nothing set is ever hidden — only the twenty chips it was set from.
/// </remarks>
[ContentProperty(nameof(Body))]
public sealed class FilterSection : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(FilterSection), string.Empty,
            propertyChanged: (b, _, _) => ((FilterSection)b).Apply());

    public static readonly BindableProperty SummaryProperty =
        BindableProperty.Create(nameof(Summary), typeof(string), typeof(FilterSection), string.Empty,
            propertyChanged: (b, _, _) => ((FilterSection)b).Apply());

    public static readonly BindableProperty IsExpandedProperty =
        BindableProperty.Create(nameof(IsExpanded), typeof(bool), typeof(FilterSection), false,
            propertyChanged: (b, _, _) => ((FilterSection)b).Apply());

    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(View), typeof(FilterSection), null,
            propertyChanged: (b, _, _) => ((FilterSection)b).ApplyBody());

    private readonly Label _title = new();
    private readonly Label _summary = new();
    private readonly ContentView _bodySlot = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Grid _header;

    private readonly Path _chevron = new()
    {
        Data = new PathGeometryConverter().ConvertFromInvariantString(RowGlyph.Chevron) as Geometry,
        Aspect = Stretch.Uniform,
        WidthRequest = 14,
        HeightRequest = 14,
        StrokeThickness = 1.5,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.End,
    };

    public FilterSection()
    {
        _title.SetDynamicResource(StyleProperty, "SectionLabel");
        _summary.SetDynamicResource(StyleProperty, "CaptionLabel");
        _chevron.SetDynamicResource(Shape.StrokeProperty, "TextSecondary");

        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { _title, _summary },
        };

        _header = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 12,
            Children = { text, _chevron },
        };

        Grid.SetColumn(_chevron, 1);

        _header.SetDynamicResource(MinimumHeightRequestProperty, "TouchTargetMin");

        // The header is one element to a screen reader; its two labels would otherwise be read as
        // a heading and an unrelated word before the control that opens them is reached.
        foreach (var child in new View[] { _title, _summary, _chevron })
            AutomationProperties.SetIsInAccessibleTree(child, false);

        _header.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => IsExpanded = !IsExpanded),
        });

        Content = new VerticalStackLayout { Spacing = 0, Children = { _header, _bodySlot } };

        Apply();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What is chosen, in the words the chips use. "Alla nivåer" when nothing is.</summary>
    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public View? Body
    {
        get => (View?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    private void ApplyBody()
    {
        _bodySlot.Content = Body;
        Apply();
    }

    private void Apply()
    {
        _title.Text = Title;
        _summary.Text = Summary;
        _summary.IsVisible = Summary.Length > 0;

        _bodySlot.IsVisible = IsExpanded && Body is not null;

        // The chevron points the way the section will move: down to open, up to close.
        _chevron.Rotation = IsExpanded ? 270 : 90;

        SemanticProperties.SetDescription(_header,
            Summary.Length > 0 ? $"{Title}: {Summary}" : Title);

        SemanticProperties.SetHint(_header, IsExpanded ? "Dölj valen" : "Visa valen");
    }
}
