using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Orientera.Presentation;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Orientera.Controls;

/// <summary>
/// The line above a block: what the block is, and — where there is more of it — the way there.
/// </summary>
/// <remarks>
/// The rubric moved out of the card and up here, which is what lets a card be a card rather than
/// a container with a label in it. The action beside it is a link and not a button: it changes
/// view, it performs nothing, and a second button by every rubric would have made the page's one
/// primary CTA into one of eight (§2.2).
/// <para>
/// The tap sits on the link alone, not on the header. A description on a layout makes its children
/// unreachable on iOS, and the rubric has to stay a heading a screen reader can jump between.
/// </para>
/// </remarks>
public sealed class SectionHeader : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(SectionHeader), string.Empty,
            propertyChanged: (b, _, _) => ((SectionHeader)b).Apply());

    public static readonly BindableProperty ActionTextProperty =
        BindableProperty.Create(nameof(ActionText), typeof(string), typeof(SectionHeader), string.Empty,
            propertyChanged: (b, _, _) => ((SectionHeader)b).Apply());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SectionHeader));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(SectionHeader));

    private readonly Label _title = new();
    private readonly Label _actionText = new();
    private readonly Path _chevron = new()
    {
        Data = new PathGeometryConverter().ConvertFromInvariantString(RowGlyph.Chevron) as Geometry,
        Aspect = Stretch.Uniform,
        WidthRequest = 11,
        HeightRequest = 11,
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        VerticalOptions = LayoutOptions.Center,
    };

    private readonly HorizontalStackLayout _action;

    public SectionHeader()
    {
        _title.SetDynamicResource(StyleProperty, "Heading2Label");
        _actionText.SetDynamicResource(StyleProperty, "LinkActionLabel");
        _chevron.SetDynamicResource(Shape.StrokeProperty, "AccentAction");

        _action = new HorizontalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children = { _actionText, _chevron },
        };

        // The link is one element that says where it goes; its two halves are not two stops.
        AutomationProperties.SetIsInAccessibleTree(_actionText, false);
        AutomationProperties.SetIsInAccessibleTree(_chevron, false);

        _action.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            }),
        });

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 12,
            Children = { _title, _action },
        };

        Grid.SetColumn(_action, 1);

        Content = grid;

        Apply();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The way to the rest of it: "Visa kalender", "Se alla". Empty leaves the line bare.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private void Apply()
    {
        _title.Text = Title;
        _actionText.Text = ActionText;

        _action.IsVisible = !string.IsNullOrWhiteSpace(ActionText);

        SemanticProperties.SetDescription(_action, ActionText);
    }
}
