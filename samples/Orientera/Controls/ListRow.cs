using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;
using Orientera.Presentation;

namespace Orientera.Controls;

/// <summary>
/// The one row shape: <c>[identity] [primary / secondary] [value] [→]</c> (P9). Column widths may
/// differ between views, the order never does.
/// </summary>
/// <remarks>
/// The value column carries two lines, not one. A live table and a result list both show a time
/// and something that qualifies it — a gap to the leader, a split — and giving that pair its own
/// place is what lets those lists keep the anatomy instead of growing a fifth column.
/// <para>
/// The row is one element to a screen reader. A card holding six labels is six swipes, so the
/// description sits on the row and the children are taken out of the tree. Anything tappable of
/// its own belongs outside the row for the same reason: a description on a layout makes its
/// children unreachable on iOS.
/// </para>
/// </remarks>
public sealed class ListRow : ContentView
{
    public static readonly BindableProperty IdentityProperty =
        BindableProperty.Create(nameof(Identity), typeof(View), typeof(ListRow), null,
            propertyChanged: (b, _, _) => ((ListRow)b).ApplyIdentity());

    public static readonly BindableProperty PrimaryProperty =
        BindableProperty.Create(nameof(Primary), typeof(string), typeof(ListRow), string.Empty,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty SecondaryProperty =
        BindableProperty.Create(nameof(Secondary), typeof(string), typeof(ListRow), string.Empty,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(ListRow), string.Empty,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty ValueDetailProperty =
        BindableProperty.Create(nameof(ValueDetail), typeof(string), typeof(ListRow), string.Empty,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty ShowChevronProperty =
        BindableProperty.Create(nameof(ShowChevron), typeof(bool), typeof(ListRow), true,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty IsHighlightedProperty =
        BindableProperty.Create(nameof(IsHighlighted), typeof(bool), typeof(ListRow), false,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(ListRow), string.Empty,
            propertyChanged: (b, _, _) => ((ListRow)b).Apply());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ListRow));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(ListRow));

    private readonly Grid _grid;
    private readonly ContentView _identitySlot = new() { VerticalOptions = LayoutOptions.Center };
    private readonly Label _primary = new();
    private readonly Label _secondary = new();
    private readonly Label _value = new() { HorizontalTextAlignment = TextAlignment.End };
    private readonly Label _valueDetail = new() { HorizontalTextAlignment = TextAlignment.End };
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
    };

    public ListRow()
    {
        _primary.SetDynamicResource(StyleProperty, "BodyStrongLabel");
        _secondary.SetDynamicResource(StyleProperty, "CaptionLabel");
        _value.SetDynamicResource(StyleProperty, "NumericStrongLabel");
        _valueDetail.SetDynamicResource(StyleProperty, "NumericCaptionLabel");
        _chevron.SetDynamicResource(Shape.StrokeProperty, "TextSecondary");

        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { _primary, _secondary },
        };

        var value = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children = { _value, _valueDetail },
        };

        _grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
            ColumnSpacing = 12,
            Children = { _identitySlot, text, value, _chevron },
        };

        Grid.SetColumn(text, 1);
        Grid.SetColumn(value, 2);
        Grid.SetColumn(_chevron, 3);

        _grid.SetDynamicResource(MinimumHeightRequestProperty, "TouchTargetMin");

        foreach (var child in new View[] { _primary, _secondary, _value, _valueDetail, _chevron })
            AutomationProperties.SetIsInAccessibleTree(child, false);

        Content = _grid;

        GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            }),
        });

        Apply();
    }

    public View? Identity
    {
        get => (View?)GetValue(IdentityProperty);
        set => SetValue(IdentityProperty, value);
    }

    public string Primary
    {
        get => (string)GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public string Secondary
    {
        get => (string)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The line under the value: a gap, a split, a placing — what qualifies it.</summary>
    public string ValueDetail
    {
        get => (string)GetValue(ValueDetailProperty);
        set => SetValue(ValueDetailProperty, value);
    }

    public bool ShowChevron
    {
        get => (bool)GetValue(ShowChevronProperty);
        set => SetValue(ShowChevronProperty, value);
    }

    /// <summary>The row that is the reader's own. Never the only thing that says so.</summary>
    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    /// <summary>What the screen reader says. Composed from the row's own text when left empty.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
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

    private void ApplyIdentity()
    {
        _identitySlot.Content = Identity;
        _identitySlot.IsVisible = Identity is not null;

        if (Identity is not null)
            AutomationProperties.SetIsInAccessibleTree(Identity, false);
    }

    private void Apply()
    {
        _primary.Text = Primary;
        _secondary.Text = Secondary;
        _value.Text = Value;
        _valueDetail.Text = ValueDetail;

        // Two pre-built styles rather than a trigger, for the reason ChipView documents: a trigger
        // remembers the colour it replaced and restores the old theme's after a swap.
        _primary.SetDynamicResource(StyleProperty, IsHighlighted ? "BodyAccentLabel" : "BodyStrongLabel");

        _secondary.IsVisible = !string.IsNullOrWhiteSpace(Secondary);
        _value.IsVisible = !string.IsNullOrWhiteSpace(Value);
        _valueDetail.IsVisible = !string.IsNullOrWhiteSpace(ValueDetail);
        _chevron.IsVisible = ShowChevron;

        SemanticProperties.SetDescription(this, string.IsNullOrWhiteSpace(Description)
            ? string.Join(", ", new[] { Primary, Secondary, Value, ValueDetail }
                .Where(s => !string.IsNullOrWhiteSpace(s)))
            : Description);
    }
}
